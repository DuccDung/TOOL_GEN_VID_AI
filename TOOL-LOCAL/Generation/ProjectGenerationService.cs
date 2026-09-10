using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_LOCAL.Generation;

internal sealed class ProjectGenerationService(
    IDbContextFactory<VideoFactoryDbContext> dbContextFactory,
    ProjectWorkspaceService workspaceService,
    IGenerationClient apiClient,
    FfprobeService mediaProbe,
    IMediaToolPreflightService mediaToolPreflight,
    AudioQualityValidator audioQualityValidator,
    SceneAudioMixer sceneAudioMixer,
    SceneVideoTrimmer sceneVideoTrimmer,
    SpeechAudioExtractor? speechAudioExtractor = null) : IProjectGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private const string SceneAudioSyncPolicyVersion = "scene-audio-sync-v3";

    public Task<GenerationProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken) =>
        apiClient.GetProviderStatusAsync(cancellationToken);

    public async Task<VoiceProfileVersionSummary> CreateVoiceProfileDraftAsync(
        Guid projectId,
        string remoteUserId,
        string scope,
        Guid? characterId,
        string voiceCode,
        decimal speakingRate,
        CancellationToken cancellationToken)
    {
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            if (project.SpeechProductionPolicy != SpeechProductionPolicies.CanonicalVoice)
            {
                throw new ArgumentException("Project chưa bật Canonical Voice.");
            }
        }
        return await apiClient.CreateVoiceProfileDraftAsync(
            new CreateVoiceProfileDraftRequest(projectId, scope, voiceCode, speakingRate, characterId),
            cancellationToken);
    }

    public async Task<VoiceProfilePreviewQuoteResponse> GetVoiceProfilePreviewQuoteAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        CancellationToken cancellationToken)
    {
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        }
        return await apiClient.GetVoiceProfilePreviewQuoteAsync(
            new VoiceProfilePreviewQuoteRequest(projectId, voiceProfileVersionId, expectedVoiceSnapshotHash),
            cancellationToken);
    }

    public async Task<VoiceProfilePreviewResponse> GenerateVoiceProfilePreviewAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        CancellationToken cancellationToken)
    {
        string projectWorkspace;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            projectWorkspace = (await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken))
                .WorkspaceRelativePath;
        }
        var response = await apiClient.GenerateVoiceProfilePreviewAsync(
            new GenerateVoiceProfilePreviewRequest(
                projectId,
                voiceProfileVersionId,
                expectedVoiceSnapshotHash,
                $"voice-preview:{voiceProfileVersionId:N}:{expectedVoiceSnapshotHash}"),
            cancellationToken);
        if (response.VoiceProfileVersionId != voiceProfileVersionId ||
            response.MimeType != "audio/wav" || response.DurationMs <= 0)
        {
            throw new InvalidDataException("Server trả về preview giọng không hợp lệ.");
        }
        var relativePath = Path.Combine("voice-profiles", $"profile-{voiceProfileVersionId:N}.wav");
        var outputPath = workspaceService.Resolve(Path.Combine(
            projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
            relativePath));
        var partialPath = outputPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }
        try
        {
            await apiClient.DownloadVoiceProfilePreviewAsync(response, partialPath, cancellationToken);
            var probe = await mediaProbe.ProbeAsync(partialPath, cancellationToken);
            await audioQualityValidator.RequireAudibleAsync(
                partialPath,
                "Preview giọng tải về không nghe được",
                cancellationToken);
            if (!probe.HasAudio || probe.DurationSeconds <= 0 ||
                probe.AudioSampleRate != response.SampleRate)
            {
                throw new InvalidDataException("Preview giọng tải về không khớp metadata đã xác nhận.");
            }
            File.Move(partialPath, outputPath, true);
        }
        catch
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
            throw;
        }
        return response;
    }

    public async Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewQuoteAsync(
        Guid projectId,
        string remoteUserId,
        string voiceCode,
        decimal speakingRate,
        CancellationToken cancellationToken)
    {
        if (!OpenAiBuiltInVoiceCatalog.IsSupported(voiceCode))
        {
            throw new ArgumentException("Giọng nghe thử không hợp lệ.");
        }
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            _ = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        }
        return await apiClient.GetVoiceCatalogPreviewQuoteAsync(
            new VoiceCatalogPreviewQuoteRequest(projectId, voiceCode, speakingRate),
            cancellationToken);
    }

    public async Task<VoiceCatalogPreviewQuoteResponse> GetVoiceCatalogPreviewContextQuoteAsync(
        string voiceCode,
        decimal speakingRate,
        CancellationToken cancellationToken)
    {
        if (!OpenAiBuiltInVoiceCatalog.IsSupported(voiceCode))
        {
            throw new ArgumentException("Giọng nghe thử không hợp lệ.");
        }
        return await apiClient.GetVoiceCatalogPreviewContextQuoteAsync(
            new VoiceCatalogPreviewContextQuoteRequest(voiceCode, speakingRate),
            cancellationToken);
    }

    public async Task<VoiceCatalogPreviewPlayback> GenerateVoiceCatalogPreviewAsync(
        Guid projectId,
        string remoteUserId,
        string voiceCode,
        decimal speakingRate,
        string operationRequestId,
        CancellationToken cancellationToken)
    {
        if (!OpenAiBuiltInVoiceCatalog.IsSupported(voiceCode) ||
            string.IsNullOrWhiteSpace(operationRequestId) ||
            operationRequestId.Length > 100)
        {
            throw new ArgumentException("Yêu cầu nghe thử giọng không hợp lệ.");
        }

        string projectWorkspace;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            projectWorkspace = project.WorkspaceRelativePath;
        }

        var response = await apiClient.GenerateVoiceCatalogPreviewAsync(
            new GenerateVoiceCatalogPreviewRequest(
                projectId,
                voiceCode,
                speakingRate,
                $"voice-catalog-preview:{projectId:N}:{voiceCode}:{operationRequestId}"),
            cancellationToken);
        if (!string.Equals(
                OpenAiBuiltInVoiceCatalog.NormalizeSelection(response.VoiceCode),
                OpenAiBuiltInVoiceCatalog.NormalizeSelection(voiceCode),
                StringComparison.OrdinalIgnoreCase) ||
            response.MimeType != "audio/wav" ||
            response.DurationMs <= 0)
        {
            throw new InvalidDataException("Server trả về bản nghe thử giọng không hợp lệ.");
        }

        var relativePath = Path.Combine(
            "voice-catalog-previews",
            $"{response.VoiceCode}-{response.ProviderRequestId:N}.wav");
        var workspaceRelativePath = Path.Combine(
            projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
            relativePath);
        var outputPath = workspaceService.Resolve(workspaceRelativePath);
        var partialPath = outputPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }
        try
        {
            await apiClient.DownloadVoiceCatalogPreviewAsync(response, partialPath, cancellationToken);
            var probe = await mediaProbe.ProbeAsync(partialPath, cancellationToken);
            await audioQualityValidator.RequireAudibleAsync(
                partialPath,
                "Bản nghe thử giọng tải về không nghe được",
                cancellationToken);
            if (!probe.HasAudio || probe.DurationSeconds <= 0 ||
                probe.AudioSampleRate != response.SampleRate)
            {
                throw new InvalidDataException("Bản nghe thử giọng tải về không khớp metadata đã xác nhận.");
            }
            File.Move(partialPath, outputPath, true);
        }
        catch
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
            throw;
        }

        var urlPath = string.Join(
            '/',
            workspaceRelativePath
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return new VoiceCatalogPreviewPlayback(
            response.VoiceCode,
            response.SpeakingRate,
            $"https://media.app.local/{urlPath}",
            response.DurationMs,
            response.ActualCost,
            response.CurrencyCode);
    }

    public async Task<VoiceProfileVersionSummary> ApproveVoiceProfileVersionAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        bool playbackConfirmed,
        CancellationToken cancellationToken)
    {
        if (!playbackConfirmed)
        {
            throw new ArgumentException("Hãy phát và nghe preview giọng trước khi duyệt.");
        }
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        }
        return await apiClient.ApproveVoiceProfileVersionAsync(
            new ApproveVoiceProfileVersionRequest(
                projectId,
                voiceProfileVersionId,
                expectedVoiceSnapshotHash,
                PlaybackConfirmed: true),
            cancellationToken);
    }

    public async Task<VoiceProfileVersionSummary> SupersedeVoiceProfileVersionAsync(
        Guid projectId,
        string remoteUserId,
        Guid voiceProfileVersionId,
        string expectedVoiceSnapshotHash,
        CancellationToken cancellationToken)
    {
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        }
        return await apiClient.SupersedeVoiceProfileVersionAsync(
            new SupersedeVoiceProfileVersionRequest(projectId, voiceProfileVersionId, expectedVoiceSnapshotHash),
            cancellationToken);
    }

    public async Task<CanonicalVoiceQuoteSummary> GetCanonicalVoiceQuoteAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid> sceneIds,
        CancellationToken cancellationToken)
    {
        if (sceneIds.Count == 0 || sceneIds.Any(x => x == Guid.Empty))
        {
            throw new ArgumentException("Danh sách cảnh cần báo giá giọng không hợp lệ.");
        }
        var quotes = new List<SceneVoiceQuoteResponse>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        if (project.SpeechProductionPolicy != SpeechProductionPolicies.CanonicalVoice)
        {
            return new CanonicalVoiceQuoteSummary(0, project.CurrencyCode, 0, 0, []);
        }
        var requestedIds = sceneIds.Distinct().ToArray();
        var scenes = await dbContext.Scenes.AsNoTracking()
            .Where(x => x.ProjectId == projectId && requestedIds.Contains(x.SceneId))
            .Select(x => new
            {
                x.SceneId,
                x.ScenePlanVersion,
                x.Narration,
                x.Dialogue,
                x.CharacterIdsJson
            })
            .ToListAsync(cancellationToken);
        if (scenes.Count != requestedIds.Length)
        {
            throw new ArgumentException("Danh sách báo giá chứa cảnh không thuộc project.");
        }
        foreach (var scene in scenes)
        {
            var speech = NormalizeNarration(!string.IsNullOrWhiteSpace(scene.Dialogue) ? scene.Dialogue : scene.Narration);
            if (speech.Length == 0)
            {
                continue;
            }
            Guid? approvedVersionId;
            if (!string.IsNullOrWhiteSpace(scene.Dialogue))
            {
                var characterIds = ParseGuidList(scene.CharacterIdsJson);
                if (characterIds.Count != 1)
                {
                    throw new ArgumentException("Cảnh thoại trực diện phải gắn đúng một nhân vật để báo giá giọng.");
                }
                approvedVersionId = await dbContext.Characters.AsNoTracking()
                    .Where(x => x.ProjectId == projectId && x.CharacterId == characterIds[0])
                    .Select(x => x.ApprovedVoiceProfileVersionId)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            else
            {
                approvedVersionId = project.ApprovedNarratorVoiceProfileVersionId;
            }
            if (approvedVersionId is null)
            {
                throw new AccountClientException(
                    SpeechSynchronizationErrorCodes.VoiceProfileMissing,
                    "Hãy tạo, nghe thử và duyệt đầy đủ giọng narrator/nhân vật trước khi tạo video.",
                    409);
            }
            var snapshotHash = await dbContext.VoiceProfileVersions.AsNoTracking()
                .Where(x => x.VoiceProfileVersionId == approvedVersionId && x.Status == VoiceProfileVersionStatuses.Approved)
                .Select(x => x.SnapshotHash)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new AccountClientException(
                    SpeechSynchronizationErrorCodes.VoiceVersionNotApproved,
                    "Phiên bản giọng đang chọn không còn ở trạng thái đã duyệt.",
                    409);
            quotes.Add(await apiClient.GetSceneVoiceQuoteAsync(
                new SceneVoiceQuoteRequest(
                    projectId,
                    scene.SceneId,
                    scene.ScenePlanVersion,
                    Sha256Hex(speech),
                    ExpectedVoiceSnapshotHash: snapshotHash,
                    ExpectedVoiceProfileVersionId: approvedVersionId),
                cancellationToken));
        }
        var currency = quotes.Select(x => x.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (currency.Length > 1)
        {
            throw new InvalidDataException("Báo giá giọng trả về nhiều loại tiền tệ không tương thích.");
        }
        return new CanonicalVoiceQuoteSummary(
            quotes.Sum(x => x.EstimatedCost),
            currency.SingleOrDefault() ?? project.CurrencyCode,
            quotes.Count(x => !x.ReusesExistingGeneration),
            quotes.Count(x => x.ReusesExistingGeneration),
            quotes);
    }

    public async Task<SceneSpeechVerificationQuoteResponse> GetSceneSpeechVerificationQuoteAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var source = await ResolveSpeechVerificationSourceAsync(
            projectId,
            remoteUserId,
            sceneId,
            cancellationToken);
        return await apiClient.GetSceneSpeechVerificationQuoteAsync(
            new SceneSpeechVerificationQuoteRequest(
                projectId,
                source.Scene.SceneId,
                source.Scene.ScenePlanVersion,
                source.ExpectedSpeechHash,
                source.DurationMs),
            cancellationToken);
    }

    public async Task<SceneSpeechVerificationResponse> VerifySceneSpeechAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        if (speechAudioExtractor is null)
        {
            throw new InvalidOperationException("Công cụ trích audio để kiểm tra lời nói chưa sẵn sàng.");
        }

        var source = await ResolveSpeechVerificationSourceAsync(
            projectId,
            remoteUserId,
            sceneId,
            cancellationToken);
        var wavPath = source.ExtractAudio
            ? source.SourcePath + $".speech-{source.ExpectedSpeechHash[..12]}.wav"
            : source.SourcePath;
        try
        {
            var probe = source.ExtractAudio
                ? await speechAudioExtractor.ExtractAsync(source.SourcePath, wavPath, cancellationToken)
                : await mediaProbe.ProbeAsync(wavPath, cancellationToken);
            if (!probe.HasAudio || probe.DurationSeconds <= 0)
            {
                throw new InvalidDataException("Audio dùng để kiểm tra lời nói không hợp lệ.");
            }

            string mediaSha256;
            await using (var stream = new FileStream(
                             wavPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                mediaSha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            }

            var durationMs = checked((long)Math.Round(
                probe.DurationSeconds * 1000m,
                MidpointRounding.AwayFromZero));
            var response = await apiClient.VerifySceneSpeechAsync(
                new VerifySceneSpeechRequest(
                    projectId,
                    source.Scene.SceneId,
                    source.Scene.ScenePlanVersion,
                    source.ExpectedSpeechHash,
                    mediaSha256,
                    durationMs,
                    $"speech-verification:{source.Scene.SceneId:N}:v{source.Scene.ScenePlanVersion}:{source.ExpectedSpeechHash}:{mediaSha256}",
                    SourceMediaAssetId: source.SourceMediaAssetId),
                wavPath,
                cancellationToken);

            await using (var writeContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                await RequireProjectAsync(writeContext, projectId, remoteUserId, cancellationToken);
                var scene = await writeContext.Scenes.SingleAsync(
                    x => x.SceneId == source.Scene.SceneId,
                    cancellationToken);
                scene.SpeechStatus = response.Status is SpeechVerificationStatuses.Passed or SpeechVerificationStatuses.NeedsReview
                    ? SceneSpeechStatuses.SpeechReviewRequired
                    : SceneSpeechStatuses.SpeechInvalid;
                scene.Status = response.Status is SpeechVerificationStatuses.Passed or SpeechVerificationStatuses.NeedsReview
                    ? "AudioReviewRequired"
                    : "NativeAudioInvalid";
                scene.LastErrorCode = response.Status == SpeechVerificationStatuses.Failed
                    ? "speech_verification_failed"
                    : null;
                scene.LastErrorMessage = response.Status == SpeechVerificationStatuses.Failed
                    ? "Transcript không khớp lời nói đã khóa. Hãy tạo lại audio hoặc clip."
                    : null;
                scene.UpdatedAtUtc = DateTime.UtcNow;
                await writeContext.SaveChangesAsync(cancellationToken);
            }

            return response;
        }
        finally
        {
            if (source.ExtractAudio && File.Exists(wavPath))
            {
                File.Delete(wavPath);
            }
        }
    }

    public async Task<SceneSpeechVerificationResponse> ApproveSpeechVerificationReviewAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        Guid speechVerificationReportId,
        string reason,
        string expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        }
        return await apiClient.ApproveSpeechVerificationReviewAsync(
            new ApproveSpeechVerificationReviewRequest(
                projectId,
                sceneId,
                speechVerificationReportId,
                reason,
                expectedRowVersion),
            cancellationToken);
    }

    public async Task<GeneratedContentResponse> GenerateContentAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken)
    {
        int version;
        string idempotencyKey;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(readContext, projectId, remoteUserId, cancellationToken);
            version = (await readContext.Scripts
                .Where(x => x.ProjectId == projectId)
                .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
            var languagePolicyVersion = KlingLongFormVietnameseValidator.ResolvePolicyVersion(project.VideoProviderCode);
            var keyPrefix = $"content:{projectId:N}:v{version}:{languagePolicyVersion}";
            var failedAttempts = await readContext.ProviderRequests
                .AsNoTracking()
                .CountAsync(
                    x => x.ProjectId == projectId &&
                         x.RequestKind == "Text" &&
                         x.Status == "Failed" &&
                         x.IdempotencyKey.StartsWith(keyPrefix),
                    cancellationToken);
            idempotencyKey = failedAttempts == 0
                ? keyPrefix
                : $"{keyPrefix}:retry:{failedAttempts}";
        }

        var response = await apiClient.GenerateContentAsync(
            new GenerateContentRequest(projectId, idempotencyKey),
            cancellationToken);
        ValidateContentPlan(response.Plan);
        await PersistContentPlanAsync(projectId, remoteUserId, version, response, cancellationToken);
        await apiClient.MaterializeProjectAssetPlanAsync(
            projectId,
            new MaterializeProjectAssetPlanRequest(response.ProviderRequestId, version),
            cancellationToken);
        return response;
    }

    public Task<ContentLanguageFailureResponse?> GetLatestContentLanguageFailureAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        apiClient.GetLatestContentLanguageFailureAsync(projectId, cancellationToken);

    public Task<ContentRepairQuoteResponse> GetContentRepairQuoteAsync(
        Guid projectId,
        Guid failedProviderRequestId,
        CancellationToken cancellationToken) =>
        apiClient.GetContentRepairQuoteAsync(
            new ContentRepairQuoteRequest(projectId, failedProviderRequestId),
            cancellationToken);

    public async Task<GeneratedContentResponse> RepairContentAsync(
        Guid projectId,
        string remoteUserId,
        Guid failedProviderRequestId,
        CancellationToken cancellationToken)
    {
        int version;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(readContext, projectId, remoteUserId, cancellationToken);
            version = (await readContext.Scripts
                .Where(x => x.ProjectId == projectId)
                .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        }

        var response = await apiClient.RepairContentAsync(
            new RepairContentRequest(
                projectId,
                failedProviderRequestId,
                $"content-repair:{failedProviderRequestId:N}"),
            cancellationToken);
        ValidateContentPlan(response.Plan);
        await PersistContentPlanAsync(projectId, remoteUserId, version, response, cancellationToken);
        await apiClient.MaterializeProjectAssetPlanAsync(
            projectId,
            new MaterializeProjectAssetPlanRequest(response.ProviderRequestId, version),
            cancellationToken);
        return response;
    }

    public async Task<MaterializeProjectAssetPlanResponse> SynchronizeProjectAssetPlanAsync(
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        var scenePlanVersion = project.CurrentScenePlanVersion
            ?? throw new ArgumentException("Dự án chưa có scene plan để đồng bộ tài sản AI.");
        var providerRequestId = await dbContext.ProviderRequests
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        (x.RequestKind == "Text" || x.RequestKind == "TextRepair") &&
                        x.Status == "Completed")
            .OrderByDescending(x => x.CompletedAtUtc)
            .Select(x => (Guid?)x.ProviderRequestId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy content plan AI của phiên bản cảnh hiện hành.");
        return await apiClient.MaterializeProjectAssetPlanAsync(
            projectId,
            new MaterializeProjectAssetPlanRequest(providerRequestId, scenePlanVersion),
            cancellationToken);
    }

    public async Task<GenerateCharacterReferenceImageResponse> GenerateCharacterReferenceImageAsync(
        Guid projectId,
        string remoteUserId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Nhân vật cần tạo ảnh không hợp lệ.");
        }

        string projectWorkspace;
        string characterKey;
        string characterName;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(readContext, projectId, remoteUserId, cancellationToken);
            var character = await readContext.Characters.AsNoTracking().SingleOrDefaultAsync(
                x => x.CharacterId == characterId && x.ProjectId == projectId,
                cancellationToken)
                ?? throw new ArgumentException("Không tìm thấy nhân vật trong dự án hiện tại.");
            if (character.Status != "Draft")
            {
                throw new ArgumentException("Nhân vật đã khóa nên không thể tạo hoặc sinh lại ảnh.");
            }
            projectWorkspace = project.WorkspaceRelativePath;
            characterKey = character.CharacterKey;
            characterName = character.Name;
        }

        var response = await apiClient.GenerateCharacterReferenceImageAsync(
            new GenerateCharacterReferenceImageRequest(
                projectId,
                characterId,
                $"character-image:{characterId:N}:{Guid.NewGuid():N}"),
            cancellationToken);
        ValidateImageMetadata(response);

        await using (var existingContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (await existingContext.MediaAssets.AsNoTracking().AnyAsync(
                x => x.SourceProviderRequestId == response.ProviderRequestId,
                cancellationToken))
            {
                return response;
            }
        }

        var fileName = $"character-{characterKey}-{response.ProviderRequestId:N}.png";
        var assetRelativePath = Path.Combine("characters", fileName).Replace(Path.DirectorySeparatorChar, '/');
        var workspaceRelativePath = Path.Combine(projectWorkspace, "characters", fileName);
        var finalPath = workspaceService.Resolve(workspaceRelativePath);
        var partialPath = $"{finalPath}.part";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        var movedToFinal = false;
        try
        {
            await apiClient.DownloadCharacterImageAsync(response, partialPath, cancellationToken);
            await ValidateDownloadedCharacterImageAsync(partialPath, response, cancellationToken);
            File.Move(partialPath, finalPath, false);
            movedToFinal = true;

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var character = await dbContext.Characters
                .Include(x => x.CharacterReferences)
                .Include(x => x.Project)
                .SingleOrDefaultAsync(
                    x => x.CharacterId == characterId &&
                         x.ProjectId == projectId &&
                         x.Project.RemoteUserId == remoteUserId &&
                         x.Project.DeletedAtUtc == null,
                    cancellationToken)
                ?? throw new ArgumentException("Không tìm thấy nhân vật trong dự án hiện tại.");
            if (character.Status != "Draft")
            {
                throw new ArgumentException("Nhân vật đã khóa nên không thể thay ảnh tham chiếu.");
            }

            var duplicate = await dbContext.MediaAssets.SingleOrDefaultAsync(
                x => x.SourceProviderRequestId == response.ProviderRequestId,
                cancellationToken);
            if (duplicate is null)
            {
                foreach (var current in character.CharacterReferences.Where(x => x.IsPrimary))
                {
                    current.IsPrimary = false;
                }

                var now = DateTime.UtcNow;
                var asset = new MediaAsset
                {
                    MediaAssetId = Guid.NewGuid(),
                    ProjectId = projectId,
                    AssetType = "CharacterReference",
                    DisplayName = $"Ảnh AI tham chiếu {characterName}",
                    RelativePath = assetRelativePath,
                    MimeType = response.MimeType,
                    SizeBytes = response.SizeBytes,
                    Sha256 = response.Sha256,
                    Width = response.Width,
                    Height = response.Height,
                    Status = "Ready",
                    SourceType = "Generated",
                    SourceProviderCode = response.ProviderCode,
                    SourceProviderRequestId = response.ProviderRequestId,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        character.CharacterKey,
                        referenceType = "Front",
                        response.ModelCode,
                        response.ProviderRequestId
                    }, JsonOptions),
                    CreatedAtUtc = now,
                    VerifiedAtUtc = now
                };
                dbContext.MediaAssets.Add(asset);
                dbContext.CharacterReferences.Add(new CharacterReference
                {
                    CharacterReferenceId = Guid.NewGuid(),
                    CharacterId = character.CharacterId,
                    MediaAssetId = asset.MediaAssetId,
                    ReferenceType = "Front",
                    IsPrimary = true,
                    ApprovalStatus = "Approved",
                    CreatedAtUtc = now,
                    ApprovedAtUtc = now
                });
                character.Project.UpdatedAtUtc = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            if (movedToFinal && File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            throw;
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static void ValidateImageMetadata(GenerateCharacterReferenceImageResponse response)
    {
        if (response.ProviderRequestId == Guid.Empty ||
            response.ProviderCode != "openai" ||
            response.ModelCode != "gpt-image-2" ||
            response.MimeType != "image/png" ||
            response.Width != 1024 ||
            response.Height != 1024 ||
            response.SizeBytes is <= 0 or > 10 * 1024 * 1024 ||
            response.Sha256.Length != 64 ||
            response.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Metadata ảnh GPT-Image-2 không hợp lệ.");
        }
    }

    private static async Task ValidateDownloadedCharacterImageAsync(
        string path,
        GenerateCharacterReferenceImageResponse response,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || fileInfo.Length != response.SizeBytes)
        {
            throw new InvalidDataException("Ảnh GPT-Image-2 tải về không đúng dung lượng.");
        }

        var header = new byte[24];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.ReadExactlyAsync(header, cancellationToken);
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (!header.AsSpan(0, 8).SequenceEqual(pngSignature) ||
            width != response.Width ||
            height != response.Height)
        {
            throw new InvalidDataException("Chữ ký MIME hoặc kích thước ảnh GPT-Image-2 không hợp lệ.");
        }
    }

    public Task<SceneFirstFrameQuoteResponse> GetSceneFirstFrameQuoteAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken) =>
        apiClient.GetSceneFirstFrameQuoteAsync(projectId, sceneId, cancellationToken);

    public async Task<SceneFirstFrameListResponse> GetSceneFirstFramesAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var response = await apiClient.GetSceneFirstFramesAsync(projectId, sceneId, cancellationToken);
        return response with
        {
            Frames = response.Frames.Select(frame => frame with
            {
                PreviewUrl = CreateFirstFramePreviewUrl(frame.RelativePath)
            }).ToArray()
        };
    }

    public async Task<ProjectSceneFirstFrameListResponse> GetProjectSceneFirstFramesAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var response = await apiClient.GetProjectSceneFirstFramesAsync(projectId, cancellationToken);
        return response with
        {
            Frames = response.Frames.Select(frame => frame with
            {
                PreviewUrl = CreateFirstFramePreviewUrl(frame.RelativePath)
            }).ToArray()
        };
    }

    public async Task<SceneFirstFrameSummary> GenerateSceneFirstFrameAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (sceneId == Guid.Empty || attempt <= 0)
        {
            throw new ArgumentException("Scene hoặc attempt tạo first-frame không hợp lệ.");
        }

        string projectWorkspace;
        string aspectRatio;
        int scenePlanVersion;
        PromptWorkItem prompt;
        ReferenceWorkItem? reference = null;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            var scene = await dbContext.Scenes.AsNoTracking()
                .Where(x => x.SceneId == sceneId && x.ProjectId == projectId)
                .Select(x => new
                {
                    x.ScenePlanVersion,
                    x.CharacterIdsJson,
                    Prompt = x.ScenePrompts
                        .Where(p => p.Status == "Approved" || p.Status == "Ready")
                        .OrderByDescending(p => p.Version)
                        .Select(p => new PromptWorkItem(p.ScenePromptId, p.Version, p.FinalPrompt, p.NegativePrompt))
                        .FirstOrDefault()
                })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new ArgumentException("Không tìm thấy cảnh trong dự án.");
            prompt = scene.Prompt ?? throw new ArgumentException("Cảnh chưa có prompt được duyệt.");
            scenePlanVersion = project.CurrentScenePlanVersion is { } current && current == scene.ScenePlanVersion
                ? current
                : throw new ArgumentException("Kế hoạch cảnh đã thay đổi. Hãy tải lại dự án.");
            var characterIds = ParseGuidList(scene.CharacterIdsJson);
            if (characterIds.Count > 1)
            {
                throw new ArgumentException("Cảnh first-frame chỉ hỗ trợ tối đa một nhân vật.");
            }
            if (characterIds.Count == 1)
            {
                var character = await dbContext.Characters.AsNoTracking()
                    .Where(x => x.CharacterId == characterIds[0] && x.ProjectId == projectId)
                    .Select(x => new
                    {
                        x.Status,
                        Reference = x.CharacterReferences
                            .Where(r => r.IsPrimary && r.ApprovalStatus == "Approved" &&
                                        r.MediaAsset.Status == "Ready" && r.MediaAsset.DeletedAtUtc == null)
                            .OrderByDescending(r => r.CreatedAtUtc)
                            .Select(r => new ReferenceWorkItem(
                                r.CharacterReferenceId,
                                r.MediaAsset.RelativePath,
                                r.MediaAsset.MimeType,
                                r.MediaAsset.Sha256,
                                r.MediaAsset.SizeBytes))
                            .FirstOrDefault()
                    })
                    .SingleOrDefaultAsync(cancellationToken);
                if (character is null || character.Status != "Approved" || character.Reference is null)
                {
                    throw new ArgumentException("Hãy khóa nhân vật và duyệt ảnh primary trước khi tạo first-frame.");
                }
                reference = character.Reference;
            }
            projectWorkspace = project.WorkspaceRelativePath;
            aspectRatio = project.AspectRatio;
        }

        var assetLibrary = await apiClient.GetProjectAssetLibraryAsync(projectId, cancellationToken);
        var assignment = assetLibrary.SceneAssignments.SingleOrDefault(x => x.SceneId == sceneId);
        var assignedIds = assignment?.ProjectAssetIds ?? [];
        var assetVersions = assetLibrary.Assets
            .Where(x => assignedIds.Contains(x.ProjectAssetId))
            .OrderBy(x => x.ProjectAssetId)
            .Select(x => $"{x.ProjectAssetId:N}:{x.CurrentVersion}");
        var assetVersionHash = Sha256Hex(string.Join('|', assetVersions));
        SceneFirstFrameCharacterInput? characterInput = null;
        if (reference is not null)
        {
            var loaded = await LoadReferenceImageAsync(projectWorkspace, reference, cancellationToken);
            characterInput = new SceneFirstFrameCharacterInput(
                loaded.CharacterReferenceId,
                loaded.MimeType,
                loaded.Base64Data,
                loaded.Sha256);
        }
        var idempotencyKey =
            $"scene-first-frame:{prompt.ScenePromptId:N}:{prompt.Version}:{reference?.CharacterReferenceId.ToString("N") ?? "none"}:{assetVersionHash}:{aspectRatio}:{attempt}";
        var response = await apiClient.GenerateSceneFirstFrameAsync(
            new GenerateSceneFirstFrameRequest(
                projectId,
                sceneId,
                scenePlanVersion,
                prompt.Version,
                idempotencyKey,
                CharacterReference: characterInput,
                Attempt: attempt),
            cancellationToken);
        ValidateSceneFirstFrameMetadata(response, aspectRatio);

        var existing = (await apiClient.GetSceneFirstFramesAsync(projectId, sceneId, cancellationToken)).Frames
            .SingleOrDefault(x => x.ProviderRequestId == response.ProviderRequestId);
        if (existing is not null)
        {
            return existing;
        }

        var extension = response.MimeType == "image/png" ? ".png" : ".jpg";
        var fileName = $"first-frame-{response.ProviderRequestId:N}{extension}";
        var workspaceRelativePath = Path.Combine(
            projectWorkspace,
            "scenes",
            sceneId.ToString("N"),
            "first-frames",
            fileName);
        var normalizedRelativePath = workspaceRelativePath.Replace(Path.DirectorySeparatorChar, '/');
        var finalPath = workspaceService.Resolve(workspaceRelativePath);
        var partialPath = $"{finalPath}.part";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        if (File.Exists(finalPath))
        {
            await ValidateDownloadedSceneFirstFrameAsync(finalPath, response, cancellationToken);
        }
        else
        {
            try
            {
                await apiClient.DownloadSceneFirstFrameAsync(response, partialPath, cancellationToken);
                await ValidateDownloadedSceneFirstFrameAsync(partialPath, response, cancellationToken);
                File.Move(partialPath, finalPath, false);
            }
            finally
            {
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }
            }
        }

        return await apiClient.MaterializeSceneFirstFrameAsync(
            projectId,
            sceneId,
            new MaterializeSceneFirstFrameRequest(
                response.ProviderRequestId,
                normalizedRelativePath,
                response.MimeType,
                response.Sha256,
                response.SizeBytes,
                response.Width,
                response.Height),
            cancellationToken);
    }

    public Task<SceneFirstFrameSummary> ApproveSceneFirstFrameAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        apiClient.ApproveSceneFirstFrameAsync(
            projectId,
            sceneId,
            frameId,
            new ChangeSceneFirstFrameStatusRequest(rowVersion),
            cancellationToken);

    public Task<SceneFirstFrameSummary> RejectSceneFirstFrameAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        apiClient.RejectSceneFirstFrameAsync(
            projectId,
            sceneId,
            frameId,
            new ChangeSceneFirstFrameStatusRequest(rowVersion),
            cancellationToken);

    public async Task<SceneFirstFrameSummary> RetrySceneFirstFrameDownloadAsync(
        Guid projectId,
        Guid sceneId,
        Guid frameId,
        CancellationToken cancellationToken)
    {
        var frame = (await apiClient.GetSceneFirstFramesAsync(projectId, sceneId, cancellationToken)).Frames
            .SingleOrDefault(x => x.SceneFirstFrameId == frameId)
            ?? throw new ArgumentException("Không tìm thấy first-frame cần tải lại.");
        var response = new GenerateSceneFirstFrameResponse(
            frame.ProviderRequestId,
            "openai",
            "gpt-image-2",
            $"/api/generation/images/scene-first-frames/{frame.ProviderRequestId:D}/content",
            frame.MimeType,
            frame.Sha256,
            frame.Width,
            frame.Height,
            frame.SizeBytes,
            0,
            0,
            0,
            "USD",
            DateTime.MaxValue);
        var finalPath = workspaceService.Resolve(frame.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var partialPath = $"{finalPath}.part";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (File.Exists(finalPath))
        {
            await ValidateDownloadedSceneFirstFrameAsync(finalPath, response, cancellationToken);
            return frame with { PreviewUrl = CreateFirstFramePreviewUrl(frame.RelativePath) };
        }
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }
        try
        {
            await apiClient.DownloadSceneFirstFrameAsync(response, partialPath, cancellationToken);
            await ValidateDownloadedSceneFirstFrameAsync(partialPath, response, cancellationToken);
            File.Move(partialPath, finalPath, false);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
        return frame with { PreviewUrl = CreateFirstFramePreviewUrl(frame.RelativePath) };
    }

    private static void ValidateSceneFirstFrameMetadata(GenerateSceneFirstFrameResponse response, string aspectRatio)
    {
        var expected = aspectRatio == "9:16" ? (Width: 720, Height: 1280) : (Width: 1280, Height: 720);
        if (response.ProviderRequestId == Guid.Empty || response.ProviderCode != "openai" ||
            response.ModelCode != "gpt-image-2" || response.MimeType is not ("image/png" or "image/jpeg") ||
            response.Width != expected.Width || response.Height != expected.Height ||
            response.SizeBytes is <= 0 or > 8L * 1024 * 1024 || response.Sha256.Length != 64 ||
            response.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Metadata first-frame GPT-Image-2 không hợp lệ.");
        }
    }

    private static async Task ValidateDownloadedSceneFirstFrameAsync(
        string path,
        GenerateSceneFirstFrameResponse response,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || fileInfo.Length != response.SizeBytes || fileInfo.Length > 8L * 1024 * 1024)
        {
            throw new InvalidDataException("First-frame tải về không đúng dung lượng.");
        }
        var bytes = new byte[fileInfo.Length];
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }
        var (mimeType, width, height) = ReadImageInfo(bytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (mimeType != response.MimeType || width != response.Width || height != response.Height ||
            !string.Equals(sha256, response.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("MIME, kích thước hoặc SHA-256 của first-frame không hợp lệ.");
        }
    }

    private static (string MimeType, int Width, int Height) ReadImageInfo(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(pngSignature))
        {
            return ("image/png", BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)));
        }
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            var offset = 2;
            while (offset + 4 <= bytes.Length)
            {
                if (bytes[offset++] != 0xFF)
                {
                    continue;
                }
                var marker = bytes[offset++];
                if (marker is 0xD8 or 0xD9)
                {
                    continue;
                }
                if (offset + 2 > bytes.Length)
                {
                    break;
                }
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
                if (length < 2 || offset + length > bytes.Length)
                {
                    break;
                }
                if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                {
                    return (
                        "image/jpeg",
                        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2)),
                        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2)));
                }
                offset += length;
            }
        }
        throw new InvalidDataException("First-frame không phải PNG/JPEG hợp lệ.");
    }

    private string? CreateFirstFramePreviewUrl(string relativePath)
    {
        try
        {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var resolved = workspaceService.Resolve(normalized);
            if (!File.Exists(resolved))
            {
                return null;
            }
            var urlPath = string.Join(
                '/',
                normalized.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            return $"https://media.app.local/{urlPath}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<int> GenerateVideosAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid>? sceneIds,
        Func<string, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        var requestedSceneIds = sceneIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        if (sceneIds is not null && (requestedSceneIds is null || requestedSceneIds.Length == 0))
        {
            throw new ArgumentException("Hãy chọn ít nhất một cảnh để tạo video.");
        }
        if (requestedSceneIds?.Length > 100)
        {
            throw new ArgumentException("Mỗi lần chỉ được tạo tối đa 100 cảnh.");
        }

        try
        {
            // Local media verification is mandatory after the provider completes. Check it
            // before any provider submit so a missing executable cannot create an
            // avoidable paid request.
            await mediaToolPreflight.RequireReadyAsync(cancellationToken);
        }
        catch (MediaToolUnavailableException exception)
        {
            await MarkMediaToolBlockedAsync(
                projectId,
                remoteUserId,
                requestedSceneIds,
                exception,
                cancellationToken);
            throw;
        }

        IReadOnlyList<SceneWorkItem> scenes;
        string speechProductionPolicy;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            speechProductionPolicy = project.SpeechProductionPolicy;
            var planVersion = project.CurrentScenePlanVersion
                ?? throw new ArgumentException("Hãy tạo nội dung OpenAI trước khi tạo video.");
            var sceneQuery = dbContext.Scenes
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == planVersion);
            if (requestedSceneIds is not null)
            {
                sceneQuery = sceneQuery.Where(x => requestedSceneIds.Contains(x.SceneId));
            }

            var rawScenes = await sceneQuery
                .OrderBy(x => x.SequenceNumber)
                .Select(x => new SceneWorkItem(
                    x.SceneId,
                    x.ScriptId,
                    x.ScenePlanVersion,
                    x.SequenceNumber,
                    x.ContentDurationMs,
                    x.GenerationDurationMs,
                    x.Narration,
                    x.Dialogue,
                    x.RequiredCapabilitiesJson,
                    x.Status,
                    x.CharacterIdsJson,
                    x.ScenePrompts
                        .OrderByDescending(prompt => prompt.Version)
                        .Select(prompt => new PromptWorkItem(
                            prompt.ScenePromptId,
                            prompt.Version,
                            prompt.FinalPrompt,
                            prompt.NegativePrompt))
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken);
            var characterIdsByScene = rawScenes.ToDictionary(
                scene => scene.SceneId,
                scene => ParseGuidList(scene.CharacterIdsJson));
            if (characterIdsByScene.Values.Any(ids => ids.Count > 1))
            {
                throw new ArgumentException("Workflow video hiện hỗ trợ tối đa một nhân vật tham chiếu trong mỗi cảnh.");
            }

            var characterIds = characterIdsByScene.Values.SelectMany(ids => ids).Distinct().ToArray();
            var characterRows = characterIds.Length == 0
                ? []
                : await dbContext.Characters
                    .AsNoTracking()
                    .Where(character => character.ProjectId == projectId && characterIds.Contains(character.CharacterId))
                    .Select(character => new CharacterWorkItem(
                        character.CharacterId,
                        character.Name,
                        character.Status,
                        character.VoiceCode,
                        character.VoiceSpeakingRate,
                        character.ApprovedVoiceProfileVersionId,
                        character.CharacterReferences
                            .Where(reference =>
                                reference.IsPrimary &&
                                reference.ApprovalStatus == "Approved" &&
                                reference.MediaAsset.Status == "Ready" &&
                                reference.MediaAsset.DeletedAtUtc == null)
                            .OrderByDescending(reference => reference.CreatedAtUtc)
                            .Select(reference => new ReferenceWorkItem(
                                reference.CharacterReferenceId,
                                reference.MediaAsset.RelativePath,
                                reference.MediaAsset.MimeType,
                                reference.MediaAsset.Sha256,
                                reference.MediaAsset.SizeBytes))
                            .FirstOrDefault()))
                    .ToListAsync(cancellationToken);
            var characterById = characterRows.ToDictionary(character => character.CharacterId);
            scenes = rawScenes
                .Select(scene =>
                {
                    var ids = characterIdsByScene[scene.SceneId];
                    return scene with
                    {
                        Character = ids.Count == 0 || !characterById.TryGetValue(ids[0], out var character)
                            ? null
                            : character
                    };
                })
                .ToArray();
            if (scenes.Count == 0 || scenes.Any(x => x.Prompt is null))
            {
                throw new ArgumentException("Dự án chưa có đủ prompt cho các cảnh.");
            }
            if (requestedSceneIds is not null && scenes.Count != requestedSceneIds.Length)
            {
                throw new ArgumentException("Danh sách cảnh chứa cảnh không thuộc kế hoạch hiện hành.");
            }

            var sceneById = scenes.ToDictionary(scene => scene.SceneId);
            if (characterIdsByScene.Any(pair => pair.Value.Count > 0 && sceneById[pair.Key].Character is null) ||
                scenes.Any(scene => scene.Character is { Status: not "Approved" } || scene.Character is { Reference: null }))
            {
                throw new ArgumentException("Hãy khóa nhân vật và chọn ảnh tham chiếu trước khi tạo video.");
            }

            project.Status = "GeneratingScenes";
            project.LastErrorCode = null;
            project.LastErrorMessage = null;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var completed = 0;
        try
        {
            foreach (var scene in scenes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsSceneApprovedAsync(scene.SceneId, cancellationToken))
                {
                    completed++;
                    continue;
                }

                if (speechProductionPolicy == SpeechProductionPolicies.CanonicalVoice &&
                    !string.IsNullOrWhiteSpace(scene.SpokenText))
                {
                    if (reportProgress is not null)
                    {
                        await reportProgress(
                            $"Đang chuẩn bị giọng chuẩn cho cảnh {scene.SequenceNumber}/{scenes.Count}...",
                            cancellationToken);
                    }
                    await EnsureSceneNarrationAsync(
                        projectId,
                        remoteUserId,
                        scene,
                        cancellationToken);
                    if (!await IsCanonicalVoiceApprovedAsync(scene, cancellationToken))
                    {
                        if (reportProgress is not null)
                        {
                            await reportProgress(
                                scene.SpeechMode == KlingSpeechModes.NativeVoiceOver
                                    ? $"Cảnh {scene.SequenceNumber} đã có WAV; chọn lại cảnh để tạo video nền mà không gọi TTS mới."
                                    : $"Cảnh {scene.SequenceNumber} đang chờ nghe và duyệt WAV trước khi tạo video nền.",
                                cancellationToken);
                        }
                        continue;
                    }
                }

                if (reportProgress is not null)
                {
                    await reportProgress(
                        $"Đang tạo video cảnh {scene.SequenceNumber}/{scenes.Count}...",
                        cancellationToken);
                }
                await GenerateSceneAsync(projectId, remoteUserId, scene, cancellationToken);
                if (speechProductionPolicy == SpeechProductionPolicies.CanonicalVoice &&
                    !string.IsNullOrWhiteSpace(scene.SpokenText))
                {
                    await EnsureSceneNarrationAsync(
                        projectId,
                        remoteUserId,
                        scene,
                        cancellationToken);
                }
                completed++;
                if (reportProgress is not null)
                {
                    await reportProgress(
                        $"Đã tải xong cảnh {scene.SequenceNumber}/{scenes.Count}.",
                        cancellationToken);
                }
            }

            var allScenesApproved = await AreAllScenesApprovedAsync(projectId, cancellationToken);
            await UpdateProjectStatusAsync(
                projectId,
                remoteUserId,
                allScenesApproved ? "ReadyToRender" : "ScenePlanning",
                null,
                null,
                cancellationToken);
            return completed;
        }
        catch (MediaToolUnavailableException exception)
        {
            await MarkMediaToolBlockedAsync(
                projectId,
                remoteUserId,
                requestedSceneIds,
                exception,
                CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var code = exception is AccountClientException accountException
                ? accountException.Code
                : "video_generation_failed";
            await UpdateProjectStatusAsync(
                projectId,
                remoteUserId,
                "Failed",
                SafeCode(code),
                SafeMessage(exception.Message),
                cancellationToken);
            throw;
        }
    }

    private async Task PersistContentPlanAsync(
        Guid projectId,
        string remoteUserId,
        int version,
        GeneratedContentResponse response,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        if (project.CurrentScriptVersion is >= 1 && project.CurrentScriptVersion >= version)
        {
            return;
        }
        ValidateContentPlan(
            response.Plan,
            KlingLongFormSpeechIntentValidator.Applies(
                project.VideoProviderCode,
                KlingLongFormVietnameseValidator.OpenAiStructuredPlan));

        var now = DateTime.UtcNow;
        await dbContext.Concepts
            .Where(x => x.ProjectId == projectId && x.Status == "Approved")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);
        await dbContext.Scripts
            .Where(x => x.ProjectId == projectId && x.Status == "Approved")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);
        await dbContext.StyleProfiles
            .Where(x => x.ProjectId == projectId && x.Status == "Approved")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);
        await dbContext.Characters
            .Where(x => x.ProjectId == projectId && (x.Status == "Approved" || x.Status == "Draft"))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Superseded"), cancellationToken);

        var concept = new Concept
        {
            ConceptId = Guid.NewGuid(),
            ProjectId = projectId,
            Version = version,
            Title = response.Plan.Title,
            SelectedHook = response.Plan.Hook,
            Angle = response.Plan.Angle,
            Audience = response.Plan.Audience,
            CallToAction = response.Plan.CallToAction,
            HooksJson = JsonSerializer.Serialize(new[] { response.Plan.Hook }, JsonOptions),
            StrategyJson = JsonSerializer.Serialize(new
            {
                response.Plan.Angle,
                response.Plan.Audience,
                response.Plan.VisualStyle
            }, JsonOptions),
            Status = "Approved",
            ProviderCode = response.ProviderCode,
            ModelCode = response.ModelCode,
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        var script = new Script
        {
            ScriptId = Guid.NewGuid(),
            ProjectId = projectId,
            ConceptId = concept.ConceptId,
            Version = version,
            StructureType = KlingLongFormVietnameseValidator.OpenAiStructuredPlan,
            Title = response.Plan.Title,
            FullText = response.Plan.ScriptFullText,
            NarrationJson = JsonSerializer.Serialize(response.Plan.Scenes.Select(x => x.Narration), JsonOptions),
            StoryBeatsJson = JsonSerializer.Serialize(response.Plan.Scenes, JsonOptions),
            EstimatedDurationMs = response.Plan.Scenes.Sum(x => (long)x.DurationSeconds * 1000),
            Status = "Approved",
            ProviderCode = response.ProviderCode,
            ModelCode = response.ModelCode,
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        var style = new StyleProfile
        {
            StyleProfileId = Guid.NewGuid(),
            ProjectId = projectId,
            Version = version,
            Name = $"OpenAI Style v{version}",
            VisualStyleJson = JsonSerializer.Serialize(new { description = response.Plan.VisualStyle }, JsonOptions),
            NegativeRulesJson = JsonSerializer.Serialize(new { prompt = response.Plan.NegativePrompt }, JsonOptions),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now
        };
        dbContext.Concepts.Add(concept);
        dbContext.Scripts.Add(script);
        dbContext.StyleProfiles.Add(style);

        var characters = response.Plan.Characters
            .Select(generated => new Character
            {
                CharacterId = Guid.NewGuid(),
                ProjectId = projectId,
                CharacterKey = generated.CharacterKey,
                Version = version,
                Name = generated.Name,
                Role = generated.Role,
                IdentityAnchor = generated.CharacterKey,
                ProfileJson = JsonSerializer.Serialize(generated, JsonOptions),
                WardrobeJson = JsonSerializer.Serialize(new
                {
                    generated.Clothing,
                    generated.Accessories
                }, JsonOptions),
                ForbiddenChangesJson = JsonSerializer.Serialize(generated.ForbiddenChanges, JsonOptions),
                VisualIdentity = generated.VisualIdentity,
                VoiceCode = project.VoiceCode,
                VoiceSpeakingRate = project.VoiceSpeakingRate,
                Status = "Draft",
                CreatedAtUtc = now
            })
            .ToArray();
        var characterByKey = characters.ToDictionary(x => x.CharacterKey, StringComparer.OrdinalIgnoreCase);
        dbContext.Characters.AddRange(characters);

        long timelineMs = 0;
        var scenes = new List<Scene>();
        foreach (var generatedScene in response.Plan.Scenes.OrderBy(x => x.SequenceNumber))
        {
            var speechMode = ResolveSpeechMode(generatedScene);
            var canonicalNarration =
                project.SpeechProductionPolicy == SpeechProductionPolicies.CanonicalVoice &&
                speechMode != KlingSpeechModes.None;
            var sceneId = Guid.NewGuid();
            var contentDurationMs = generatedScene.DurationSeconds * 1000L;
            var generationDurationSeconds = generatedScene.GenerationDurationSeconds ?? generatedScene.DurationSeconds;
            var generationDurationMs = generationDurationSeconds * 1000L;
            if (generationDurationMs < contentDurationMs)
            {
                throw new InvalidDataException("Thời lượng provider không được ngắn hơn thời lượng nội dung cảnh.");
            }
            var scene = new Scene
            {
                SceneId = sceneId,
                ProjectId = projectId,
                ScriptId = script.ScriptId,
                StyleProfileId = style.StyleProfileId,
                ScenePlanVersion = version,
                SequenceNumber = generatedScene.SequenceNumber,
                ContinuityGroupKey = "main-story",
                StoryBeatId = $"scene-{generatedScene.SequenceNumber:000}",
                StoryPurpose = generatedScene.StoryPurpose,
                Narration = speechMode == KlingSpeechModes.NativeVoiceOver
                    ? NullIfWhiteSpace(generatedScene.Narration)
                    : null,
                Dialogue = speechMode == KlingSpeechModes.OnCameraDialogue
                    ? NullIfWhiteSpace(generatedScene.Narration)
                    : null,
                VisualDescription = generatedScene.VisualPrompt,
                CameraDirection = "cinematic composition and controlled camera movement",
                Lighting = response.Plan.VisualStyle,
                Motion = "natural coherent subject motion",
                ContentDurationMs = contentDurationMs,
                GenerationDurationMs = generationDurationMs,
                TimelineStartMs = timelineMs,
                TimelineEndMs = timelineMs + contentDurationMs,
                TailTrimMs = generationDurationMs - contentDurationMs,
                CharacterIdsJson = JsonSerializer.Serialize(
                    generatedScene.CharacterKeys.Select(key => characterByKey[key].CharacterId),
                    JsonOptions),
                EntryStateJson = JsonSerializer.Serialize(new { continuity = "continue from previous scene" }, JsonOptions),
                ExitStateJson = JsonSerializer.Serialize(new { continuity = "prepare for next scene" }, JsonOptions),
                RequiredCapabilitiesJson = JsonSerializer.Serialize(new
                {
                    textToVideo = true,
                    contentDurationSeconds = generatedScene.DurationSeconds,
                    generationDurationSeconds,
                    tailTrimSeconds = generationDurationSeconds - generatedScene.DurationSeconds,
                    project.AspectRatio,
                    nativeAudio = true,
                    speechMode,
                    muteOutputAudio = canonicalNarration,
                    generatedScene.SpeakerCharacterKey,
                    generatedScene.VoiceStyle,
                    generatedScene.AmbientAudio,
                    generatedScene.SoundEffects,
                    effectiveGenerationLanguageCode = response.EffectiveGenerationLanguageCode,
                    generationLanguagePolicyVersion = response.GenerationLanguagePolicyVersion
                }, JsonOptions),
                Status = "PromptReady",
                SpeechStatus = speechMode == KlingSpeechModes.None
                    ? "SpeechNotRequired"
                    : "SpeechMissing",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var prompt = new ScenePrompt
            {
                ScenePromptId = Guid.NewGuid(),
                SceneId = sceneId,
                Version = 1,
                PromptTemplateName = "openai-content-plan",
                PromptTemplateVersion = "3",
                CanonicalInputJson = JsonSerializer.Serialize(generatedScene, JsonOptions),
                FinalPrompt = generatedScene.VisualPrompt,
                NegativePrompt = response.Plan.NegativePrompt,
                PromptHash = Sha256Hex(
                    generatedScene.VisualPrompt + "\n" + response.Plan.NegativePrompt + "\n" +
                    response.EffectiveGenerationLanguageCode + "\n" + response.GenerationLanguagePolicyVersion),
                Status = "Approved",
                CreatedAtUtc = now,
                ApprovedAtUtc = now
            };
            scene.ScenePrompts.Add(prompt);
            scenes.Add(scene);
            timelineMs += contentDurationMs;
        }

        dbContext.Scenes.AddRange(scenes);
        project.CurrentConceptVersion = version;
        project.CurrentScriptVersion = version;
        project.CurrentCharacterVersion = version;
        project.CurrentStyleVersion = version;
        project.CurrentScenePlanVersion = version;
        project.Status = "ScenePlanning";
        project.LastErrorCode = null;
        project.LastErrorMessage = null;
        project.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < scenes.Count; index++)
        {
            scenes[index].PreviousSceneId = index == 0 ? null : scenes[index - 1].SceneId;
            scenes[index].NextSceneId = index == scenes.Count - 1 ? null : scenes[index + 1].SceneId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var relativePath = Path.Combine(project.WorkspaceRelativePath, "script", $"content-plan-v{version}.json");
        var outputPath = workspaceService.Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(response, JsonOptions),
            Encoding.UTF8,
            cancellationToken);
    }

    private async Task GenerateSceneAsync(
        Guid projectId,
        string remoteUserId,
        SceneWorkItem scene,
        CancellationToken cancellationToken)
    {
        var prompt = scene.Prompt!;
        VideoTaskResponse task;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            var existingRequest = await dbContext.ProviderRequests
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.SceneId == scene.SceneId && x.RequestKind == "Video")
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var requiresNewAttempt = scene.Status == "NativeAudioInvalid";
            if (!requiresNewAttempt &&
                existingRequest is not null &&
                existingRequest.Status is not ("Failed" or "Cancelled" or "Expired"))
            {
                task = await apiClient.GetVideoStatusAsync(existingRequest.ProviderRequestId, cancellationToken);
            }
            else
            {
                VideoReferenceImageInput? referenceImage = null;
                SceneFirstFrameInput? firstFrame = null;
                if (string.Equals(project.VideoProviderCode, "fal", StringComparison.OrdinalIgnoreCase))
                {
                    var approved = (await apiClient.GetSceneFirstFramesAsync(projectId, scene.SceneId, cancellationToken)).Frames
                        .SingleOrDefault(x => x.Status == SceneFirstFrameStatuses.Approved && x.IsCurrent)
                        ?? throw new InvalidDataException(
                            "Cảnh này chưa có first-frame đúng tỷ lệ đã duyệt. Hãy tạo và duyệt first-frame trước khi gửi sang Veo.");
                    firstFrame = await LoadSceneFirstFrameInputAsync(approved, cancellationToken);
                }
                else if (scene.Character?.Reference is not null)
                {
                    referenceImage = await LoadReferenceImageAsync(
                        project.WorkspaceRelativePath,
                        scene.Character.Reference,
                        cancellationToken);
                }
                var attempt = await dbContext.ProviderRequests.CountAsync(
                    x => x.ProjectId == projectId && x.SceneId == scene.SceneId && x.RequestKind == "Video",
                    cancellationToken) + 1;
                task = await apiClient.SubmitVideoAsync(
                    new SubmitVideoRequest(
                        projectId,
                        scene.SceneId,
                        $"video:{prompt.ScenePromptId:N}:input:{firstFrame?.SceneFirstFrameId.ToString("N") ?? referenceImage?.CharacterReferenceId.ToString("N") ?? "none"}:attempt:{attempt}",
                        ReferenceImage: referenceImage,
                        ScenePlanVersion: scene.ScenePlanVersion,
                        ScenePromptVersion: prompt.Version,
                        FirstFrame: firstFrame),
                    cancellationToken);
            }
        }

        var generationId = await EnsureVideoGenerationAsync(scene, task, cancellationToken);
        var deadline = DateTime.UtcNow.AddMinutes(45);
        while (task.Status is not ("Completed" or "Failed" or "Cancelled" or "Expired"))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Provider chưa hoàn tất cảnh sau 45 phút; bạn có thể tiếp tục kiểm tra task sau.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            task = await apiClient.GetVideoStatusAsync(task.ProviderRequestId, cancellationToken);
            await UpdateGenerationWaitingStatusAsync(
                generationId,
                scene.SceneId,
                task.Status,
                task.ErrorCode,
                task.ErrorMessage,
                cancellationToken);
        }

        if (task.Status != "Completed" || string.IsNullOrWhiteSpace(task.OutputUrl))
        {
            throw new AccountClientException(
                task.ErrorCode ?? "video_generation_failed",
                task.ErrorMessage ?? "Provider không thể hoàn tất video.",
                502);
        }

        await DownloadAndApproveAsync(projectId, remoteUserId, scene, generationId, task, cancellationToken);
    }

    private async Task<VideoReferenceImageInput> LoadReferenceImageAsync(
        string projectWorkspace,
        ReferenceWorkItem reference,
        CancellationToken cancellationToken)
    {
        if (reference.SizeBytes is <= 0 or > 10 * 1024 * 1024 ||
            reference.MimeType is not ("image/jpeg" or "image/png"))
        {
            throw new InvalidDataException("Ảnh tham chiếu nhân vật không đáp ứng giới hạn của provider video.");
        }

        var relativePath = Path.Combine(
            projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
            reference.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var resolvedPath = workspaceService.Resolve(relativePath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Không tìm thấy ảnh tham chiếu nhân vật trong workspace.");
        }

        byte[] bytes;
        await using (var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length != reference.SizeBytes || stream.Length > 10 * 1024 * 1024)
            {
                throw new InvalidDataException("Ảnh tham chiếu nhân vật đã thay đổi sau khi được duyệt.");
            }

            bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(reference.Sha256.ToLowerInvariant())))
        {
            throw new InvalidDataException("Ảnh tham chiếu nhân vật không khớp bản đã được duyệt.");
        }

        return new VideoReferenceImageInput(
            reference.CharacterReferenceId,
            reference.MimeType,
            Convert.ToBase64String(bytes),
            hash);
    }

    private async Task<SceneFirstFrameInput> LoadSceneFirstFrameInputAsync(
        SceneFirstFrameSummary frame,
        CancellationToken cancellationToken)
    {
        if (frame.Status != SceneFirstFrameStatuses.Approved || !frame.IsCurrent ||
            frame.SizeBytes is <= 0 or > 8L * 1024 * 1024 || frame.MimeType is not ("image/png" or "image/jpeg"))
        {
            throw new InvalidDataException("First-frame chưa được duyệt hoặc đã lỗi thời.");
        }
        var path = workspaceService.Resolve(frame.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Không tìm thấy file first-frame đã duyệt trong workspace.");
        }
        var bytes = new byte[new FileInfo(path).Length];
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length != frame.SizeBytes || stream.Length > 8L * 1024 * 1024)
            {
                throw new InvalidDataException("File first-frame đã thay đổi sau khi được duyệt.");
            }
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }
        var info = ReadImageInfo(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (info.MimeType != frame.MimeType || info.Width != frame.Width || info.Height != frame.Height ||
            !string.Equals(hash, frame.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("File first-frame không khớp bản đã duyệt.");
        }
        return new SceneFirstFrameInput(frame.SceneFirstFrameId, frame.MimeType, Convert.ToBase64String(bytes), hash);
    }

    private async Task<Guid> EnsureVideoGenerationAsync(
        SceneWorkItem scene,
        VideoTaskResponse task,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.VideoGenerations.SingleOrDefaultAsync(
            x => x.ProviderRequestId == task.ProviderRequestId,
            cancellationToken);
        var localScene = await dbContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
        var localPrompt = await dbContext.ScenePrompts.SingleAsync(x => x.ScenePromptId == scene.Prompt!.ScenePromptId, cancellationToken);
        if (existing is null)
        {
            var attempt = (await dbContext.VideoGenerations
                .Where(x => x.SceneId == scene.SceneId)
                .MaxAsync(x => (int?)x.AttemptNumber, cancellationToken) ?? 0) + 1;
            existing = new VideoGeneration
            {
                VideoGenerationId = Guid.NewGuid(),
                SceneId = scene.SceneId,
                ScenePromptId = localPrompt.ScenePromptId,
                ProviderRequestId = task.ProviderRequestId,
                AttemptNumber = attempt,
                Status = LocalGenerationStatus(task.Status),
                RequestedDurationMs = scene.GenerationDurationMs,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.VideoGenerations.Add(existing);
        }

        localScene.Status = LocalSceneStatus(task.Status);
        localScene.LastErrorCode = string.IsNullOrWhiteSpace(task.ErrorCode) ? null : SafeCode(task.ErrorCode);
        localScene.LastErrorMessage = string.IsNullOrWhiteSpace(task.ErrorMessage) ? null : SafeMessage(task.ErrorMessage);
        localScene.UpdatedAtUtc = DateTime.UtcNow;
        localPrompt.ProviderCode = task.ProviderCode;
        localPrompt.ModelCode = task.ModelCode;
        await dbContext.SaveChangesAsync(cancellationToken);
        return existing.VideoGenerationId;
    }

    private async Task UpdateGenerationWaitingStatusAsync(
        Guid generationId,
        Guid sceneId,
        string taskStatus,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var generation = await dbContext.VideoGenerations.SingleAsync(
            x => x.VideoGenerationId == generationId,
            cancellationToken);
        var scene = await dbContext.Scenes.SingleAsync(x => x.SceneId == sceneId, cancellationToken);
        if (taskStatus == "Completed")
        {
            generation.Status = "Generated";
            scene.Status = "Generated";
        }
        else if (taskStatus is "Failed" or "Cancelled" or "Expired")
        {
            generation.Status = taskStatus == "Expired" ? "Failed" : taskStatus;
            scene.Status = taskStatus == "Expired" ? "Failed" : taskStatus;
        }
        else
        {
            generation.Status = "WaitingProvider";
            scene.Status = "WaitingProvider";
        }

        scene.LastErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : SafeCode(errorCode);
        scene.LastErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : SafeMessage(errorMessage);
        scene.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DownloadAndApproveAsync(
        Guid projectId,
        string remoteUserId,
        SceneWorkItem scene,
        Guid generationId,
        VideoTaskResponse task,
        CancellationToken cancellationToken)
    {
        string projectWorkspace;
        string aspectRatio;
        string speechProductionPolicy;
        bool isLongFormWorkflow;
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
            projectWorkspace = project.WorkspaceRelativePath;
            aspectRatio = project.AspectRatio;
            speechProductionPolicy = project.SpeechProductionPolicy;
            isLongFormWorkflow = await dbContext.Scripts
                .AsNoTracking()
                .AnyAsync(
                    x => x.ScriptId == scene.ScriptId &&
                         x.ProjectId == projectId &&
                         x.StructureType == KlingLongFormVietnameseValidator.OpenAiStructuredPlan,
                    cancellationToken);
            var generation = await dbContext.VideoGenerations.SingleAsync(x => x.VideoGenerationId == generationId, cancellationToken);
            generation.Status = "Downloading";
            var localScene = await dbContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
            localScene.Status = "Generated";
            localScene.LastErrorCode = null;
            localScene.LastErrorMessage = null;
            localScene.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var fileName = $"scene-{scene.SequenceNumber:000}-{task.ProviderRequestId:N}.mp4";
        var assetRelativePath = Path.Combine("scenes", fileName).Replace(Path.DirectorySeparatorChar, '/');
        var workspaceRelativePath = Path.Combine(projectWorkspace, "scenes", fileName);
        var finalPath = workspaceService.Resolve(workspaceRelativePath);
        var partialPath = finalPath + ".part";
        var trimmedPartialPath = finalPath + ".trimmed.part";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        MediaProbeResult probe;
        AudioQualityResult? nativeAudioQuality;
        var trimmedToContentDuration = scene.ContentDurationMs > 0 &&
                                       scene.ContentDurationMs < scene.GenerationDurationMs;
        var canonicalAudio = speechProductionPolicy == SpeechProductionPolicies.CanonicalVoice &&
            !string.IsNullOrWhiteSpace(scene.SpokenText);
        var outputAudioEnabled = !canonicalAudio && !ReadBooleanProperty(
            scene.RequiredCapabilitiesJson,
            "muteOutputAudio");
        try
        {
            await apiClient.DownloadVideoAsync(task.OutputUrl!, partialPath, cancellationToken);
            probe = await mediaProbe.ProbeAsync(partialPath, cancellationToken);
            if (!probe.HasVideo)
            {
                throw new InvalidDataException("Tệp provider tải về không chứa luồng video.");
            }

            if (trimmedToContentDuration)
            {
                if (scene.ContentDurationMs % 1000 != 0)
                {
                    throw new InvalidDataException("Thời lượng video ngắn cần cắt phải là số giây nguyên.");
                }
                var targetDurationSeconds = checked((int)(scene.ContentDurationMs / 1000));
                await sceneVideoTrimmer.TrimAsync(
                    partialPath,
                    trimmedPartialPath,
                    targetDurationSeconds,
                    cancellationToken,
                    includeAudio: outputAudioEnabled);
                probe = await mediaProbe.ProbeAsync(trimmedPartialPath, cancellationToken);
                if (!probe.HasVideo ||
                    probe.DurationSeconds < targetDurationSeconds - 0.25m ||
                    probe.DurationSeconds > targetDurationSeconds + 0.5m)
                {
                    throw new InvalidDataException("Clip sau khi cắt không đạt đúng thời lượng người dùng đã chọn.");
                }
                if (!outputAudioEnabled && probe.HasAudio)
                {
                    throw new InvalidDataException("Clip đã tắt âm thanh nhưng file sau xử lý vẫn còn audio stream.");
                }
                nativeAudioQuality = outputAudioEnabled
                    ? await audioQualityValidator.AnalyzeAsync(trimmedPartialPath, cancellationToken)
                    : null;
                File.Move(trimmedPartialPath, finalPath, true);
                File.Delete(partialPath);
            }
            else if (!outputAudioEnabled)
            {
                await sceneVideoTrimmer.StripAudioAsync(
                    partialPath,
                    trimmedPartialPath,
                    cancellationToken);
                probe = await mediaProbe.ProbeAsync(trimmedPartialPath, cancellationToken);
                if (!probe.HasVideo || probe.HasAudio)
                {
                    throw new InvalidDataException("Clip tắt âm thanh sau xử lý không hợp lệ.");
                }
                nativeAudioQuality = null;
                File.Move(trimmedPartialPath, finalPath, true);
                File.Delete(partialPath);
            }
            else
            {
                nativeAudioQuality = await audioQualityValidator.AnalyzeAsync(partialPath, cancellationToken);
                File.Move(partialPath, finalPath, true);
            }
        }
        catch
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
            if (File.Exists(trimmedPartialPath))
            {
                File.Delete(trimmedPartialPath);
            }
            throw;
        }

        string hash;
        await using (var stream = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }

        var fileInfo = new FileInfo(finalPath);
        var speechExpected = !string.IsNullOrWhiteSpace(scene.SpokenText);
        var (expectedWidth, expectedHeight) = Dimensions(aspectRatio, "720p");
        var durationMs = probe.DurationSeconds > 0
            ? checked((long)Math.Round(probe.DurationSeconds * 1000m, MidpointRounding.AwayFromZero))
            : scene.GenerationDurationMs;
        await using var writeContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await RequireProjectAsync(writeContext, projectId, remoteUserId, cancellationToken);
        var generationToApprove = await writeContext.VideoGenerations.SingleAsync(
            x => x.VideoGenerationId == generationId,
            cancellationToken);
        var sceneToApprove = await writeContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
        var asset = await writeContext.MediaAssets.SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.RelativePath == assetRelativePath,
            cancellationToken);
        if (asset is null)
        {
            asset = new MediaAsset
            {
                MediaAssetId = Guid.NewGuid(),
                ProjectId = projectId,
                SceneId = scene.SceneId,
                AssetType = "SceneVideo",
                DisplayName = $"Cảnh {scene.SequenceNumber}",
                RelativePath = assetRelativePath,
                MimeType = "video/mp4",
                SizeBytes = fileInfo.Length,
                Sha256 = hash,
                Width = probe.Width ?? expectedWidth,
                Height = probe.Height ?? expectedHeight,
                FrameRate = probe.FramesPerSecond,
                DurationMs = durationMs,
                AudioSampleRate = probe.AudioSampleRate,
                Status = "Ready",
                SourceType = "Generated",
                SourceProviderCode = task.ProviderCode,
                SourceExternalRequestId = task.ExternalRequestId,
                SourceProviderRequestId = task.ProviderRequestId,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    task.ProviderRequestId,
                    task.ModelCode,
                    audioStrategy = outputAudioEnabled ? "ProviderNative" : "SilentOutput",
                    requestedContentDurationMs = scene.ContentDurationMs,
                    providerGenerationDurationMs = scene.GenerationDurationMs,
                    trimmedToContentDuration,
                    providerNativeAudioRequested = true,
                    outputAudioEnabled,
                    speechExpected,
                    speechMode = !string.IsNullOrWhiteSpace(scene.Dialogue)
                        ? KlingSpeechModes.OnCameraDialogue
                        : !string.IsNullOrWhiteSpace(scene.Narration)
                            ? KlingSpeechModes.NativeVoiceOver
                            : KlingSpeechModes.None,
                    spokenTextHash = Sha256Hex(scene.SpokenText ?? string.Empty),
                    nativeAudioExpected = outputAudioEnabled,
                    nativeAudioPresent = probe.HasAudio,
                    nativeAudioAudible = nativeAudioQuality?.IsAudible ?? false,
                    meanVolumeDb = nativeAudioQuality?.MeanVolumeDb,
                    maxVolumeDb = nativeAudioQuality?.MaxVolumeDb,
                    silentRatio = nativeAudioQuality?.SilentRatio,
                    probe.VideoCodec,
                    probe.AudioCodec,
                    warningCode = nativeAudioQuality?.FailureCode
                }, JsonOptions),
                CreatedAtUtc = DateTime.UtcNow,
                VerifiedAtUtc = DateTime.UtcNow
            };
            writeContext.MediaAssets.Add(asset);
        }

        generationToApprove.OutputMediaAssetId = asset.MediaAssetId;
        generationToApprove.ActualDurationMs = durationMs;
        generationToApprove.QualityReportJson = JsonSerializer.Serialize(new
        {
            audioStrategy = outputAudioEnabled ? "ProviderNative" : "SilentOutput",
            requestedContentDurationMs = scene.ContentDurationMs,
            providerGenerationDurationMs = scene.GenerationDurationMs,
            trimmedToContentDuration,
            providerNativeAudioRequested = true,
            outputAudioEnabled,
            speechExpected,
            nativeAudioExpected = outputAudioEnabled,
            nativeAudioPresent = probe.HasAudio,
            nativeAudioAudible = nativeAudioQuality?.IsAudible ?? false,
            meanVolumeDb = nativeAudioQuality?.MeanVolumeDb,
            maxVolumeDb = nativeAudioQuality?.MaxVolumeDb,
            silentRatio = nativeAudioQuality?.SilentRatio,
            issues = !outputAudioEnabled || nativeAudioQuality?.IsAudible == true
                ? Array.Empty<string>()
                : new[] { nativeAudioQuality?.FailureCode ?? "native_audio_invalid" }
        }, JsonOptions);
        var nativeAudioInvalid = outputAudioEnabled &&
                                 (!probe.HasAudio || nativeAudioQuality?.IsAudible != true);
        generationToApprove.Status = outputAudioEnabled
            ? nativeAudioInvalid ? "NativeAudioInvalid" : "AudioReviewRequired"
            : "Approved";
        generationToApprove.CompletedAtUtc = DateTime.UtcNow;
        sceneToApprove.ApprovedGenerationId = outputAudioEnabled
            ? null
            : generationToApprove.VideoGenerationId;
        sceneToApprove.ApprovedRenderMediaAssetId = outputAudioEnabled || canonicalAudio
            ? null
            : asset.MediaAssetId;
        sceneToApprove.Status = outputAudioEnabled
            ? nativeAudioInvalid ? "NativeAudioInvalid" : "AudioReviewRequired"
            : canonicalAudio ? "Generated" : "Approved";
        sceneToApprove.LastErrorCode = nativeAudioInvalid
            ? nativeAudioQuality?.FailureCode ?? "provider_native_audio_inaudible"
            : null;
        sceneToApprove.LastErrorMessage = nativeAudioInvalid
            ? nativeAudioQuality?.FailureMessage ?? "Clip provider không có Native Audio nghe được. Hãy tạo lại cảnh."
            : null;
        sceneToApprove.UpdatedAtUtc = DateTime.UtcNow;
        await writeContext.SaveChangesAsync(cancellationToken);
        if (speechExpected &&
            outputAudioEnabled &&
            nativeAudioQuality?.IsAudible == true &&
            string.Equals(
                speechProductionPolicy,
                SpeechProductionPolicies.ProviderNativeVerified,
                StringComparison.Ordinal))
        {
            sceneToApprove.SpeechStatus = isLongFormWorkflow
                ? SceneSpeechStatuses.SpeechReviewRequired
                : SceneSpeechStatuses.SpeechVerificationRequired;
            sceneToApprove.LastErrorCode = null;
            sceneToApprove.LastErrorMessage = null;
            await writeContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureSceneNarrationAsync(
        Guid projectId,
        string remoteUserId,
        SceneWorkItem scene,
        CancellationToken cancellationToken)
    {
        var narration = NormalizeNarration(scene.SpokenText);
        if (string.IsNullOrWhiteSpace(narration))
        {
            return;
        }
        var narrationHash = Sha256Hex(narration);
        string projectWorkspace;
        string voiceCode;
        decimal speakingRate;
        Guid? approvedVoiceProfileVersionId;
        string? approvedVoiceSnapshotHash;
        Guid? existingVoiceAssetId;
        Guid? existingVoiceRequestId;
        Guid? existingVoiceGenerationId;
        string? existingVoiceRelativePath;
        string? existingVoiceSha256;
        string? existingVoiceStatus;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await RequireProjectAsync(readContext, projectId, remoteUserId, cancellationToken);
            projectWorkspace = project.WorkspaceRelativePath;
            var useCharacterVoice = scene.SpeechMode == KlingSpeechModes.OnCameraDialogue;
            approvedVoiceProfileVersionId = useCharacterVoice
                ? scene.Character?.ApprovedVoiceProfileVersionId
                : project.ApprovedNarratorVoiceProfileVersionId;
            if (approvedVoiceProfileVersionId is null)
            {
                throw new AccountClientException(
                    SpeechSynchronizationErrorCodes.VoiceProfileMissing,
                    useCharacterVoice
                        ? "Hãy tạo, nghe thử và duyệt giọng nhân vật trước khi tạo video."
                        : "Hãy tạo, nghe thử và duyệt giọng narrator trước khi tạo video.",
                    409);
            }
            var approvedVoice = await readContext.VoiceProfileVersions.AsNoTracking()
                .Where(x => x.VoiceProfileVersionId == approvedVoiceProfileVersionId &&
                            x.Status == VoiceProfileVersionStatuses.Approved &&
                            x.PreviewProviderRequestId != null)
                .Select(x => new { x.VoiceCode, x.SpeakingRate, x.SnapshotHash })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new AccountClientException(
                    SpeechSynchronizationErrorCodes.VoiceVersionNotApproved,
                    "Phiên bản giọng chưa được preview và duyệt hợp lệ.",
                    409);
            voiceCode = approvedVoice.VoiceCode;
            speakingRate = approvedVoice.SpeakingRate;
            approvedVoiceSnapshotHash = approvedVoice.SnapshotHash;
            var existingVoice = await readContext.VoiceGenerations
                .AsNoTracking()
                .Where(x => x.SceneId == scene.SceneId &&
                            x.ScenePlanVersion == scene.ScenePlanVersion &&
                            x.NarrationHash == narrationHash &&
                            x.VoiceCode == voiceCode &&
                            x.SpeakingRate == speakingRate &&
                            x.VoiceProfileVersionId == approvedVoiceProfileVersionId &&
                            x.VoiceSnapshotHash == approvedVoiceSnapshotHash &&
                            (x.Status == "Completed" || x.Status == "Approved") &&
                            x.OutputMediaAssetId != null &&
                            x.OutputMediaAsset!.Status == "Ready" &&
                            x.OutputMediaAsset.DeletedAtUtc == null)
                .OrderByDescending(x => x.Version)
                .Select(x => new
                {
                    x.VoiceGenerationId,
                    x.ProviderRequestId,
                    x.Status,
                    AssetId = x.OutputMediaAssetId,
                    x.OutputMediaAsset!.RelativePath,
                    x.OutputMediaAsset.Sha256
                })
                .FirstOrDefaultAsync(cancellationToken);
            existingVoiceAssetId = existingVoice?.AssetId;
            existingVoiceRequestId = existingVoice?.ProviderRequestId;
            existingVoiceGenerationId = existingVoice?.VoiceGenerationId;
            existingVoiceRelativePath = existingVoice?.RelativePath;
            existingVoiceSha256 = existingVoice?.Sha256;
            existingVoiceStatus = existingVoice?.Status;
        }

        string voicePath;
        Guid voiceAssetId;
        Guid voiceProviderRequestId;
        Guid voiceGenerationId;
        string voiceAssetSha256;
        var voiceApproved = string.Equals(existingVoiceStatus, "Approved", StringComparison.Ordinal);
        var reusedExistingVoice = false;
        if (existingVoiceAssetId is { } readyVoiceAssetId &&
            existingVoiceRequestId is { } readyVoiceRequestId &&
            existingVoiceGenerationId is { } readyVoiceGenerationId &&
            !string.IsNullOrWhiteSpace(existingVoiceRelativePath))
        {
            var relative = Path.Combine(
                projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
                existingVoiceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            voicePath = workspaceService.Resolve(relative);
            if (!File.Exists(voicePath))
            {
                throw new FileNotFoundException("Metadata giọng đọc đã có nhưng file trong workspace bị thiếu.", voicePath);
            }
            voiceAssetId = readyVoiceAssetId;
            voiceProviderRequestId = readyVoiceRequestId;
            voiceGenerationId = readyVoiceGenerationId;
            voiceAssetSha256 = existingVoiceSha256
                ?? throw new InvalidDataException("MediaAsset giọng đọc chưa có SHA-256 hợp lệ.");
            reusedExistingVoice = true;
        }
        else
        {
            var voiceSnapshotHash = approvedVoiceSnapshotHash;
            var response = await apiClient.GenerateSceneVoiceAsync(
                new GenerateSceneVoiceRequest(
                    projectId,
                    scene.SceneId,
                    scene.ScenePlanVersion,
                    narrationHash,
                    $"scene-voice:{scene.SceneId:N}:v{scene.ScenePlanVersion}:{narrationHash}:{voiceSnapshotHash}",
                    ExpectedVoiceSnapshotHash: approvedVoiceSnapshotHash,
                    ExpectedVoiceProfileVersionId: approvedVoiceProfileVersionId),
                cancellationToken);
            if (response.Status != "Completed" ||
                !string.Equals(response.VoiceCode, voiceCode, StringComparison.Ordinal) ||
                response.DurationMs <= 0 ||
                !string.Equals(response.ExpectedSpeechHash, narrationHash, StringComparison.OrdinalIgnoreCase) ||
                (approvedVoiceProfileVersionId.HasValue && response.VoiceProfileVersionId != approvedVoiceProfileVersionId) ||
                (approvedVoiceSnapshotHash is not null && !string.Equals(
                    response.VoiceSnapshotHash,
                    approvedVoiceSnapshotHash,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Server trả về kết quả giọng đọc không hợp lệ.");
            }

            var voiceFileName = $"scene-{scene.SequenceNumber:000}-{response.ProviderRequestId:N}.wav";
            var voiceAssetRelativePath = Path.Combine("voice", voiceFileName).Replace(Path.DirectorySeparatorChar, '/');
            var voiceWorkspacePath = Path.Combine(projectWorkspace, "voice", voiceFileName);
            var finalVoicePath = workspaceService.Resolve(voiceWorkspacePath);
            var partialVoicePath = finalVoicePath + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(finalVoicePath)!);
            if (File.Exists(partialVoicePath))
            {
                File.Delete(partialVoicePath);
            }
            AudioQualityResult downloadedVoiceQuality;
            try
            {
                await apiClient.DownloadSceneVoiceAsync(response, partialVoicePath, cancellationToken);
                var voiceProbe = await mediaProbe.ProbeAsync(partialVoicePath, cancellationToken);
                downloadedVoiceQuality = await audioQualityValidator.RequireAudibleAsync(
                    partialVoicePath,
                    "Giọng đọc tải về không nghe được",
                    cancellationToken);
                if (!voiceProbe.HasAudio || voiceProbe.DurationSeconds <= 0 ||
                    voiceProbe.AudioSampleRate != response.SampleRate)
                {
                    throw new InvalidDataException("Giọng đọc tải về không khớp thông số âm thanh đã xác nhận.");
                }
                File.Move(partialVoicePath, finalVoicePath, true);
            }
            catch
            {
                if (File.Exists(partialVoicePath))
                {
                    File.Delete(partialVoicePath);
                }
                throw;
            }

            await using var writeContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(cancellationToken);
            await RequireProjectAsync(writeContext, projectId, remoteUserId, cancellationToken);
            var voiceGeneration = await writeContext.VoiceGenerations.SingleOrDefaultAsync(
                x => x.ProviderRequestId == response.ProviderRequestId &&
                     x.SceneId == scene.SceneId &&
                     x.ProjectId == projectId,
                cancellationToken)
                ?? throw new InvalidDataException("Không tìm thấy bản ghi VoiceGeneration do server vừa tạo.");
            var voiceAsset = await writeContext.MediaAssets.SingleOrDefaultAsync(
                x => x.SourceProviderRequestId == response.ProviderRequestId,
                cancellationToken);
            if (voiceAsset is null)
            {
                voiceAsset = new MediaAsset
                {
                    MediaAssetId = Guid.NewGuid(),
                    ProjectId = projectId,
                    SceneId = scene.SceneId,
                    AssetType = "SceneVoice",
                    DisplayName = $"Giọng đọc cảnh {scene.SequenceNumber}",
                    RelativePath = voiceAssetRelativePath,
                    MimeType = response.MimeType,
                    SizeBytes = response.SizeBytes,
                    Sha256 = response.Sha256,
                    DurationMs = response.DurationMs,
                    AudioSampleRate = response.SampleRate,
                    Status = "Ready",
                    SourceType = "Generated",
                    SourceProviderCode = response.ProviderCode,
                    SourceExternalRequestId = response.ProviderRequestId.ToString("D"),
                    SourceProviderRequestId = response.ProviderRequestId,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        response.ModelCode,
                        response.VoiceCode,
                        response.ProviderVoiceCode,
                        response.Channels,
                        response.VoiceGenerationId,
                        response.VoiceProfileVersionId,
                        response.VoiceSnapshotHash,
                        response.VerificationStatus,
                        narrationHash,
                        scene.ScenePlanVersion,
                        downloadedVoiceQuality.MeanVolumeDb,
                        downloadedVoiceQuality.MaxVolumeDb,
                        downloadedVoiceQuality.SilentRatio,
                        voiceDurationRatio = scene.ContentDurationMs > 0
                            ? response.DurationMs / (decimal)scene.ContentDurationMs
                            : (decimal?)null
                    }, JsonOptions),
                    CreatedAtUtc = DateTime.UtcNow,
                    VerifiedAtUtc = DateTime.UtcNow,
                    RowVersion = new byte[8]
                };
                writeContext.MediaAssets.Add(voiceAsset);
            }
            voiceGeneration.OutputMediaAssetId = voiceAsset.MediaAssetId;
            voiceApproved = string.Equals(voiceGeneration.Status, "Approved", StringComparison.Ordinal);
            if (!voiceApproved)
            {
                voiceGeneration.Status = "Completed";
            }
            voiceGeneration.DurationMs = response.DurationMs;
            voiceGeneration.VoiceSnapshotHash = response.VoiceSnapshotHash;
            voiceGeneration.VerificationStatus = response.VerificationStatus;
            voiceGeneration.CompletedAtUtc ??= DateTime.UtcNow;
            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            voicePath = finalVoicePath;
            voiceAssetId = voiceAsset.MediaAssetId;
            voiceProviderRequestId = response.ProviderRequestId;
            voiceGenerationId = voiceGeneration.VoiceGenerationId;
            voiceAssetSha256 = response.Sha256;
        }

        string currentVoiceSha256;
        await using (var voiceStream = new FileStream(
                         voicePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            currentVoiceSha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(voiceStream, cancellationToken)).ToLowerInvariant();
        }
        if (!string.Equals(currentVoiceSha256, voiceAssetSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Canonical WAV trong workspace đã thay đổi sau khi được ghi nhận.");
        }

        var voiceDurationProbe = await mediaProbe.ProbeAsync(voicePath, cancellationToken);
        try
        {
            sceneAudioMixer.ValidateVoiceDuration(
                voiceDurationProbe.DurationSeconds,
                scene.ContentDurationMs / 1000m);
        }
        catch (SpeechDurationOutOfRangeException exception)
        {
            await using var invalidContext = await dbContextFactory.CreateDbContextAsync(CancellationToken.None);
            await RequireProjectAsync(invalidContext, projectId, remoteUserId, CancellationToken.None);
            var invalidScene = await invalidContext.Scenes.SingleAsync(
                x => x.SceneId == scene.SceneId,
                CancellationToken.None);
            invalidScene.SpeechStatus = SceneSpeechStatuses.SpeechInvalid;
            invalidScene.LastErrorCode = SpeechSynchronizationErrorCodes.SpeechDurationOutOfRange;
            invalidScene.LastErrorMessage = exception.Message;
            invalidScene.UpdatedAtUtc = DateTime.UtcNow;
            await invalidContext.SaveChangesAsync(CancellationToken.None);
            throw new AccountClientException(
                SpeechSynchronizationErrorCodes.SpeechDurationOutOfRange,
                exception.Message,
                422);
        }
        if (scene.SpeechMode == KlingSpeechModes.OnCameraDialogue && !voiceApproved)
        {
            await using var readyContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await RequireProjectAsync(readyContext, projectId, remoteUserId, cancellationToken);
            var readyScene = await readyContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
            readyScene.SpeechStatus = SceneSpeechStatuses.SpeechReviewRequired;
            readyScene.Status = "AudioReviewRequired";
            readyScene.LastErrorCode = null;
            readyScene.LastErrorMessage = null;
            readyScene.UpdatedAtUtc = DateTime.UtcNow;
            await readyContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!voiceApproved)
        {
            if (reusedExistingVoice)
            {
                await using var approvalContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await approvalContext.Database.BeginTransactionAsync(cancellationToken);
                await RequireProjectAsync(approvalContext, projectId, remoteUserId, cancellationToken);
                var currentVoice = await approvalContext.VoiceGenerations.SingleAsync(
                    x => x.VoiceGenerationId == voiceGenerationId &&
                         x.ProjectId == projectId &&
                         x.SceneId == scene.SceneId &&
                         x.ScenePlanVersion == scene.ScenePlanVersion &&
                         x.NarrationHash == narrationHash &&
                         x.VoiceProfileVersionId == approvedVoiceProfileVersionId &&
                         x.VoiceSnapshotHash == approvedVoiceSnapshotHash &&
                         x.OutputMediaAssetId == voiceAssetId &&
                         x.Status == "Completed",
                    cancellationToken);
                var currentScene = await approvalContext.Scenes.SingleAsync(
                    x => x.SceneId == scene.SceneId &&
                         x.ProjectId == projectId &&
                         x.ScenePlanVersion == scene.ScenePlanVersion,
                    cancellationToken);
                var now = DateTime.UtcNow;
                currentVoice.Status = "Approved";
                currentVoice.ApprovedAtUtc = now;
                currentScene.ApprovedVoiceGenerationId = currentVoice.VoiceGenerationId;
                currentScene.ApprovedGenerationId = null;
                currentScene.ApprovedRenderMediaAssetId = null;
                currentScene.SpeechStatus = SceneSpeechStatuses.SpeechApproved;
                currentScene.Status = "PromptReady";
                currentScene.LastErrorCode = null;
                currentScene.LastErrorMessage = null;
                currentScene.UpdatedAtUtc = now;
                await approvalContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            await using var reviewContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await RequireProjectAsync(reviewContext, projectId, remoteUserId, cancellationToken);
            var reviewScene = await reviewContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
            reviewScene.SpeechStatus = SceneSpeechStatuses.SpeechReviewRequired;
            reviewScene.Status = "AudioReviewRequired";
            reviewScene.LastErrorCode = null;
            reviewScene.LastErrorMessage = null;
            reviewScene.UpdatedAtUtc = DateTime.UtcNow;
            await reviewContext.SaveChangesAsync(cancellationToken);
            return;
        }

        string rawVideoPath;
        Guid rawVideoAssetId;
        await using (var readContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var rawAsset = await readContext.Scenes
                .AsNoTracking()
                .Where(x => x.SceneId == scene.SceneId && x.ProjectId == projectId && x.ApprovedGenerationId != null)
                .Select(x => new
                {
                    AssetId = x.ApprovedGeneration!.OutputMediaAssetId,
                    x.ApprovedGeneration!.OutputMediaAsset!.RelativePath
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (rawAsset is null)
            {
                return;
            }
            if (rawAsset.AssetId is null || string.IsNullOrWhiteSpace(rawAsset.RelativePath))
            {
                throw new InvalidDataException("Clip video đã duyệt chưa có MediaAsset hợp lệ.");
            }
            rawVideoAssetId = rawAsset.AssetId.Value;
            rawVideoPath = workspaceService.Resolve(Path.Combine(
                projectWorkspace.Replace('/', Path.DirectorySeparatorChar),
                rawAsset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        if (!File.Exists(rawVideoPath))
        {
            throw new FileNotFoundException("Không tìm thấy clip video trong workspace.", rawVideoPath);
        }

        var narratedFileName = $"scene-{scene.SequenceNumber:000}-narrated-{voiceProviderRequestId:N}.mp4";
        var narratedAssetRelativePath = Path.Combine("scenes", narratedFileName).Replace(Path.DirectorySeparatorChar, '/');
        var narratedPath = workspaceService.Resolve(Path.Combine(projectWorkspace, "scenes", narratedFileName));
        await using (var existingContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var existingNarratedAsset = await existingContext.MediaAssets.AsNoTracking()
                .Where(
                x => x.ProjectId == projectId &&
                     x.SceneId == scene.SceneId &&
                     x.AssetType == "SceneVideoNarrated" &&
                     x.RelativePath == narratedAssetRelativePath &&
                     x.Status == "Ready" &&
                     x.DeletedAtUtc == null)
                .Select(x => new { x.MediaAssetId, x.MetadataJson, x.Sha256 })
                .SingleOrDefaultAsync(cancellationToken);
            var existingMatchesVoice = existingNarratedAsset is not null &&
                ReadStringProperty(existingNarratedAsset.MetadataJson, "rawVideoMediaAssetId") == rawVideoAssetId.ToString("D") &&
                ReadStringProperty(existingNarratedAsset.MetadataJson, "voiceSnapshotHash") == approvedVoiceSnapshotHash &&
                string.Equals(
                    ReadStringProperty(existingNarratedAsset.MetadataJson, "voiceGenerationId"),
                    voiceGenerationId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    ReadStringProperty(existingNarratedAsset.MetadataJson, "speechHash"),
                    narrationHash,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    ReadStringProperty(existingNarratedAsset.MetadataJson, "mixStrategy"),
                    SpeechMixStrategies.ReplaceAllNativeAudio,
                    StringComparison.Ordinal) &&
                string.Equals(
                    ReadStringProperty(existingNarratedAsset.MetadataJson, "audioSyncPolicyVersion"),
                    SceneAudioSyncPolicyVersion,
                    StringComparison.Ordinal);
            var cachedFileValid = false;
            if (existingMatchesVoice && existingNarratedAsset is not null && File.Exists(narratedPath))
            {
                await using var cachedStream = File.OpenRead(narratedPath);
                var cachedHash = Convert.ToHexString(await SHA256.HashDataAsync(cachedStream, cancellationToken)).ToLowerInvariant();
                cachedFileValid = string.Equals(cachedHash, existingNarratedAsset.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            if (cachedFileValid)
            {
                await using var updateContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                await RequireProjectAsync(updateContext, projectId, remoteUserId, cancellationToken);
                var currentScene = await updateContext.Scenes.SingleAsync(
                    x => x.SceneId == scene.SceneId,
                    cancellationToken);
                currentScene.ApprovedGenerationId = null;
                currentScene.ApprovedRenderMediaAssetId = null;
                currentScene.SpeechStatus = "SpeechReviewRequired";
                currentScene.Status = "AudioReviewRequired";
                currentScene.UpdatedAtUtc = DateTime.UtcNow;
                await updateContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var narratedPartialPath = narratedPath + ".part";
        if (File.Exists(narratedPartialPath))
        {
            File.Delete(narratedPartialPath);
        }
        SceneAudioMixResult mixResult;
        try
        {
            mixResult = await sceneAudioMixer.MixAsync(
                rawVideoPath,
                voicePath,
                narratedPartialPath,
                scene.ContentDurationMs / 1000m,
                cancellationToken,
                SpeechMixStrategies.ReplaceAllNativeAudio,
                nativeAmbienceVerifiedNoSpeech: false);
            File.Move(narratedPartialPath, narratedPath, true);
        }
        catch
        {
            if (File.Exists(narratedPartialPath))
            {
                File.Delete(narratedPartialPath);
            }
            throw;
        }

        string narratedHash;
        await using (var stream = new FileStream(
            narratedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            narratedHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }
        var narratedFile = new FileInfo(narratedPath);
        await using (var writeContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await RequireProjectAsync(writeContext, projectId, remoteUserId, cancellationToken);
            var narratedAsset = await writeContext.MediaAssets.SingleOrDefaultAsync(
                x => x.ProjectId == projectId && x.RelativePath == narratedAssetRelativePath,
                cancellationToken);
            var voiceSnapshot = await writeContext.VoiceGenerations
                .Where(x => x.VoiceGenerationId == voiceGenerationId)
                .Select(x => new { x.VoiceSnapshotHash, x.VoiceProfileVersionId })
                .SingleAsync(cancellationToken);
            var narratedMetadata = JsonSerializer.Serialize(new
            {
                RawVideoMediaAssetId = rawVideoAssetId,
                VoiceMediaAssetId = voiceAssetId,
                VoiceProviderRequestId = voiceProviderRequestId,
                VoiceGenerationId = voiceGenerationId,
                voiceSnapshot.VoiceSnapshotHash,
                voiceSnapshot.VoiceProfileVersionId,
                SpeechHash = narrationHash,
                audioSyncPolicyVersion = SceneAudioSyncPolicyVersion,
                mixResult.MixStrategy,
                mixResult.PreservedNativeAudio,
                mixResult.VoiceDurationSeconds,
                mixResult.TargetDurationSeconds,
                mixResult.AppliedTempo,
                mixResult.TrailingPaddingSeconds,
                mixResult.SourceSpeechStartMs,
                mixResult.SourceSpeechEndMs,
                mixResult.TargetSpeechStartMs,
                mixResult.EffectiveVoiceDurationSeconds,
                canonicalVoiceAudible = mixResult.OutputAudioQuality.IsAudible,
                mixResult.OutputAudioQuality.MeanVolumeDb,
                mixResult.OutputAudioQuality.MaxVolumeDb,
                mixResult.OutputAudioQuality.SilentRatio
            }, JsonOptions);
            if (narratedAsset is null)
            {
                narratedAsset = new MediaAsset
                {
                    MediaAssetId = Guid.NewGuid(),
                    ProjectId = projectId,
                    SceneId = scene.SceneId,
                    AssetType = "SceneVideoNarrated",
                    DisplayName = $"Cảnh {scene.SequenceNumber} có lời đọc",
                    RelativePath = narratedAssetRelativePath,
                    CreatedAtUtc = DateTime.UtcNow,
                    RowVersion = new byte[8]
                };
                writeContext.MediaAssets.Add(narratedAsset);
            }
            narratedAsset.MimeType = "video/mp4";
            narratedAsset.SizeBytes = narratedFile.Length;
            narratedAsset.Sha256 = narratedHash;
            narratedAsset.Width = mixResult.OutputProbe.Width;
            narratedAsset.Height = mixResult.OutputProbe.Height;
            narratedAsset.FrameRate = mixResult.OutputProbe.FramesPerSecond;
            narratedAsset.DurationMs = checked((long)Math.Round(
                mixResult.OutputProbe.DurationSeconds * 1000m,
                MidpointRounding.AwayFromZero));
            narratedAsset.AudioSampleRate = mixResult.OutputProbe.AudioSampleRate;
            narratedAsset.Status = "Ready";
            narratedAsset.SourceType = "Generated";
            narratedAsset.SourceProviderCode = "local-ffmpeg";
            narratedAsset.MetadataJson = narratedMetadata;
            narratedAsset.VerifiedAtUtc = DateTime.UtcNow;
            var localScene = await writeContext.Scenes.SingleAsync(x => x.SceneId == scene.SceneId, cancellationToken);
            localScene.ApprovedGenerationId = null;
            localScene.ApprovedRenderMediaAssetId = null;
            localScene.SpeechStatus = "SpeechReviewRequired";
            localScene.Status = "AudioReviewRequired";
            localScene.LastErrorCode = null;
            localScene.LastErrorMessage = null;
            localScene.UpdatedAtUtc = DateTime.UtcNow;
            await writeContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<SpeechVerificationSource> ResolveSpeechVerificationSourceAsync(
        Guid projectId,
        string remoteUserId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        if (sceneId == Guid.Empty)
        {
            throw new ArgumentException("Cảnh cần kiểm tra lời nói không hợp lệ.", nameof(sceneId));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        var scene = await dbContext.Scenes.AsNoTracking()
            .Where(x => x.SceneId == sceneId &&
                        x.ProjectId == projectId &&
                        x.ScenePlanVersion == project.CurrentScenePlanVersion)
            .Select(x => new SceneWorkItem(
                x.SceneId,
                x.ScriptId,
                x.ScenePlanVersion,
                x.SequenceNumber,
                x.ContentDurationMs,
                x.GenerationDurationMs,
                x.Narration,
                x.Dialogue,
                x.RequiredCapabilitiesJson,
                x.Status,
                x.CharacterIdsJson,
                null))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy cảnh hiện hành cần kiểm tra lời nói.");
        if (string.IsNullOrWhiteSpace(scene.SpokenText))
        {
            throw new ArgumentException("Cảnh không có lời nói cần kiểm tra.");
        }

        var expectedSpeechHash = Sha256Hex(NormalizeNarration(scene.SpokenText));
        var canonical = string.Equals(
            project.SpeechProductionPolicy,
            SpeechProductionPolicies.CanonicalVoice,
            StringComparison.Ordinal);
        if (canonical)
        {
            throw new AccountClientException(
                SpeechSynchronizationErrorCodes.SpeechVerificationNotRequired,
                "Canonical Voice dùng kiểm tra kỹ thuật WAV cục bộ; không chạy kiểm tra transcript ASR.",
                409);
        }

        var asset = await dbContext.VideoGenerations.AsNoTracking()
            .Where(x => x.SceneId == sceneId &&
                        x.OutputMediaAssetId != null &&
                        x.OutputMediaAsset!.AssetType == "SceneVideo" &&
                        x.OutputMediaAsset.Status == "Ready" &&
                        x.OutputMediaAsset.DeletedAtUtc == null)
            .OrderByDescending(x => x.AttemptNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new { AssetId = x.OutputMediaAssetId!.Value, x.OutputMediaAsset!.RelativePath, x.OutputMediaAsset.DurationMs })
            .FirstOrDefaultAsync(cancellationToken);
        if (asset is null || string.IsNullOrWhiteSpace(asset.RelativePath))
        {
            throw new InvalidOperationException("Cảnh chưa có clip Native Audio để kiểm tra transcript.");
        }

        var sourcePath = workspaceService.Resolve(Path.Combine(
            project.WorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar),
            asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Không tìm thấy media cần kiểm tra trong workspace.", sourcePath);
        }

        return new SpeechVerificationSource(
            scene,
            asset.AssetId,
            sourcePath,
            Math.Max(1, asset.DurationMs ?? scene.GenerationDurationMs),
            expectedSpeechHash,
            ExtractAudio: true);
    }

    private async Task<bool> IsSceneApprovedAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Scenes.AsNoTracking().AnyAsync(
            x => x.SceneId == sceneId &&
                 x.Status == "Approved" &&
                 x.ApprovedGenerationId != null &&
                 x.ApprovedRenderMediaAssetId != null &&
                 (x.SpeechStatus == SceneSpeechStatuses.SpeechNotRequired ||
                  x.SpeechStatus == SceneSpeechStatuses.SpeechApproved),
            cancellationToken);
    }

    private async Task<bool> IsCanonicalVoiceApprovedAsync(
        SceneWorkItem scene,
        CancellationToken cancellationToken)
    {
        var expectedSpeechHash = Sha256Hex(NormalizeNarration(scene.SpokenText));
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Scenes
            .AsNoTracking()
            .Where(x => x.SceneId == scene.SceneId && x.ApprovedVoiceGenerationId != null)
            .AnyAsync(
                x => x.ApprovedVoiceGeneration!.Status == "Approved" &&
                     x.ApprovedVoiceGeneration.ApprovedAtUtc != null &&
                     x.ApprovedVoiceGeneration.ScenePlanVersion == scene.ScenePlanVersion &&
                     x.ApprovedVoiceGeneration.NarrationHash == expectedSpeechHash &&
                     x.ApprovedVoiceGeneration.OutputMediaAssetId != null &&
                     x.ApprovedVoiceGeneration.OutputMediaAsset!.Status == "Ready" &&
                     x.ApprovedVoiceGeneration.OutputMediaAsset.DeletedAtUtc == null,
                cancellationToken);
    }

    private async Task<bool> AreAllScenesApprovedAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var planVersion = await dbContext.Projects
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.CurrentScenePlanVersion)
            .SingleAsync(cancellationToken);
        return planVersion.HasValue &&
               await dbContext.Scenes
                   .AsNoTracking()
                   .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == planVersion.Value)
                   .AllAsync(
                       x => x.Status == "Approved" &&
                            x.ApprovedGenerationId != null &&
                            x.ApprovedRenderMediaAssetId != null &&
                            (x.SpeechStatus == SceneSpeechStatuses.SpeechNotRequired ||
                             x.SpeechStatus == SceneSpeechStatuses.SpeechApproved),
                       cancellationToken);
    }

    private async Task UpdateProjectStatusAsync(
        Guid projectId,
        string remoteUserId,
        string status,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        project.Status = status;
        project.LastErrorCode = errorCode;
        project.LastErrorMessage = errorMessage;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkMediaToolBlockedAsync(
        Guid projectId,
        string remoteUserId,
        IReadOnlyCollection<Guid>? requestedSceneIds,
        MediaToolUnavailableException exception,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var project = await RequireProjectAsync(dbContext, projectId, remoteUserId, cancellationToken);
        var requested = requestedSceneIds?.ToArray();
        var generations = await dbContext.VideoGenerations
            .Include(x => x.Scene)
            .Include(x => x.ProviderRequest)
            .Where(x =>
                x.Scene.ProjectId == projectId &&
                x.ProviderRequest.Status == "Completed" &&
                x.OutputMediaAssetId == null &&
                (x.Status == "Downloading" || x.Status == "Generated") &&
                (requested == null || requested.Contains(x.SceneId)))
            .ToListAsync(cancellationToken);
        foreach (var generation in generations)
        {
            generation.Status = "Generated";
            generation.Scene.Status = "Generated";
            generation.Scene.LastErrorCode = SafeCode(exception.Code);
            generation.Scene.LastErrorMessage = SafeMessage(exception.Message);
            generation.Scene.UpdatedAtUtc = DateTime.UtcNow;
        }

        project.Status = "ScenePlanning";
        project.LastErrorCode = SafeCode(exception.Code);
        project.LastErrorMessage = SafeMessage(exception.Message);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Project> RequireProjectAsync(
        VideoFactoryDbContext dbContext,
        Guid projectId,
        string remoteUserId,
        CancellationToken cancellationToken) =>
        await dbContext.Projects.SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.RemoteUserId == remoteUserId && x.DeletedAtUtc == null,
            cancellationToken)
        ?? throw new ArgumentException("Không tìm thấy dự án của tài khoản hiện tại.");

    private static void ValidateContentPlan(
        GeneratedContentPlan plan,
        bool enforceKlingLongFormSpeechPolicy = false)
    {
        if (plan.Scenes.Count == 0 || plan.Scenes.Any(x =>
                x.DurationSeconds is < 1 or > 30 ||
                (x.GenerationDurationSeconds ?? x.DurationSeconds) < x.DurationSeconds ||
                (x.GenerationDurationSeconds ?? x.DurationSeconds) > 30))
        {
            throw new InvalidDataException("Content plan OpenAI không có danh sách cảnh hợp lệ.");
        }

        if (plan.Scenes.Select(x => x.SequenceNumber).Distinct().Count() != plan.Scenes.Count)
        {
            throw new InvalidDataException("Content plan OpenAI có số thứ tự cảnh bị trùng.");
        }

        var characterKeys = plan.Characters
            .Select(x => x.CharacterKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (characterKeys.Count != plan.Characters.Count ||
            plan.Characters.Any(character =>
                string.IsNullOrWhiteSpace(character.CharacterKey) ||
                string.IsNullOrWhiteSpace(character.Name) ||
                string.IsNullOrWhiteSpace(character.VisualIdentity)) ||
            plan.Scenes.Any(scene => scene.CharacterKeys.Any(key => !characterKeys.Contains(key))))
        {
            throw new InvalidDataException("Content plan OpenAI có liên kết nhân vật không hợp lệ.");
        }

        var assets = plan.Assets ?? [];
        if (assets.Count > 0)
        {
            if (assets.Select(x => x.AssetKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assets.Count ||
                assets.Any(asset =>
                    string.IsNullOrWhiteSpace(asset.AssetKey) ||
                    string.IsNullOrWhiteSpace(asset.Name) ||
                    string.IsNullOrWhiteSpace(asset.CanonicalDescription) ||
                    !ProjectAssetTypes.IsSupported(asset.AssetType)))
            {
                throw new InvalidDataException("Content plan OpenAI có thư viện tài sản không hợp lệ.");
            }
            var assetByKey = assets.ToDictionary(x => x.AssetKey, StringComparer.OrdinalIgnoreCase);
            foreach (var scene in plan.Scenes)
            {
                var sceneAssetKeys = (scene.AssetKeys ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (sceneAssetKeys.Length == 0 ||
                    sceneAssetKeys.Any(key => !assetByKey.ContainsKey(key)) ||
                    sceneAssetKeys.Count(key => assetByKey[key].AssetType == ProjectAssetTypes.Background) != 1)
                {
                    throw new InvalidDataException("Content plan OpenAI có liên kết tài sản và cảnh không hợp lệ.");
                }
            }
        }

        foreach (var scene in plan.Scenes)
        {
            var mode = ResolveSpeechMode(scene);
            var spokenText = NormalizeNarration(scene.Narration);
            var speaker = NullIfWhiteSpace(scene.SpeakerCharacterKey);
            if (mode == KlingSpeechModes.None)
            {
                if (spokenText.Length > 0 || speaker is not null)
                {
                    throw new InvalidDataException("Cảnh không lời không được chứa nội dung hoặc người nói.");
                }
                continue;
            }

            if (spokenText.Length == 0)
            {
                throw new InvalidDataException("Cảnh có lời provider nhưng nội dung lời đang trống.");
            }
            if (mode == KlingSpeechModes.OnCameraDialogue &&
                (scene.CharacterKeys.Count != 1 ||
                 speaker is null ||
                 !string.Equals(speaker, scene.CharacterKeys[0], StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Cảnh thoại trực tiếp phải gắn đúng một nhân vật nói.");
            }
            if (mode == KlingSpeechModes.NativeVoiceOver && speaker is not null)
            {
                throw new InvalidDataException("Cảnh voice-over không được gắn nhân vật nói trực tiếp.");
            }
            if (enforceKlingLongFormSpeechPolicy)
            {
                var violation = KlingLongFormSpeechIntentValidator.FindViolation(
                    mode,
                    spokenText,
                    speaker,
                    scene.CharacterKeys.Count);
                if (violation is not null)
                {
                    throw new InvalidDataException(violation);
                }
            }
        }
    }

    private static string ResolveSpeechMode(GeneratedContentScene scene)
    {
        if (scene.SpeechMode is KlingSpeechModes.None or
            KlingSpeechModes.OnCameraDialogue or
            KlingSpeechModes.NativeVoiceOver)
        {
            return scene.SpeechMode;
        }

        if (string.IsNullOrWhiteSpace(scene.Narration))
        {
            return KlingSpeechModes.None;
        }

        return scene.CharacterKeys.Count == 1
            ? KlingSpeechModes.OnCameraDialogue
            : KlingSpeechModes.NativeVoiceOver;
    }

    private static string ComposeKlingPrompt(string positive, string? negative)
    {
        var prompt = string.IsNullOrWhiteSpace(negative)
            ? positive.Trim()
            : $"{positive.Trim()} Avoid: {negative.Trim()}";
        return prompt.Length <= 3072 ? prompt : prompt[..3072];
    }

    private static IReadOnlyList<Guid> ParseGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Guid[]>(json)?.Distinct().ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool ReadBooleanProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadStringProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string LocalGenerationStatus(string taskStatus) => taskStatus switch
    {
        "Completed" => "Generated",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        "Expired" => "Failed",
        _ => "WaitingProvider"
    };

    private static string LocalSceneStatus(string taskStatus) => taskStatus switch
    {
        "Completed" => "Generated",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        "Expired" => "Failed",
        _ => "WaitingProvider"
    };

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeNarration(string? value) =>
        SpeechTextNormalization.Normalize(value);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (int Width, int Height) Dimensions(string aspectRatio, string resolution) =>
        (aspectRatio, resolution) switch
        {
            ("9:16", "720p") => (720, 1280),
            ("1:1", "720p") => (720, 720),
            _ => (1280, 720)
        };

    private static string SafeCode(string value) => value.Length <= 100 ? value : value[..100];

    private static string SafeMessage(string value) => value.Length <= 4000 ? value : value[..4000];

    private sealed record SceneWorkItem(
        Guid SceneId,
        Guid ScriptId,
        int ScenePlanVersion,
        int SequenceNumber,
        long ContentDurationMs,
        long GenerationDurationMs,
        string? Narration,
        string? Dialogue,
        string? RequiredCapabilitiesJson,
        string Status,
        string? CharacterIdsJson,
        PromptWorkItem? Prompt)
    {
        public string? SpokenText => !string.IsNullOrWhiteSpace(Dialogue) ? Dialogue : Narration;

        public string SpeechMode => !string.IsNullOrWhiteSpace(Dialogue)
            ? KlingSpeechModes.OnCameraDialogue
            : !string.IsNullOrWhiteSpace(Narration)
                ? KlingSpeechModes.NativeVoiceOver
                : KlingSpeechModes.None;

        public CharacterWorkItem? Character { get; init; }
    }

    private sealed record PromptWorkItem(
        Guid ScenePromptId,
        int Version,
        string FinalPrompt,
        string? NegativePrompt);

    private sealed record CharacterWorkItem(
        Guid CharacterId,
        string Name,
        string Status,
        string? VoiceCode,
        decimal? VoiceSpeakingRate,
        Guid? ApprovedVoiceProfileVersionId,
        ReferenceWorkItem? Reference);

    private sealed record SpeechVerificationSource(
        SceneWorkItem Scene,
        Guid SourceMediaAssetId,
        string SourcePath,
        long DurationMs,
        string ExpectedSpeechHash,
        bool ExtractAudio);

    private sealed record ReferenceWorkItem(
        Guid CharacterReferenceId,
        string RelativePath,
        string MimeType,
        string Sha256,
        long SizeBytes);
}
