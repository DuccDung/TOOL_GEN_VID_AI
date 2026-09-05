using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal sealed record VietsubOcrSettingsInput(
    string LanguageCode,
    string Profile,
    VietsubNormalizedRegion Region);

internal sealed record VietsubOcrPreviewResult(
    long TimestampMilliseconds,
    string Text,
    float Confidence,
    int FrameWidth,
    int FrameHeight);

internal sealed class VietsubOcrService(
    IVietsubLocalJobAuthorizer authorizer,
    IVietsubOcrSourceResolver mediaImportService,
    IVietsubOcrFrameReader frameReader,
    IVietsubOcrRecognizer recognizer,
    VietsubJobManager jobManager)
{
    public Task<VietsubOcrRuntimeStatus> GetRuntimeStatusAsync(CancellationToken cancellationToken) =>
        recognizer.GetRuntimeStatusAsync(cancellationToken);

    public Task AuthorizeProjectAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        authorizer.AuthorizeAsync(
            userId,
            organizationId,
            session.Manifest,
            cancellationToken);

    public async Task<VietsubOcrSettings> UpdateSettingsAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubOcrSettingsInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await authorizer.AuthorizeAsync(
            userId,
            organizationId,
            session.Manifest,
            cancellationToken);
        var settings = NormalizeSettings(input);
        await session.UpdateAsync(
            manifest => manifest.OcrSettings = settings,
            cancellationToken);
        await session.FlushAsync(cancellationToken);
        return settings;
    }

    public async Task<VietsubOcrPreviewResult> PreviewAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubOcrSettingsInput input,
        long timestampMilliseconds,
        CancellationToken cancellationToken)
    {
        var project = session.Manifest;
        await authorizer.AuthorizeAsync(userId, organizationId, project, cancellationToken);
        var media = RequireMedia(project);
        if (timestampMilliseconds < 0
            || timestampMilliseconds > (long)Math.Ceiling(media.Metadata.DurationSeconds * 1000))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.TimestampInvalid,
                "Timestamp quét thử nằm ngoài thời lượng video.");
        }
        var settings = NormalizeSettings(input);
        var runtime = await recognizer.GetRuntimeStatusAsync(cancellationToken);
        EnsureRuntime(runtime, settings.LanguageCode);
        var sourcePath = await mediaImportService.ResolveVerifiedSourcePathAsync(
            project.ProjectId,
            media,
            cancellationToken);
        var profile = VietsubOcrProfile.Resolve(settings.Profile);
        await foreach (var frame in frameReader.ReadAsync(
                           sourcePath,
                           media.Metadata.Width,
                           media.Metadata.Height,
                           media.Metadata.RotationDegrees,
                           settings.Region,
                           profile,
                           timestampMilliseconds,
                           cancellationToken))
        {
            var result = await recognizer.RecognizeAsync(
                frame,
                settings.LanguageCode,
                cancellationToken);
            return new(
                timestampMilliseconds,
                result.Text,
                result.Confidence,
                frame.Width,
                frame.Height);
        }
        throw new VietsubOcrException(
            VietsubOcrErrorCodes.FrameExtractionFailed,
            "Không đọc được frame tại timestamp đã chọn.");
    }

    public async Task<VietsubJobSummary> StartAsync(
        VietsubProjectSession session,
        string userId,
        Guid organizationId,
        VietsubOcrSettingsInput input,
        CancellationToken cancellationToken)
    {
        var project = session.Manifest;
        await authorizer.AuthorizeAsync(userId, organizationId, project, cancellationToken);
        var media = RequireMedia(project);
        var settings = NormalizeSettings(input);
        var runtime = await recognizer.GetRuntimeStatusAsync(cancellationToken);
        EnsureRuntime(runtime, settings.LanguageCode);
        _ = await mediaImportService.ResolveVerifiedSourcePathAsync(
            project.ProjectId,
            media,
            cancellationToken);
        var parameters = VietsubOcrJobParameters.Create(
            media.MediaId,
            media.Sha256,
            media.Metadata.DurationSeconds,
            media.Metadata.Width,
            media.Metadata.Height,
            media.Metadata.RotationDegrees,
            settings);

        await session.UpdateAsync(
            manifest => manifest.OcrSettings = settings,
            cancellationToken);
        await session.FlushAsync(cancellationToken);
        VietsubJobSummary job;
        try
        {
            job = await jobManager.EnqueueAsync(
                project.ProjectId,
                VietsubJobTypes.OcrLocal,
                ["OCR_PREPARE", "OCR_EXTRACT_FRAMES", "OCR_RECOGNIZE", "OCR_BUILD_CUES", "OCR_WRITE_ARTIFACT"],
                parameters.ToJson(),
                maxAttempts: 3,
                startImmediately: false,
                cancellationToken: cancellationToken);
        }
        catch (VietsubJobException exception) when (exception.Code == "vietsub_job_already_active")
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.JobAlreadyActive,
                "Dự án đã có một local job đang chờ, chạy hoặc tạm dừng.",
                exception);
        }

        try
        {
            await session.UpdateAsync(
                manifest => manifest.Status = VietsubProjectStatuses.Processing,
                cancellationToken);
            await session.FlushAsync(cancellationToken);
            await jobManager.StartAsync(
                project.ProjectId,
                job.Id,
                cancellationToken);
            return job;
        }
        catch
        {
            try
            {
                await jobManager.CancelAsync(project.ProjectId, job.Id, CancellationToken.None);
            }
            catch (Exception)
            {
                // Best-effort compensation; keep the original startup failure.
            }
            try
            {
                await session.UpdateAsync(
                    manifest => manifest.Status = VietsubProjectStatuses.Ready,
                    CancellationToken.None);
                await session.FlushAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // Best-effort compensation; keep the original startup failure.
            }
            throw;
        }
    }

    private static VietsubOcrSettings NormalizeSettings(VietsubOcrSettingsInput input)
    {
        var settings = new VietsubOcrSettings
        {
            LanguageCode = input.LanguageCode,
            Profile = input.Profile,
            Region = input.Region
        };
        settings.Normalize();
        return settings;
    }

    private static VietsubMediaReference RequireMedia(VietsubProjectManifest project)
    {
        var media = project.SourceVideo;
        if (media is null
            || !media.Metadata.HasVideo
            || media.Metadata.DurationSeconds <= 0
            || media.Metadata.Width <= 0
            || media.Metadata.Height <= 0)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.VideoNotReady,
                "Hãy nhập video nguồn hợp lệ trước khi chạy OCR.");
        }
        return media;
    }

    private static void EnsureRuntime(VietsubOcrRuntimeStatus runtime, string languageCode)
    {
        if (!runtime.Ready)
        {
            throw new VietsubOcrException(
                runtime.ErrorCode ?? VietsubOcrErrorCodes.RuntimeInvalid,
                runtime.Message);
        }
        if (!runtime.AvailableLanguages.Contains(languageCode, StringComparer.Ordinal))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.LanguageNotSupported,
                "Runtime OCR chưa cài model ngôn ngữ đã chọn.");
        }
    }
}
