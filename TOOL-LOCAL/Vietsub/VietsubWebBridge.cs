using System.Collections.Concurrent;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Playback;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_LOCAL.WebView;

namespace TOOL_LOCAL.Vietsub;

internal sealed record VietsubUserContext(string UserId, Guid OrganizationId);

internal sealed record CreateVietsubProjectRequest(string Name);

internal sealed record OpenVietsubProjectRequest(Guid ProjectId);

internal sealed record RenameVietsubProjectRequest(Guid ProjectId, string Name);

internal sealed record ImportVietsubMediaRequest(string Mode);

internal sealed record ImportVietsubSrtRequest(string LanguageCode);

internal sealed record ActivateVietsubSubtitleTrackRequest(Guid TrackId);

internal sealed record GetVietsubSubtitlePageRequest(
    Guid? TrackId,
    int Offset,
    int PageSize,
    string? Search,
    string? Status,
    string? Speaker);

internal sealed record UpdateVietsubSubtitleCueRequest(
    Guid CueId,
    string OriginalText,
    string TranslatedText,
    string Speaker,
    Guid? ExpectedTrackId = null,
    int? ExpectedTrackRevision = null);

internal sealed record VietsubSubtitleCueTimelineRequest(Guid CueId, long PositionMilliseconds);

internal sealed record GetVietsubTimelineWindowRequest(
    Guid? TrackId,
    long WindowStartMilliseconds,
    long WindowEndMilliseconds,
    int MaximumCues);

internal sealed record UpdateVietsubTimelineCueRequest(
    Guid TrackId,
    Guid CueId,
    int ExpectedTrackRevision,
    long StartMilliseconds,
    long EndMilliseconds);

internal sealed record VietsubSubtitleCueRequest(Guid CueId);

internal sealed record ExportVietsubSrtRequest(string Mode);

internal sealed record UpdateVietsubSubtitleStyleRequest(
    VietsubSubtitleStyle Style,
    VietsubAudioMixSettings? AudioMixSettings = null,
    VietsubVideoTransformSettings? VideoTransformSettings = null);

internal sealed record VietsubJobRequest(Guid JobId);

internal sealed record VietsubOcrPreviewRequest(
    string LanguageCode,
    string Profile,
    VietsubNormalizedRegion Region,
    long TimestampMilliseconds);

internal sealed record ActivateVietsubOcrTrackRequest(Guid JobId, bool ConfirmImpact);

internal sealed record RequestVietsubTimelineThumbnails(
    string SourceSha256,
    int[] Indices);

internal sealed record RequestVietsubTimelineWaveform(string SourceSha256);

internal sealed class VietsubWebBridge : IDisposable
{
    private const string MessagePrefix = "vietsub.";
    private const int MaxMessageLength = 64 * 1024;
    private readonly bool _enabled;
    private readonly Action<string> _postJson;
    private readonly VietsubProjectStore? _projectStore;
    private readonly Func<VietsubUserContext?>? _contextProvider;
    private readonly IVietsubProjectRegistryClient? _registryClient;
    private readonly VietsubMediaImportService? _mediaImportService;
    private readonly Func<string?>? _mediaFileSelector;
    private readonly VietsubMediaPlaybackService? _mediaPlaybackService;
    private readonly VietsubTimelineThumbnailService? _thumbnailService;
    private readonly VietsubTimelineWaveformService? _waveformService;
    private readonly VietsubSubtitleService? _subtitleService;
    private readonly Func<string?>? _subtitleFileSelector;
    private readonly Func<string?>? _subtitleExportSelector;
    private readonly Func<string?>? _videoExportSelector;
    private readonly VietsubJobManager? _jobManager;
    private readonly VietsubOcrService? _ocrService;
    private readonly VietsubTranslationService? _translationService;
    private readonly VietsubCloudTranslationService? _cloudTranslationService;
    private readonly VietsubVoiceService? _voiceService;
    private readonly VietsubVideoExportService? _videoExportService;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly object _operationSync = new();
    private readonly ConcurrentDictionary<Guid, byte> _ocrCompletionInFlight = new();
    private readonly ConcurrentDictionary<Guid, byte> _translationCompletionHandled = new();
    private readonly ConcurrentDictionary<Guid, byte> _voiceCompletionHandled = new();
    private readonly ConcurrentDictionary<string, byte> _waveformFailures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _timelineArtifactTasks = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeOperation;
    private CancellationTokenSource? _timelineArtifactCancellation;
    private string? _activeOperationRequestId;
    private VietsubProjectSession? _projectSession;
    private bool _disposed;

    public VietsubWebBridge(
        bool enabled,
        Action<string> postJson,
        VietsubProjectStore? projectStore = null,
        Func<VietsubUserContext?>? contextProvider = null,
        IVietsubProjectRegistryClient? registryClient = null,
        VietsubMediaImportService? mediaImportService = null,
        Func<string?>? mediaFileSelector = null,
        VietsubMediaPlaybackService? mediaPlaybackService = null,
        VietsubTimelineThumbnailService? thumbnailService = null,
        VietsubSubtitleService? subtitleService = null,
        Func<string?>? subtitleFileSelector = null,
        Func<string?>? subtitleExportSelector = null,
        VietsubJobManager? jobManager = null,
        VietsubOcrService? ocrService = null,
        VietsubTimelineWaveformService? waveformService = null,
        VietsubTranslationService? translationService = null,
        VietsubVoiceService? voiceService = null,
        Func<string?>? videoExportSelector = null,
        VietsubVideoExportService? videoExportService = null,
        VietsubCloudTranslationService? cloudTranslationService = null)
    {
        _enabled = enabled;
        _postJson = postJson;
        _projectStore = projectStore;
        _contextProvider = contextProvider;
        _registryClient = registryClient;
        _mediaImportService = mediaImportService;
        _mediaFileSelector = mediaFileSelector;
        _mediaPlaybackService = mediaPlaybackService;
        _thumbnailService = thumbnailService;
        _subtitleService = subtitleService;
        _subtitleFileSelector = subtitleFileSelector;
        _subtitleExportSelector = subtitleExportSelector;
        _videoExportSelector = videoExportSelector;
        _jobManager = jobManager;
        _ocrService = ocrService;
        _waveformService = waveformService;
        _translationService = translationService;
        _cloudTranslationService = cloudTranslationService;
        _voiceService = voiceService;
        _videoExportService = videoExportService;
        if (_jobManager is not null)
        {
            _jobManager.JobChanged += JobManagerOnChanged;
        }
        if (_thumbnailService is not null)
        {
            _thumbnailService.ThumbnailReady += ThumbnailServiceOnReady;
            _thumbnailService.ThumbnailFailed += ThumbnailServiceOnFailed;
        }
    }

    public async Task<bool> TryHandleAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxMessageLength)
        {
            return false;
        }

        WebMessageRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<WebMessageRequest>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request?.Type?.StartsWith(MessagePrefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            PostError(null, "vietsub_request_id_required", "Yêu cầu Vietsub phải có mã đối chiếu.");
            return true;
        }

        if (!_enabled)
        {
            PostError(request.RequestId, "vietsub_feature_disabled", "Tính năng dịch phụ đề chưa được bật.");
            return true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (request.Type)
            {
                case "vietsub.state.get":
                case "vietsub.refresh":
                    await PostStateAsync(request.RequestId, cancellationToken);
                    break;
                case "vietsub.project.create":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => CreateProjectAsync(request, token),
                        cancellationToken);
                    break;
                case "vietsub.project.open":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => OpenProjectAsync(request, token),
                        cancellationToken);
                    break;
                case "vietsub.project.rename":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => RenameProjectAsync(request, token),
                        cancellationToken);
                    break;
                case "vietsub.project.close":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        CloseCurrentSessionAsync,
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.media.import":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => ImportMediaAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.subtitle.import":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => ImportSrtAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.subtitle.track.activate":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => ActivateSubtitleTrackAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.subtitle.page.get":
                    await PostSubtitlePageAsync(request, request.RequestId, cancellationToken);
                    break;
                case "vietsub.subtitle.style.get":
                    PostSubtitleStyle(request.RequestId);
                    break;
                case "vietsub.subtitle.style.update":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => UpdateSubtitleStyleAsync(request, request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.timeline.window.get":
                    await PostTimelineWindowAsync(request, request.RequestId, cancellationToken);
                    break;
                case "vietsub.timeline.thumbnails.request":
                    RequestTimelineThumbnails(request);
                    break;
                case "vietsub.timeline.waveform.request":
                    RequestTimelineWaveform(request, request.RequestId);
                    break;
                case "vietsub.timeline.cue.update":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => UpdateTimelineCueAsync(request, request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.subtitle.voice.update":
                    await RunProjectOperationAsync(request.RequestId,
                        token => UpdateCueVoiceSelectionAsync(request, request.RequestId, token),
                        cancellationToken, notifyCompletion: true);
                    break;
                case "vietsub.subtitle.cue.update":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => UpdateSubtitleCueAsync(request, request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.subtitle.cue.split":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => SplitSubtitleCueAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.subtitle.cue.align-start":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => AlignSubtitleCueAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.subtitle.cue.duplicate":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => DuplicateSubtitleCueAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.subtitle.cue.delete":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => DeleteSubtitleCueAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.subtitle.export":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => ExportSrtAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.operation.cancel":
                    await CancelActiveOperationAsync(request.RequestId, cancellationToken);
                    break;
                case "vietsub.video.export":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => ExportVideoAsync(request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.job.status":
                    await PostJobStatusAsync(request, request.RequestId, cancellationToken);
                    break;
                case "vietsub.job.pause":
                    await ControlJobAsync(request, request.RequestId, "PAUSE", cancellationToken);
                    break;
                case "vietsub.job.resume":
                    await ControlJobAsync(request, request.RequestId, "RESUME", cancellationToken);
                    break;
                case "vietsub.job.retry":
                    await ControlJobAsync(request, request.RequestId, "RETRY", cancellationToken);
                    break;
                case "vietsub.job.cancel":
                    await ControlJobAsync(request, request.RequestId, "CANCEL", cancellationToken);
                    break;
                case "vietsub.ocr.runtime.status":
                    await PostOcrRuntimeStatusAsync(request.RequestId, cancellationToken);
                    break;
                case "vietsub.translation.runtime.status":
                    PostTranslationRuntimeStatus(request.RequestId);
                    break;
                case "vietsub.translation.runtime.install":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => InstallTranslationRuntimeAsync(request, request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.voice.runtime.status":
                    PostVoiceRuntimeStatus(request.RequestId);
                    break;
                case "vietsub.voice.models.status":
                    await PostVoiceModelStatusesAsync(request.RequestId, cancellationToken);
                    break;
                case "vietsub.voice.model.install":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => InstallVoiceModelAsync(request, request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.voice.select":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => SelectVoiceAsync(request, request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.voice.runtime.install":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => InstallVoiceRuntimeAsync(request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.ocr.region.update":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => UpdateOcrSettingsAsync(request, request.RequestId, token),
                        cancellationToken,
                        notifyCompletion: true);
                    break;
                case "vietsub.ocr.preview":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => PreviewOcrAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.job.ocr":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => StartOcrAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.job.translate":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => StartTranslationAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.cloud.availability":
                    var cloudSession = RequireProjectSession();
                    var cloudStatus = _cloudTranslationService is null
                        ? new TOOL_SHARED.Contracts.Vietsub.VietsubCloudAvailability(false, "CLOUD_DISABLED", "Dịch Cloud chưa được bật.")
                        : await _cloudTranslationService.AvailabilityAsync(cloudSession, cancellationToken);
                    Post(new WebMessageResponse("vietsub.cloud.availability", request.RequestId,
                        new { projectId = cloudSession.Manifest.ProjectId, organizationId = cloudSession.Manifest.OrganizationId, availability = cloudStatus }));
                    break;
                case "vietsub.job.translate.cloud":
                    await RunProjectOperationAsync(request.RequestId,
                        token => StartCloudTranslationAsync(request, request.RequestId, token), cancellationToken);
                    break;
                case "vietsub.job.voice":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => StartVoiceAsync(request, request.RequestId, token),
                        cancellationToken);
                    break;
                case "vietsub.ocr.track.activate":
                    await ActivateCompletedOcrTrackAsync(request, request.RequestId, cancellationToken);
                    break;
                default:
                    PostError(
                        request.RequestId,
                        "vietsub_operation_not_supported",
                        "Chức năng Vietsub này chưa được hỗ trợ.");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            PostError(request.RequestId, "vietsub_operation_cancelled", "Tác vụ Vietsub đã được hủy.");
        }
        catch (JsonException)
        {
            PostError(request.RequestId, "vietsub_invalid_payload", "Dữ liệu Vietsub không đúng định dạng.");
        }
        catch (VietsubMediaException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (VietsubSubtitleException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (VietsubJobException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (VietsubOcrException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (VietsubTranslationException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (VietsubVoiceException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (VietsubVideoExportException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            PostError(request.RequestId, "vietsub_validation_failed", exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            PostError(request.RequestId, "vietsub_access_denied", "Bạn không có quyền truy cập dự án Vietsub này.");
        }
        catch (FileNotFoundException exception)
        {
            PostError(request.RequestId, "vietsub_project_not_found", exception.Message);
        }
        catch (InvalidDataException)
        {
            PostError(request.RequestId, "vietsub_project_corrupted", "Dữ liệu dự án Vietsub bị lỗi hoặc chưa được hỗ trợ.");
        }
        catch (InvalidOperationException exception)
        {
            PostError(request.RequestId, "vietsub_operation_conflict", exception.Message);
        }
        catch (Exception)
        {
            PostError(request.RequestId, "vietsub_operation_failed", "Không thể hoàn tất thao tác Vietsub.");
        }

        return true;
    }

    internal CancellationToken BeginOperation(string requestId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("Mã đối chiếu không được để trống.", nameof(requestId));
        }

        lock (_operationSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeOperation is not null)
            {
                throw new InvalidOperationException("Một tác vụ Vietsub khác đang chạy.");
            }

            _activeOperationRequestId = requestId;
            _activeOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return _activeOperation.Token;
        }
    }

    internal void CompleteOperation(string requestId)
    {
        lock (_operationSync)
        {
            if (!string.Equals(_activeOperationRequestId, requestId, StringComparison.Ordinal))
            {
                return;
            }

            _activeOperation?.Dispose();
            _activeOperation = null;
            _activeOperationRequestId = null;
        }
    }

    private async Task RunProjectOperationAsync(
        string requestId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool notifyCompletion = false)
    {
        var operationToken = BeginOperation(requestId, cancellationToken);
        await PostStateAsync(requestId, cancellationToken);
        var completed = false;
        try
        {
            await operation(operationToken);
            completed = true;
        }
        finally
        {
            CompleteOperation(requestId);
            await PostStateAsync(requestId, cancellationToken);
            if (completed && notifyCompletion)
            {
                Post(new WebMessageResponse(
                    "vietsub.operation.completed",
                    requestId,
                    new { completed = true }));
            }
        }
    }

    private async Task CreateProjectAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        var store = RequireProjectStore();
        var context = RequireContext();
        var payload = request.Payload.Deserialize<CreateVietsubProjectRequest>(_jsonOptions)
            ?? throw new JsonException();
        var manifest = await store.CreateAsync(
            context.OrganizationId,
            context.UserId,
            payload.Name,
            cancellationToken: cancellationToken);
        await SynchronizeManifestAsync(manifest, cancellationToken);
        await SelectProjectAsync(manifest, cancellationToken);
    }

    private async Task OpenProjectAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        var store = RequireProjectStore();
        var context = RequireContext();
        var payload = request.Payload.Deserialize<OpenVietsubProjectRequest>(_jsonOptions)
            ?? throw new JsonException();
        if (payload.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("Mã dự án Vietsub không hợp lệ.");
        }

        var manifest = await store.OpenAsync(
            payload.ProjectId,
            context.OrganizationId,
            context.UserId,
            cancellationToken);
        if (!manifest.ServerSynchronized)
        {
            await SynchronizeManifestAsync(manifest, cancellationToken);
        }
        await SelectProjectAsync(manifest, cancellationToken);
        await PostStateAsync(request.RequestId!, cancellationToken);
        await EnsureTimelineArtifactsAsync(manifest, request.RequestId!, cancellationToken);
    }

    private async Task RenameProjectAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        var store = RequireProjectStore();
        var context = RequireContext();
        var payload = request.Payload.Deserialize<RenameVietsubProjectRequest>(_jsonOptions)
            ?? throw new JsonException();
        var renamed = await store.RenameAsync(
            payload.ProjectId,
            context.OrganizationId,
            context.UserId,
            payload.Name,
            cancellationToken);
        renamed.ServerSynchronized = false;
        renamed.ServerSyncErrorCode = null;
        await store.SaveAsync(renamed, cancellationToken);
        await SynchronizeManifestAsync(renamed, cancellationToken);
        if (_projectSession?.Manifest.ProjectId == payload.ProjectId)
        {
            await _projectSession.UpdateAsync(
                manifest =>
                {
                    manifest.Name = renamed.Name;
                    manifest.ServerSynchronized = renamed.ServerSynchronized;
                    manifest.ServerSyncErrorCode = renamed.ServerSyncErrorCode;
                },
                cancellationToken);
        }
    }

    private async Task ImportMediaAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = _projectSession
            ?? throw new InvalidOperationException("Hãy mở một dự án Vietsub trước khi thêm video.");
        var mediaService = _mediaImportService
            ?? throw new InvalidOperationException("Dịch vụ media Vietsub chưa được cấu hình.");
        var selector = _mediaFileSelector
            ?? throw new InvalidOperationException("Trình chọn video Vietsub chưa được cấu hình.");
        var payload = request.Payload.Deserialize<ImportVietsubMediaRequest>(_jsonOptions)
            ?? throw new JsonException();
        var mode = payload.Mode?.Trim().ToUpperInvariant() switch
        {
            VietsubMediaImportModes.Copy => VietsubMediaImportMode.Copy,
            VietsubMediaImportModes.Link => VietsubMediaImportMode.Link,
            _ => throw new ArgumentException("Chế độ nhập video phải là COPY hoặc LINK.")
        };

        var sourcePath = selector();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            Post(new WebMessageResponse(
                "vietsub.media.selection.cancelled",
                requestId,
                new { cancelled = true }));
            return;
        }

        var progress = new Progress<VietsubMediaImportProgress>(value =>
            Post(new WebMessageResponse(
                "vietsub.media.import.progress",
                requestId,
                value)));
        var media = await mediaService.ImportAsync(
            session.Manifest,
            sourcePath,
            mode,
            progress: progress,
            cancellationToken: cancellationToken);
        await session.UpdateAsync(
            manifest =>
            {
                manifest.SourceVideo = media;
                manifest.Status = VietsubProjectStatuses.Ready;
            },
            cancellationToken);
        await session.FlushAsync(cancellationToken);
        await PostStateAsync(requestId, cancellationToken);
        await EnsureTimelineArtifactsAsync(session.Manifest, requestId, cancellationToken);
    }

    private async Task EnsureTimelineArtifactsAsync(
        VietsubProjectManifest project,
        string requestId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartWaveformGeneration(project, requestId);
        await Task.CompletedTask;
    }

    private void StartWaveformGeneration(
        VietsubProjectManifest project,
        string requestId)
    {
        if (_waveformService is null || project.SourceVideo is null)
        {
            return;
        }

        CancellationToken cancellationToken;
        lock (_operationSync)
        {
            if (_disposed || _projectSession?.Manifest.ProjectId != project.ProjectId)
            {
                return;
            }
            _timelineArtifactCancellation ??= new CancellationTokenSource();
            cancellationToken = _timelineArtifactCancellation.Token;
        }
        var key = GetWaveformFailureKey(project);
        _timelineArtifactTasks.GetOrAdd(
            key,
            _ => Task.Run(
                () => RunWaveformGenerationAsync(project, requestId, key, cancellationToken),
                CancellationToken.None));
    }

    private async Task RunWaveformGenerationAsync(
        VietsubProjectManifest project,
        string requestId,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var artifact = await _waveformService!.EnsureAsync(project, cancellationToken);
            if (!IsCurrentTimelineSource(project))
            {
                return;
            }
            _waveformFailures.TryRemove(key, out _);
            Post(new WebMessageResponse(
                "vietsub.timeline.waveform.ready",
                requestId,
                new
                {
                    mediaId = project.SourceVideo!.MediaId,
                    sourceSha256 = project.SourceVideo.Sha256,
                    profileVersion = VietsubTimelineWaveformService.ProfileVersion,
                    artifact.Status,
                    artifact.Url,
                    artifact.Revision
                }));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!IsCurrentTimelineSource(project))
            {
                return;
            }
            _waveformFailures[key] = 0;
            Post(new WebMessageResponse(
                "vietsub.timeline.waveform.failed",
                requestId,
                new
                {
                    resourceType = VietsubPlaybackResourceTypes.Waveform,
                    index = (int?)null,
                    errorCode = exception is VietsubMediaException mediaException
                        ? mediaException.Code
                        : "vietsub_waveform_generation_failed"
                }));
        }
        finally
        {
            _timelineArtifactTasks.TryRemove(key, out _);
        }
    }

    private void RequestTimelineThumbnails(WebMessageRequest request)
    {
        var session = RequireProjectSession();
        var service = _thumbnailService
            ?? throw new InvalidOperationException("Dịch vụ thumbnail timeline chưa được cấu hình.");
        var payload = request.Payload.Deserialize<RequestVietsubTimelineThumbnails>(_jsonOptions)
            ?? throw new JsonException();
        var media = RequireCurrentTimelineMedia(session.Manifest, payload.SourceSha256);
        if (payload.Indices is null || payload.Indices.Length == 0)
        {
            throw new ArgumentException("Danh sách thumbnail cần tạo không được để trống.");
        }
        if (payload.Indices.Length > 64)
        {
            throw new VietsubMediaException(
                "vietsub_thumbnail_request_too_large",
                "Mỗi yêu cầu chỉ được chứa tối đa 64 thumbnail.");
        }
        if (payload.Indices.Any(index =>
            index is < 0 or >= VietsubTimelineThumbnailService.ThumbnailCount))
        {
            throw new VietsubMediaException(
                "vietsub_thumbnail_index_invalid",
                "Index thumbnail không thuộc profile timeline hiện hành.");
        }
        var indices = payload.Indices.Distinct().ToArray();
        _ = media;
        service.Request(session.Manifest, indices);
    }

    private void RequestTimelineWaveform(WebMessageRequest request, string requestId)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<RequestVietsubTimelineWaveform>(_jsonOptions)
            ?? throw new JsonException();
        RequireCurrentTimelineMedia(session.Manifest, payload.SourceSha256);
        StartWaveformGeneration(session.Manifest, requestId);
    }

    private VietsubMediaReference RequireCurrentTimelineMedia(
        VietsubProjectManifest manifest,
        string sourceSha256)
    {
        var context = RequireContext();
        if (context.OrganizationId != manifest.OrganizationId
            || !string.Equals(context.UserId, manifest.OwnerUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException();
        }
        var media = manifest.SourceVideo
            ?? throw new VietsubMediaException("vietsub_media_source_required", "Dự án chưa có video nguồn.");
        if (!string.Equals(sourceSha256, media.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new VietsubMediaException(
                "vietsub_media_artifact_stale",
                "Nguồn media đã thay đổi; hãy tải lại trạng thái timeline.");
        }
        var status = _mediaImportService?.GetSourceStatus(manifest.ProjectId, media)
            ?? throw new InvalidOperationException("Dịch vụ media Vietsub chưa được cấu hình.");
        if (!status.Available || status.Changed || string.IsNullOrWhiteSpace(status.EffectivePath))
        {
            throw new VietsubMediaException(
                status.Changed ? "vietsub_media_source_changed" : "vietsub_media_source_unavailable",
                "Video nguồn không còn sẵn sàng.");
        }
        return media;
    }

    private void ThumbnailServiceOnReady(object? sender, VietsubTimelineThumbnailReady ready)
    {
        if (!IsCurrentTimelineSource(ready.MediaId, ready.SourceSha256))
        {
            return;
        }
        Post(new WebMessageResponse(
            "vietsub.timeline.thumbnail.ready",
            null,
            ready));
    }

    private void ThumbnailServiceOnFailed(object? sender, VietsubTimelineThumbnailFailed failed)
    {
        if (!IsCurrentTimelineSource(failed.MediaId, failed.SourceSha256))
        {
            return;
        }
        Post(new WebMessageResponse(
            "vietsub.timeline.thumbnail.failed",
            null,
            new
            {
                resourceType = VietsubPlaybackResourceTypes.Thumbnail,
                failed.ProfileVersion,
                failed.Index,
                failed.ErrorCode
            }));
    }

    private bool IsCurrentTimelineSource(VietsubProjectManifest project) =>
        project.SourceVideo is { } media && IsCurrentTimelineSource(media.MediaId, media.Sha256);

    private bool IsCurrentTimelineSource(Guid mediaId, string sourceSha256) =>
        !_disposed
        && _projectSession?.Manifest.SourceVideo is { } current
        && current.MediaId == mediaId
        && string.Equals(current.Sha256, sourceSha256, StringComparison.OrdinalIgnoreCase);

    private async Task ImportSrtAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var service = RequireSubtitleService();
        var selector = _subtitleFileSelector
            ?? throw new InvalidOperationException("Trình chọn SRT Vietsub chưa được cấu hình.");
        var payload = request.Payload.Deserialize<ImportVietsubSrtRequest>(_jsonOptions)
            ?? throw new JsonException();
        var sourcePath = selector();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            Post(new WebMessageResponse(
                "vietsub.subtitle.selection.cancelled",
                requestId,
                new { cancelled = true }));
            return;
        }

        var track = await service.ImportSrtAsync(
            session.Manifest,
            sourcePath,
            payload.LanguageCode,
            cancellationToken);
        await session.UpdateAsync(
            manifest => manifest.ActiveSubtitleTrackId = track.TrackId,
            cancellationToken);
        await session.FlushAsync(cancellationToken);
        PostSubtitleChanged(requestId, resetPage: true);
    }

    private async Task ActivateSubtitleTrackAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<ActivateVietsubSubtitleTrackRequest>(_jsonOptions)
            ?? throw new JsonException();
        if (payload.TrackId == Guid.Empty)
        {
            throw new ArgumentException("Mã track phụ đề không hợp lệ.");
        }
        await RequireSubtitleService().ActivateTrackAsync(
            session.Manifest,
            payload.TrackId,
            cancellationToken);
        await session.UpdateAsync(
            manifest => manifest.ActiveSubtitleTrackId = payload.TrackId,
            cancellationToken);
        await session.FlushAsync(cancellationToken);
        PostSubtitleChanged(requestId, resetPage: true);
    }

    private async Task PostSubtitlePageAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<GetVietsubSubtitlePageRequest>(_jsonOptions)
            ?? new GetVietsubSubtitlePageRequest(null, 0, 50, null, null, null);
        var page = await RequireSubtitleService().GetPageAsync(
            session.Manifest,
            new VietsubSubtitlePageQuery(
                payload.TrackId,
                payload.Offset,
                payload.PageSize,
                payload.Search,
                payload.Status,
                payload.Speaker),
            cancellationToken);
        Post(new WebMessageResponse("vietsub.subtitle.page", requestId, page));
    }

    private async Task UpdateSubtitleCueAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<UpdateVietsubSubtitleCueRequest>(_jsonOptions)
            ?? throw new JsonException();
        if (payload.ExpectedTrackId.HasValue != payload.ExpectedTrackRevision.HasValue
            || (payload.ExpectedTrackId.HasValue &&
                (session.Manifest.ActiveSubtitleTrackId != payload.ExpectedTrackId
                 || payload.ExpectedTrackRevision < 1)))
            throw new VietsubSubtitleException("vietsub_timeline_edit_conflict",
                "Track phụ đề đã thay đổi. Hãy tải lại trước khi chỉnh tiếp.");
        await RequireSubtitleService().UpdateCueAsync(
            session.Manifest,
            payload.CueId,
            payload.OriginalText,
            payload.TranslatedText,
            payload.Speaker,
            cancellationToken,
            payload.ExpectedTrackRevision);
        PostSubtitleChanged(requestId, resetPage: false);
    }

    private void PostSubtitleStyle(string requestId)
    {
        var session = RequireProjectSession();
        EnsureCurrentProjectContext(session.Manifest);
        Post(new WebMessageResponse(
            "vietsub.subtitle.style",
            requestId,
            session.Manifest.SubtitleStyle.Copy()));
    }

    private async Task UpdateSubtitleStyleAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        EnsureCurrentProjectContext(session.Manifest);
        var payload = request.Payload.Deserialize<UpdateVietsubSubtitleStyleRequest>(_jsonOptions)
            ?? throw new JsonException();
        var style = payload.Style?.Copy() ?? throw new JsonException();
        style.Normalize();
        var audioMixSettings = payload.AudioMixSettings?.Copy()
            ?? session.Manifest.AudioMixSettings.Copy();
        audioMixSettings.Normalize();
        var videoTransformSettings = payload.VideoTransformSettings?.Copy()
            ?? session.Manifest.VideoTransformSettings.Copy();
        await session.UpdateAsync(manifest =>
        {
            manifest.SubtitleStyle = style;
            manifest.AudioMixSettings = audioMixSettings;
            manifest.VideoTransformSettings = videoTransformSettings;
        }, cancellationToken);
        await session.FlushAsync(cancellationToken);
        Post(new WebMessageResponse(
            "vietsub.subtitle.style.updated",
            requestId,
            style.Copy()));
        Post(new WebMessageResponse(
            "vietsub.audio.mix.updated",
            requestId,
            audioMixSettings.Copy()));
        Post(new WebMessageResponse(
            "vietsub.video.transform.updated",
            requestId,
            videoTransformSettings.Copy()));
    }

    private async Task PostTimelineWindowAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<GetVietsubTimelineWindowRequest>(_jsonOptions)
            ?? throw new JsonException();
        var window = await RequireSubtitleService().GetTimelineWindowAsync(
            session.Manifest,
            new VietsubTimelineWindowQuery(
                payload.TrackId,
                payload.WindowStartMilliseconds,
                payload.WindowEndMilliseconds,
                payload.MaximumCues),
            cancellationToken);
        Post(new WebMessageResponse("vietsub.timeline.window", requestId, window));
    }

    private async Task UpdateTimelineCueAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<UpdateVietsubTimelineCueRequest>(_jsonOptions)
            ?? throw new JsonException();
        var voiceWorkspace = _voiceService is null
            ? null
            : await _voiceService.GetWorkspaceAsync(session.Manifest, cancellationToken);
        var update = await RequireSubtitleService().UpdateCueTimingWithResultAsync(
            session.Manifest,
            payload.TrackId,
            payload.CueId,
            payload.ExpectedTrackRevision,
            payload.StartMilliseconds,
            payload.EndMilliseconds,
            cancellationToken);
        if (_voiceService is not null && voiceWorkspace?.Timeline is { } timeline)
        {
            await _voiceService.TryCarryForwardTimelineAfterCueExtensionAsync(
                session.Manifest,
                payload.CueId,
                timeline,
                update,
                cancellationToken);
        }
        PostSubtitleChanged(requestId, resetPage: false, payload.TrackId, update.TrackRevision);
    }

    private async Task SplitSubtitleCueAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<VietsubSubtitleCueTimelineRequest>(_jsonOptions)
            ?? throw new JsonException();
        await RequireSubtitleService().SplitCueAsync(
            session.Manifest,
            payload.CueId,
            payload.PositionMilliseconds,
            cancellationToken);
        PostSubtitleChanged(requestId, resetPage: false);
    }

    private async Task AlignSubtitleCueAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<VietsubSubtitleCueTimelineRequest>(_jsonOptions)
            ?? throw new JsonException();
        await RequireSubtitleService().AlignCueStartAsync(
            session.Manifest,
            payload.CueId,
            payload.PositionMilliseconds,
            cancellationToken);
        PostSubtitleChanged(requestId, resetPage: false);
    }

    private async Task DuplicateSubtitleCueAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<VietsubSubtitleCueRequest>(_jsonOptions)
            ?? throw new JsonException();
        var cueId = await RequireSubtitleService().DuplicateCueAsync(
            session.Manifest,
            payload.CueId,
            cancellationToken);
        Post(new WebMessageResponse(
            "vietsub.subtitle.cue.duplicated",
            requestId,
            new { cueId }));
        PostSubtitleChanged(requestId, resetPage: false);
    }

    private async Task DeleteSubtitleCueAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var payload = request.Payload.Deserialize<VietsubSubtitleCueRequest>(_jsonOptions)
            ?? throw new JsonException();
        await RequireSubtitleService().DeleteCueAsync(
            session.Manifest,
            payload.CueId,
            cancellationToken);
        PostSubtitleChanged(requestId, resetPage: false);
    }

    private async Task ExportSrtAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var selector = _subtitleExportSelector
            ?? throw new InvalidOperationException("Trình chọn nơi xuất SRT chưa được cấu hình.");
        var payload = request.Payload.Deserialize<ExportVietsubSrtRequest>(_jsonOptions)
            ?? throw new JsonException();
        var translated = payload.Mode?.Trim().ToUpperInvariant() switch
        {
            "ORIGINAL" => false,
            "TRANSLATED" => true,
            _ => throw new ArgumentException("Chế độ xuất SRT phải là ORIGINAL hoặc TRANSLATED.")
        };
        var destinationPath = selector();
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            Post(new WebMessageResponse(
                "vietsub.subtitle.selection.cancelled",
                requestId,
                new { cancelled = true }));
            return;
        }
        var fileName = await RequireSubtitleService().ExportSrtAsync(
            session.Manifest,
            destinationPath,
            translated,
            cancellationToken);
        Post(new WebMessageResponse(
            "vietsub.subtitle.export.completed",
            requestId,
            new { fileName, mode = translated ? "TRANSLATED" : "ORIGINAL" }));
    }

    private void PostSubtitleChanged(
        string requestId,
        bool resetPage,
        Guid? trackId = null,
        int? trackRevision = null) =>
        Post(new WebMessageResponse(
            "vietsub.subtitle.changed",
            requestId,
            new { resetPage, trackId, trackRevision }));

    private async Task PostJobStatusAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        await RequireOcrService().AuthorizeProjectAsync(
            session,
            context.UserId,
            context.OrganizationId,
            cancellationToken);
        var payload = request.Payload.Deserialize<VietsubJobRequest>(_jsonOptions)
            ?? throw new JsonException();
        if (payload.JobId == Guid.Empty)
        {
            throw new ArgumentException("Mã job Vietsub không hợp lệ.");
        }
        var job = await RequireJobManager().GetAsync(
            session.Manifest.ProjectId,
            payload.JobId,
            cancellationToken)
            ?? throw new VietsubJobException(
                "vietsub_job_not_found",
                "Không tìm thấy job trong dự án Vietsub hiện tại.");
        Post(new WebMessageResponse("vietsub.job.status", requestId, job));
    }

    private async Task ExportVideoAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var selector = _videoExportSelector
            ?? throw new InvalidOperationException("Trình chọn nơi xuất video chưa được cấu hình.");
        var destinationPath = selector();
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            Post(new WebMessageResponse(
                "vietsub.video.export.cancelled",
                requestId,
                new { cancelled = true }));
            return;
        }
        Post(new WebMessageResponse(
            "vietsub.video.export.started",
            requestId,
            new { started = true }));
        var result = await RequireVideoExportService().ExportAsync(
            session,
            context.UserId,
            context.OrganizationId,
            destinationPath,
            cancellationToken,
            new Progress<VietsubVideoExportProgress>(update =>
            {
                var current = _contextProvider?.Invoke();
                if (!_disposed && ReferenceEquals(session, _projectSession)
                    && current?.OrganizationId == context.OrganizationId
                    && string.Equals(current?.UserId, context.UserId, StringComparison.Ordinal)
                    && string.Equals(_activeOperationRequestId, requestId, StringComparison.Ordinal))
                    Post(new WebMessageResponse("vietsub.video.export.progress", requestId, update));
            }));
        Post(new WebMessageResponse(
            "vietsub.video.export.completed",
            requestId,
            new
            {
                result.FileName,
                result.SizeBytes,
                result.DurationSeconds
            }));
    }

    private async Task PostOcrRuntimeStatusAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        var status = await RequireOcrService().GetRuntimeStatusAsync(cancellationToken);
        Post(new WebMessageResponse("vietsub.ocr.runtime.status", requestId, status));
    }

    private void PostTranslationRuntimeStatus(string requestId)
    {
        var status = RequireTranslationService().GetRuntimeStatus();
        Post(new WebMessageResponse("vietsub.translation.runtime.status", requestId, status));
    }

    private async Task InstallTranslationRuntimeAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var input = request.Payload.Deserialize<VietsubInstallTranslationRuntimeInput>(_jsonOptions)
            ?? new VietsubInstallTranslationRuntimeInput();
        var progress = new Progress<VietsubTranslationRuntimeInstallProgress>(update =>
            Post(new WebMessageResponse(
                "vietsub.translation.runtime.install.progress",
                requestId,
                update)));
        try
        {
            var status = await RequireTranslationService().InstallRuntimeAsync(
                session,
                context.UserId,
                context.OrganizationId,
                input,
                progress,
                cancellationToken);
            Post(new WebMessageResponse("vietsub.translation.runtime.status", requestId, status));
        }
        catch (VietsubTranslationException)
        {
            PostTranslationRuntimeStatus(requestId);
            throw;
        }
    }

    private void PostVoiceRuntimeStatus(string requestId)
    {
        var status = RequireVoiceService().GetRuntimeStatus();
        Post(new WebMessageResponse("vietsub.voice.runtime.status", requestId, status));
    }

    private async Task PostVoiceModelStatusesAsync(string requestId, CancellationToken token)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var models = await RequireVoiceService().GetModelStatusesAsync(
            session, context.UserId, context.OrganizationId, token);
        Post(new WebMessageResponse("vietsub.voice.models.status", requestId,
            new { projectId = session.Manifest.ProjectId, models }));
    }

    private sealed class VoiceModelBridgeProgress(Action<VietsubVoiceModelInstallProgress> report)
        : IProgress<VietsubVoiceModelInstallProgress>
    {
        public void Report(VietsubVoiceModelInstallProgress value) => report(value);
    }

    private async Task InstallVoiceModelAsync(
        WebMessageRequest request, string requestId, CancellationToken token)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var input = request.Payload.Deserialize<VietsubInstallVoiceModelRequest>(_jsonOptions)
            ?? throw new JsonException("Thiếu mã giọng local cần cài.");
        if (input.ExpectedProjectId == Guid.Empty || input.ExpectedProjectId != session.Manifest.ProjectId
            || string.IsNullOrWhiteSpace(input.VoiceId) || input.VoiceId.Length > 80)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.ModelNotApproved,
                "Dự án hoặc giọng local cần cài không hợp lệ.");

        var progress = new VoiceModelBridgeProgress(update =>
            Post(new WebMessageResponse("vietsub.voice.model.install.progress", requestId,
                new { projectId = session.Manifest.ProjectId, update.VoiceId, update.Stage,
                    update.Percent, update.Message, update.BytesProcessed, update.TotalBytes })));
        try
        {
            await RequireVoiceService().InstallModelAsync(
                session, context.UserId, context.OrganizationId, input.VoiceId, progress, token);
            await PostVoiceModelStatusesAsync(requestId, token);
        }
        catch (VietsubVoiceException)
        {
            await PostVoiceModelStatusesAsync(requestId, CancellationToken.None);
            throw;
        }
    }

    private async Task SelectVoiceAsync(WebMessageRequest request, string requestId, CancellationToken token)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var input = request.Payload.Deserialize<VietsubSelectVoiceRequest>(_jsonOptions)
            ?? throw new JsonException("Thiếu giọng local cần chọn.");
        if (input.ExpectedProjectId != session.Manifest.ProjectId || input.ExpectedProjectId == Guid.Empty
            || string.IsNullOrWhiteSpace(input.VoiceId) || input.VoiceId.Length > 80)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.ModelNotApproved,
                "Dự án hoặc giọng local cần chọn không hợp lệ.");
        await RequireVoiceService().SelectVoiceAsync(session, context.UserId,
            context.OrganizationId, input.VoiceId, token);
        await PostStateAsync(requestId, token);
    }

    private async Task InstallVoiceRuntimeAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var progress = new Progress<VietsubVoiceRuntimeInstallProgress>(update =>
            Post(new WebMessageResponse("vietsub.voice.runtime.install.progress", requestId, update)));
        try
        {
            var status = await RequireVoiceService().InstallRuntimeAsync(
                session,
                context.UserId,
                context.OrganizationId,
                progress,
                cancellationToken);
            Post(new WebMessageResponse("vietsub.voice.runtime.status", requestId, status));
        }
        catch (VietsubVoiceException)
        {
            PostVoiceRuntimeStatus(requestId);
            throw;
        }
    }

    private async Task UpdateOcrSettingsAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var input = request.Payload.Deserialize<VietsubOcrSettingsInput>(_jsonOptions)
            ?? throw new JsonException();
        var settings = await RequireOcrService().UpdateSettingsAsync(
            session,
            context.UserId,
            context.OrganizationId,
            input,
            cancellationToken);
        Post(new WebMessageResponse("vietsub.ocr.settings", requestId, settings));
    }

    private async Task PreviewOcrAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var payload = request.Payload.Deserialize<VietsubOcrPreviewRequest>(_jsonOptions)
            ?? throw new JsonException();
        var result = await RequireOcrService().PreviewAsync(
            session,
            context.UserId,
            context.OrganizationId,
            new VietsubOcrSettingsInput(payload.LanguageCode, payload.Profile, payload.Region),
            payload.TimestampMilliseconds,
            cancellationToken);
        Post(new WebMessageResponse("vietsub.ocr.preview", requestId, result));
    }

    private async Task StartOcrAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var userContext = RequireContext();
        var input = request.Payload.Deserialize<VietsubOcrSettingsInput>(_jsonOptions)
            ?? throw new JsonException();
        var job = await RequireOcrService().StartAsync(
            session,
            userContext.UserId,
            userContext.OrganizationId,
            input,
            cancellationToken);
        Post(new WebMessageResponse("vietsub.job.changed", requestId, job));
    }

    private async Task StartTranslationAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var userContext = RequireContext();
        var input = request.Payload.Deserialize<VietsubStartTranslationInput>(_jsonOptions)
            ?? throw new JsonException();
        var job = await RequireTranslationService().StartAsync(
            session,
            userContext.UserId,
            userContext.OrganizationId,
            input,
            cancellationToken);
        Post(new WebMessageResponse("vietsub.job.changed", requestId, job));
    }

    private async Task StartCloudTranslationAsync(WebMessageRequest request, string requestId, CancellationToken ct)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var input = request.Payload.Deserialize<VietsubStartCloudTranslationRequest>(_jsonOptions) ?? throw new JsonException();
        var service = _cloudTranslationService ?? throw new VietsubTranslationException("CLOUD_DISABLED", "Dịch Cloud chưa được bật.");
        var job = await service.StartAsync(session, context.UserId, context.OrganizationId,
            new VietsubStartTranslationInput("CONTINUE", input.ExpectedTrackId, input.ExpectedTrackRevision), ct);
        if (job is null)
            Post(new WebMessageResponse("vietsub.cloud.nothingToTranslate", requestId, new { projectId = session.Manifest.ProjectId }));
        else Post(new WebMessageResponse("vietsub.job.changed", requestId, job));
    }

    private sealed record UpdateCueVoiceSelectionRequest(Guid ExpectedTrackId, int ExpectedTrackRevision,
        IReadOnlyList<Guid> CueIds, bool? VoiceEnabled);

    private async Task UpdateCueVoiceSelectionAsync(WebMessageRequest request, string requestId, CancellationToken token)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        var input = request.Payload.Deserialize<UpdateCueVoiceSelectionRequest>(_jsonOptions) ?? throw new JsonException();
        var revision = await RequireVoiceService().SetCueVoiceEnabledAsync(session, context.UserId,
            context.OrganizationId, input.ExpectedTrackId, input.ExpectedTrackRevision,
            input.CueIds, input.VoiceEnabled ?? throw new JsonException("Thiếu lựa chọn tạo giọng."), token);
        PostSubtitleChanged(requestId, resetPage: false, trackId: input.ExpectedTrackId, trackRevision: revision);
    }

    private async Task StartVoiceAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var userContext = RequireContext();
        var input = request.Payload.Deserialize<VietsubStartVoiceInput>(_jsonOptions)
            ?? throw new JsonException();
        var job = await RequireVoiceService().StartAsync(
            session,
            userContext.UserId,
            userContext.OrganizationId,
            input,
            cancellationToken);
        Post(new WebMessageResponse("vietsub.job.changed", requestId, job));
    }

    private async Task ActivateCompletedOcrTrackAsync(
        WebMessageRequest request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        await RequireOcrService().AuthorizeProjectAsync(
            session,
            context.UserId,
            context.OrganizationId,
            cancellationToken);
        var payload = request.Payload.Deserialize<ActivateVietsubOcrTrackRequest>(_jsonOptions)
            ?? throw new JsonException();
        var job = await RequireJobManager().GetAsync(
            session.Manifest.ProjectId,
            payload.JobId,
            cancellationToken)
            ?? throw new VietsubJobException(
                "vietsub_job_not_found",
                "Không tìm thấy OCR job trong dự án hiện tại.");
        if (job.Type != VietsubJobTypes.OcrLocal
            || job.Status != VietsubJobStatusNames.Completed
            || job.OutputTrackId is not Guid outputTrackId)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.JobNotResumable,
                "OCR job chưa hoàn thành hoặc không có track đầu ra.");
        }
        var impact = await RequireSubtitleService().AssessActivationImpactAsync(
            session.Manifest,
            outputTrackId,
            cancellationToken);
        if (impact.RequiresConfirmation && !payload.ConfirmImpact)
        {
            Post(new WebMessageResponse(
                "vietsub.ocr.activation.required",
                requestId,
                new { jobId = job.Id, outputTrackId, impact.Reasons }));
            return;
        }
        await ActivateOcrTrackCoreAsync(session, job.Id, outputTrackId, requestId, cancellationToken);
    }

    private async Task ControlJobAsync(
        WebMessageRequest request,
        string requestId,
        string action,
        CancellationToken cancellationToken)
    {
        var session = RequireProjectSession();
        var context = RequireContext();
        await RequireOcrService().AuthorizeProjectAsync(
            session,
            context.UserId,
            context.OrganizationId,
            cancellationToken);
        var payload = request.Payload.Deserialize<VietsubJobRequest>(_jsonOptions)
            ?? throw new JsonException();
        if (payload.JobId == Guid.Empty)
        {
            throw new ArgumentException("Mã job Vietsub không hợp lệ.");
        }

        var manager = RequireJobManager();
        var projectId = session.Manifest.ProjectId;
        if (_cloudTranslationService is not null)
            await _cloudTranslationService.ControlRemoteAsync(projectId, payload.JobId, action, cancellationToken);
        var job = action switch
        {
            "PAUSE" => await manager.PauseAsync(projectId, payload.JobId, cancellationToken),
            "RESUME" => await manager.ResumeAsync(projectId, payload.JobId, cancellationToken),
            "RETRY" => await manager.RetryAsync(projectId, payload.JobId, cancellationToken),
            "CANCEL" => await manager.CancelAsync(projectId, payload.JobId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        Post(new WebMessageResponse("vietsub.job.changed", requestId, job));
        await PostStateAsync(requestId, cancellationToken);
    }

    private void JobManagerOnChanged(object? sender, VietsubJobChangedEventArgs eventArgs)
    {
        if (_disposed || _projectSession?.Manifest.ProjectId != eventArgs.Job.ProjectId)
        {
            return;
        }

        if (eventArgs.Job.Type == VietsubJobTypes.OcrLocal
            && eventArgs.Job.Status is VietsubJobStatusNames.Completed
                or VietsubJobStatusNames.Failed
                or VietsubJobStatusNames.Cancelled)
        {
            _ = CompleteOcrJobOnceAsync(eventArgs.Job);
        }
        if (eventArgs.Job.Type is VietsubJobTypes.TranslateLocal or VietsubJobTypes.TranslateCloud
            && eventArgs.Job.Status is VietsubJobStatusNames.Completed
                or VietsubJobStatusNames.Failed
                or VietsubJobStatusNames.Cancelled)
        {
            _ = CompleteTranslationJobOnceAsync(eventArgs.Job);
        }
        if (eventArgs.Job.Type == VietsubJobTypes.SynthesizeVoiceLocal
            && eventArgs.Job.Status is VietsubJobStatusNames.Completed
                or VietsubJobStatusNames.Failed
                or VietsubJobStatusNames.Cancelled)
        {
            _ = CompleteVoiceJobOnceAsync(eventArgs.Job);
        }

        TryPostJobNotification(
            new WebMessageResponse("vietsub.job.changed", null, eventArgs.Job),
            eventArgs.Job.ProjectId,
            eventArgs.Job.Id,
            "JOB_NOTIFICATION_FAILED");
        if (eventArgs.Job.Type == VietsubJobTypes.TranslateLocal
            && _translationService?.GetExecutionState(eventArgs.Job.Id) is { } execution
            && _projectSession is { } currentSession)
        {
            TryPostJobNotification(new WebMessageResponse("vietsub.translation.execution", null,
                new VietsubTranslationExecutionStatus(eventArgs.Job.ProjectId, currentSession.Manifest.OrganizationId,
                    eventArgs.Job.Id, execution.Backend, execution.DeviceName,
                    execution.CpuFallback ? VietsubTranslationGpuPlanner.FallbackMessage(execution.FallbackCode) : null,
                    execution.CpuFallback, execution.FallbackCode)),
                eventArgs.Job.ProjectId, eventArgs.Job.Id, "TRANSLATION_EXECUTION_NOTIFICATION_FAILED");
        }
    }

    private async Task CompleteTranslationJobOnceAsync(VietsubJobSummary job)
    {
        if (!_translationCompletionHandled.TryAdd(job.Id, 0))
        {
            return;
        }

        try
        {
            var session = _projectSession;
            if (_disposed || session?.Manifest.ProjectId != job.ProjectId)
            {
                return;
            }

            var projectStatus = job.Status switch
            {
                VietsubJobStatusNames.Completed => VietsubProjectStatuses.Completed,
                VietsubJobStatusNames.Failed => VietsubProjectStatuses.Failed,
                _ => VietsubProjectStatuses.Ready
            };
            if (session.Manifest.Status != projectStatus)
            {
                await session.UpdateAsync(
                    manifest => manifest.Status = projectStatus,
                    CancellationToken.None);
                await session.FlushAsync(CancellationToken.None);
            }

            TryPostJobNotification(
                new WebMessageResponse(
                    "vietsub.subtitle.changed",
                    null,
                    new
                    {
                        resetPage = false,
                        trackId = job.OutputTrackId,
                        trackRevision = (int?)null
                    }),
                job.ProjectId,
                job.Id,
                "TRANSLATION_SUBTITLE_NOTIFICATION_FAILED");
            if (job.Status == VietsubJobStatusNames.Completed)
            {
                TryPostJobNotification(
                    new WebMessageResponse(
                        "vietsub.translation.completed",
                        null,
                        new { jobId = job.Id, trackId = job.OutputTrackId }),
                    job.ProjectId,
                    job.Id,
                    "TRANSLATION_COMPLETED_NOTIFICATION_FAILED");
            }
            await PostStateAsync(job.Id.ToString("N"), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _jobManager?.RecordDiagnostic(
                job.ProjectId,
                job.Id,
                "TRANSLATION_COMPLETION_FAILED",
                $"Không thể hoàn tất trạng thái dịch local ({exception.GetType().Name}).");
            TryPostJobNotification(
                new WebMessageResponse(
                    "vietsub.error",
                    null,
                    Error: new WebMessageError(
                        "vietsub_translation_completion_update_failed",
                        "Tác vụ dịch đã kết thúc nhưng chưa thể làm mới phụ đề.")),
                job.ProjectId,
                job.Id,
                "TRANSLATION_COMPLETION_NOTIFICATION_FAILED");
        }
    }

    private async Task CompleteVoiceJobOnceAsync(VietsubJobSummary job)
    {
        if (!_voiceCompletionHandled.TryAdd(job.Id, 0)) return;
        try
        {
            var session = _projectSession;
            if (_disposed || session?.Manifest.ProjectId != job.ProjectId) return;
            var status = job.Status switch
            {
                VietsubJobStatusNames.Completed => VietsubProjectStatuses.Completed,
                VietsubJobStatusNames.Failed => VietsubProjectStatuses.Failed,
                _ => VietsubProjectStatuses.Ready
            };
            if (session.Manifest.Status != status)
            {
                await session.UpdateAsync(manifest => manifest.Status = status, CancellationToken.None);
                await session.FlushAsync(CancellationToken.None);
            }
            if (job.Status == VietsubJobStatusNames.Completed)
            {
                TryPostJobNotification(
                    new WebMessageResponse(
                        "vietsub.voice.completed",
                        null,
                        new { jobId = job.Id, trackId = job.OutputTrackId }),
                    job.ProjectId,
                    job.Id,
                    "VOICE_COMPLETED_NOTIFICATION_FAILED");
            }
            await PostStateAsync(job.Id.ToString("N"), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _jobManager?.RecordDiagnostic(
                job.ProjectId,
                job.Id,
                "VOICE_COMPLETION_FAILED",
                $"Không thể làm mới trạng thái voice ({exception.GetType().Name}).");
        }
    }

    private async Task CompleteOcrJobOnceAsync(VietsubJobSummary job)
    {
        if (!_ocrCompletionInFlight.TryAdd(job.Id, 0))
        {
            return;
        }

        try
        {
            await HandleOcrJobFinishedCoreAsync(job);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _jobManager?.RecordDiagnostic(
                job.ProjectId,
                job.Id,
                "OCR_COMPLETION_FAILED",
                $"Không thể hoàn tất trạng thái OCR ({exception.GetType().Name}).");
            TryPostJobNotification(
                new WebMessageResponse(
                    "vietsub.error",
                    null,
                    Error: new WebMessageError(
                        "vietsub_ocr_completion_update_failed",
                        "OCR đã xong nhưng chưa thể cập nhật track đang dùng.")),
                job.ProjectId,
                job.Id,
                "OCR_COMPLETION_NOTIFICATION_FAILED");
        }
        finally
        {
            _ocrCompletionInFlight.TryRemove(job.Id, out _);
        }
    }

    private async Task HandleOcrJobFinishedCoreAsync(VietsubJobSummary job)
    {
        var session = _projectSession;
        if (_disposed || session?.Manifest.ProjectId != job.ProjectId)
        {
            return;
        }
        if (job.Status != VietsubJobStatusNames.Completed || job.OutputTrackId is not Guid outputTrackId)
        {
            var nextStatus = job.Status == VietsubJobStatusNames.Failed
                ? VietsubProjectStatuses.Failed
                : VietsubProjectStatuses.Ready;
            if (session.Manifest.Status == nextStatus)
            {
                return;
            }
            await session.UpdateAsync(
                manifest => manifest.Status = nextStatus,
                CancellationToken.None);
            await session.FlushAsync(CancellationToken.None);
            return;
        }

        if (session.Manifest.Status == VietsubProjectStatuses.Completed
            && session.Manifest.ActiveSubtitleTrackId == outputTrackId)
        {
            return;
        }

        var subtitleService = RequireSubtitleService();
        var impact = await subtitleService.AssessActivationImpactAsync(
            session.Manifest,
            outputTrackId,
            CancellationToken.None);
        if (impact.RequiresConfirmation)
        {
            if (session.Manifest.Status != VietsubProjectStatuses.Ready)
            {
                await session.UpdateAsync(
                    manifest => manifest.Status = VietsubProjectStatuses.Ready,
                    CancellationToken.None);
                await session.FlushAsync(CancellationToken.None);
            }
            TryPostJobNotification(
                new WebMessageResponse(
                    "vietsub.ocr.activation.required",
                    null,
                    new { jobId = job.Id, outputTrackId, impact.Reasons }),
                job.ProjectId,
                job.Id,
                "OCR_ACTIVATION_NOTIFICATION_FAILED");
            return;
        }
        await ActivateOcrTrackCoreAsync(
            session,
            job.Id,
            outputTrackId,
            requestId: null,
            CancellationToken.None);
    }

    private async Task ActivateOcrTrackCoreAsync(
        VietsubProjectSession session,
        Guid jobId,
        Guid outputTrackId,
        string? requestId,
        CancellationToken cancellationToken)
    {
        await RequireSubtitleService().ActivateTrackAsync(
            session.Manifest,
            outputTrackId,
            cancellationToken);
        await session.UpdateAsync(
            manifest =>
            {
                manifest.ActiveSubtitleTrackId = outputTrackId;
                manifest.Status = VietsubProjectStatuses.Completed;
            },
            cancellationToken);
        await session.FlushAsync(cancellationToken);
        TryPostJobNotification(
            new WebMessageResponse(
                "vietsub.ocr.completed",
                requestId,
                new { jobId, outputTrackId, activated = true }),
            session.Manifest.ProjectId,
            jobId,
            "OCR_COMPLETED_NOTIFICATION_FAILED");
        TryPostJobNotification(
            new WebMessageResponse(
                "vietsub.subtitle.changed",
                requestId ?? string.Empty,
                new { resetPage = true, trackId = outputTrackId, trackRevision = (int?)null }),
            session.Manifest.ProjectId,
            jobId,
            "OCR_SUBTITLE_NOTIFICATION_FAILED");
    }

    private async Task SelectProjectAsync(
        VietsubProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        await CloseCurrentSessionAsync(cancellationToken);
        var session = new VietsubProjectSession(RequireProjectStore(), manifest);
        try
        {
            await session.StartAsync(cancellationToken);
            _projectSession = session;
            lock (_operationSync)
            {
                _timelineArtifactCancellation = new CancellationTokenSource();
            }
            if (_jobManager is not null)
            {
                await _jobManager.RestoreInterruptedJobsAsync(
                    manifest.ProjectId,
                    cancellationToken);
                await ReconcileOcrProjectStatusAsync(session, cancellationToken);
            }
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private async Task ReconcileOcrProjectStatusAsync(
        VietsubProjectSession session,
        CancellationToken cancellationToken)
    {
        if (_jobManager is null
            || session.Manifest.Status != VietsubProjectStatuses.Processing)
        {
            return;
        }

        var latestOcrJob = (await _jobManager.ListAsync(
                session.Manifest.ProjectId,
                cancellationToken: cancellationToken))
            .FirstOrDefault(job => job.Type == VietsubJobTypes.OcrLocal);
        if (latestOcrJob?.Status is VietsubJobStatusNames.Completed
            or VietsubJobStatusNames.Failed
            or VietsubJobStatusNames.Cancelled)
        {
            await CompleteOcrJobOnceAsync(latestOcrJob);
        }
    }

    private void TryPostJobNotification(
        WebMessageResponse response,
        Guid projectId,
        Guid jobId,
        string failureEventType)
    {
        try
        {
            Post(response);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _jobManager?.RecordDiagnostic(
                projectId,
                jobId,
                failureEventType,
                $"Không thể gửi notification OCR ({exception.GetType().Name}).");
        }
    }

    private async Task SynchronizeManifestAsync(
        VietsubProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        if (_registryClient is null)
        {
            manifest.ServerSynchronized = false;
            manifest.ServerSyncErrorCode = "vietsub_registry_not_configured";
            await RequireProjectStore().SaveAsync(manifest, cancellationToken);
            return;
        }

        try
        {
            TOOL_SHARED.Contracts.Vietsub.VietsubProjectResponse response;
            try
            {
                response = await _registryClient.RegisterAsync(manifest, cancellationToken);
            }
            catch (AccountClientException exception) when (
                exception.Code == "vietsub_project_id_conflict")
            {
                response = await _registryClient.RenameAsync(
                    manifest.ProjectId,
                    manifest.OrganizationId,
                    manifest.Name,
                    cancellationToken);
            }

            if (response.ProjectId != manifest.ProjectId
                || response.OrganizationId != manifest.OrganizationId
                || !string.Equals(response.CreatedByUserId, manifest.OwnerUserId, StringComparison.Ordinal)
                || response.IsArchived)
            {
                throw new InvalidDataException("Server trả về registry Vietsub không khớp workspace local.");
            }

            manifest.ServerSynchronized = true;
            manifest.ServerSyncErrorCode = null;
        }
        catch (AccountClientException exception) when (exception.StatusCode != 401)
        {
            manifest.ServerSynchronized = false;
            manifest.ServerSyncErrorCode = exception.Code;
        }
        catch (HttpRequestException)
        {
            manifest.ServerSynchronized = false;
            manifest.ServerSyncErrorCode = "vietsub_registry_unavailable";
        }

        await RequireProjectStore().SaveAsync(manifest, cancellationToken);
    }

    private async Task CloseCurrentSessionAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? artifactCancellation;
        lock (_operationSync)
        {
            artifactCancellation = _timelineArtifactCancellation;
            _timelineArtifactCancellation = null;
        }
        artifactCancellation?.Cancel();
        artifactCancellation?.Dispose();
        _thumbnailService?.CancelActive();
        var session = _projectSession;
        _projectSession = null;
        if (session is null)
        {
            return;
        }

        await session.CloseAsync(cancellationToken);
        await session.DisposeAsync();
    }

    private async Task CancelActiveOperationAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        string? cancelledRequestId;
        lock (_operationSync)
        {
            cancelledRequestId = _activeOperationRequestId;
            _activeOperation?.Cancel();
        }

        Post(new WebMessageResponse(
            "vietsub.operation.cancelled",
            requestId,
            new
            {
                cancelledRequestId,
                hadActiveOperation = cancelledRequestId is not null
            }));
        await PostStateAsync(requestId, cancellationToken);
    }

    private async Task PostStateAsync(string requestId, CancellationToken cancellationToken)
    {
        string? activeOperationRequestId;
        lock (_operationSync)
        {
            activeOperationRequestId = _activeOperationRequestId;
        }

        IReadOnlyList<VietsubProjectSummary> projects = [];
        VietsubProjectSummary? selectedProject = null;
        VietsubSubtitleWorkspaceSummary? subtitleWorkspace = null;
        VietsubVoiceWorkspaceSummary? voiceWorkspace = null;
        IReadOnlyList<VietsubJobSummary> jobs = [];
        var stateSession = _projectSession;
        if (_projectStore is not null && _contextProvider is not null)
        {
            var context = RequireContext();
            if (_projectSession is not null
                && (_projectSession.Manifest.OrganizationId != context.OrganizationId
                    || !string.Equals(
                        _projectSession.Manifest.OwnerUserId,
                        context.UserId,
                        StringComparison.Ordinal)))
            {
                await CloseCurrentSessionAsync(cancellationToken);
            }
            stateSession = _projectSession;
            var manifest = stateSession?.Manifest;
            projects = await _projectStore.ListAsync(
                context.OrganizationId,
                context.UserId,
                cancellationToken);
            selectedProject = manifest is null
                ? null
                : ToSelectedProjectSummary(manifest);
            if (manifest is not null && _subtitleService is not null)
            {
                subtitleWorkspace = await _subtitleService.GetWorkspaceAsync(
                    manifest,
                    cancellationToken);
            }
            if (manifest is not null && _voiceService is not null)
            {
                voiceWorkspace = await _voiceService.GetWorkspaceAsync(
                    manifest,
                    cancellationToken);
            }
            if (manifest is not null && _jobManager is not null)
            {
                jobs = await _jobManager.ListAsync(
                    manifest.ProjectId,
                    cancellationToken: cancellationToken);
            }
            // Background SQLite/hash work must never publish data into a newly selected context.
            var currentContext = _contextProvider.Invoke();
            if (_disposed || !ReferenceEquals(stateSession, _projectSession)
                || currentContext?.OrganizationId != context.OrganizationId
                || !string.Equals(currentContext?.UserId, context.UserId, StringComparison.Ordinal)) return;
        }

        Post(new WebMessageResponse(
            "vietsub.state",
            requestId,
            new
            {
                enabled = true,
                busy = activeOperationRequestId is not null,
                activeOperationRequestId,
                stage = _projectStore is null ? "shell_ready" : "workspace_ready",
                projects,
                selectedProject,
                subtitleWorkspace,
                subtitleStyle = _projectSession?.Manifest.SubtitleStyle,
                audioMixSettings = _projectSession?.Manifest.AudioMixSettings,
                videoTransformSettings = _projectSession?.Manifest.VideoTransformSettings,
                voiceWorkspace,
                ocrSettings = _projectSession?.Manifest.OcrSettings,
                voiceRuntime = _voiceService?.GetRuntimeStatus(),
                jobs,
                activeJob = jobs.FirstOrDefault(job => job.Status is
                    VietsubJobStatusNames.Pending or
                    VietsubJobStatusNames.Running or
                    VietsubJobStatusNames.Pausing or
                    VietsubJobStatusNames.Paused or
                    VietsubJobStatusNames.Interrupted or
                    VietsubJobStatusNames.Failed)
            }));
    }

    private VietsubProjectSummary ToSelectedProjectSummary(VietsubProjectManifest manifest)
    {
        var summary = VietsubProjectStore.ToSummary(manifest) with
        { TranslationExecutionPolicy = manifest.TranslationSettings.ExecutionPolicy };
        if (manifest.SourceVideo is null || _mediaImportService is null)
        {
            return summary;
        }

        var media = manifest.SourceVideo;
        var sourceStatus = _mediaImportService.GetSourceStatus(manifest.ProjectId, media);
        var playbackUrl = sourceStatus.Available && !sourceStatus.Changed
            ? VietsubMediaPlaybackService.CreatePlaybackUrl(manifest.ProjectId, media.MediaId)
            : string.Empty;
        var artifactsCanBeServed = sourceStatus.Available
            && !sourceStatus.Changed
            && !string.IsNullOrWhiteSpace(sourceStatus.EffectivePath);
        var waveform = artifactsCanBeServed
            ? _waveformService?.GetExistingArtifact(manifest)
            : null;
        waveform ??= new VietsubTimelineWaveformArtifact(
                media.Metadata.HasAudio
                    ? VietsubWaveformStatuses.Pending
                    : VietsubWaveformStatuses.NoAudio,
                null);
        if (waveform.Status == VietsubWaveformStatuses.Pending
            && _waveformFailures.ContainsKey(GetWaveformFailureKey(manifest)))
        {
            waveform = new(VietsubWaveformStatuses.Failed, null);
        }
        return summary with
        {
            SourceVideo = new VietsubMediaSummary(
                media.MediaId,
                media.FileName,
                media.ImportMode,
                media.SizeBytes,
                media.Sha256,
                media.Metadata.DurationSeconds,
                media.Metadata.Width,
                media.Metadata.Height,
                media.Metadata.FramesPerSecond,
                media.Metadata.VideoCodec,
                media.Metadata.AudioCodec,
                media.Metadata.HasAudio,
                sourceStatus.Available,
                sourceStatus.Changed,
                sourceStatus.IssueCode,
                playbackUrl,
                artifactsCanBeServed ? _thumbnailService?.GetExistingUrls(manifest) ?? [] : [],
                artifactsCanBeServed ? _thumbnailService?.GetExistingTimelineThumbnails(manifest) ?? [] : [],
                waveform.Url,
                waveform.Status,
                media.Metadata.RotationDegrees,
                VietsubTimelineThumbnailService.ProfileVersion,
                VietsubTimelineThumbnailService.ThumbnailCount,
                VietsubTimelineWaveformService.ProfileVersion,
                waveform.Revision)
        };
    }

    private static string GetWaveformFailureKey(VietsubProjectManifest project) =>
        project.SourceVideo is { } media
            ? $"{project.ProjectId:N}:{media.Sha256}"
            : project.ProjectId.ToString("N");

    public VietsubPlaybackResponse TryOpenPlaybackRequest(
        Uri requestUri,
        string method,
        string? rangeHeader)
    {
        if (!_enabled)
        {
            return VietsubMediaPlaybackService.Error(
                403,
                "Forbidden",
                "vietsub_feature_disabled",
                VietsubMediaPlaybackService.ClassifyResource(requestUri));
        }
        if (_disposed || _mediaPlaybackService is null)
        {
            return VietsubMediaPlaybackService.Error(
                503,
                "Service Unavailable",
                "vietsub_media_bridge_unavailable",
                VietsubMediaPlaybackService.ClassifyResource(requestUri));
        }
        if (_projectSession?.Manifest is not { } manifest)
        {
            return VietsubMediaPlaybackService.Error(
                403,
                "Forbidden",
                "vietsub_media_project_session_required",
                VietsubMediaPlaybackService.ClassifyResource(requestUri));
        }

        var context = _contextProvider?.Invoke();
        if (context is null
            || context.OrganizationId != manifest.OrganizationId
            || !string.Equals(context.UserId, manifest.OwnerUserId, StringComparison.Ordinal))
        {
            return VietsubMediaPlaybackService.Error(
                403,
                "Forbidden",
                "vietsub_media_session_context_mismatch",
                VietsubMediaPlaybackService.ClassifyResource(requestUri));
        }

        return _mediaPlaybackService.Open(requestUri, method, rangeHeader, manifest);
    }

    public async Task<VietsubPlaybackResponse> OpenPlaybackRequestAsync(
        Uri requestUri, string method, string? rangeHeader, CancellationToken cancellationToken)
    {
        // Capture UI-owned context before dispatching disk/hash work. Never invoke the
        // context provider or access WebView COM objects from the background thread.
        var session = _projectSession;
        var manifest = session?.Manifest;
        var service = _mediaPlaybackService;
        var context = _contextProvider?.Invoke();
        if (!_enabled || _disposed || service is null || manifest is null || context is null
            || context.OrganizationId != manifest.OrganizationId
            || !string.Equals(context.UserId, manifest.OwnerUserId, StringComparison.Ordinal))
        {
            var unavailable = _disposed || service is null;
            var code = !_enabled ? "vietsub_feature_disabled" : unavailable ? "vietsub_media_bridge_unavailable"
                : manifest is null ? "vietsub_media_project_session_required" : "vietsub_media_session_context_mismatch";
            return VietsubMediaPlaybackService.Error(unavailable && _enabled ? 503 : 403,
                unavailable && _enabled ? "Service Unavailable" : "Forbidden", code,
                VietsubMediaPlaybackService.ClassifyResource(requestUri));
        }

        var snapshot = JsonSerializer.Deserialize<VietsubProjectManifest>(JsonSerializer.SerializeToUtf8Bytes(manifest))!;
        var response = await Task.Run(() => service.Open(requestUri, method, rangeHeader, snapshot, cancellationToken), cancellationToken);
        var currentContext = _contextProvider?.Invoke();
        if (cancellationToken.IsCancellationRequested || _disposed || !ReferenceEquals(session, _projectSession)
            || !ReferenceEquals(manifest, _projectSession?.Manifest)
            || manifest.ActiveSubtitleTrackId != snapshot.ActiveSubtitleTrackId
            || manifest.UpdatedAtUtc != snapshot.UpdatedAtUtc
            || manifest.SourceVideo?.MediaId != snapshot.SourceVideo?.MediaId
            || manifest.SourceVideo?.Sha256 != snapshot.SourceVideo?.Sha256
            || currentContext?.OrganizationId != snapshot.OrganizationId
            || !string.Equals(currentContext?.UserId, snapshot.OwnerUserId, StringComparison.Ordinal))
        {
            response.Content.Dispose();
            return VietsubMediaPlaybackService.Error(403, "Forbidden", "vietsub_media_session_context_mismatch",
                VietsubMediaPlaybackService.ClassifyResource(requestUri));
        }
        return response;
    }

    private VietsubProjectStore RequireProjectStore() =>
        _projectStore ?? throw new InvalidOperationException("Kho dự án Vietsub chưa được cấu hình.");

    private VietsubProjectSession RequireProjectSession() =>
        _projectSession ?? throw new InvalidOperationException("Hãy mở một dự án Vietsub trước.");

    private VietsubSubtitleService RequireSubtitleService() =>
        _subtitleService ?? throw new InvalidOperationException("Dịch vụ phụ đề Vietsub chưa được cấu hình.");

    private VietsubJobManager RequireJobManager() =>
        _jobManager ?? throw new InvalidOperationException("Job engine Vietsub chưa được cấu hình.");

    private VietsubOcrService RequireOcrService() =>
        _ocrService ?? throw new InvalidOperationException("Dịch vụ OCR Vietsub chưa được cấu hình.");

    private VietsubTranslationService RequireTranslationService() =>
        _translationService
        ?? throw new InvalidOperationException("Dịch vụ dịch local Vietsub chưa được cấu hình.");

    private VietsubVoiceService RequireVoiceService() =>
        _voiceService
        ?? throw new InvalidOperationException("Dịch vụ giọng local Vietsub chưa được cấu hình.");

    private VietsubVideoExportService RequireVideoExportService() =>
        _videoExportService
        ?? throw new InvalidOperationException("Dịch vụ xuất video Vietsub chưa được cấu hình.");

    private VietsubUserContext RequireContext() =>
        _contextProvider?.Invoke() is { } context
            && context.OrganizationId != Guid.Empty
            && !string.IsNullOrWhiteSpace(context.UserId)
                ? context
                : throw new InvalidOperationException("Hãy chọn tổ chức trước khi dùng Vietsub.");

    private void EnsureCurrentProjectContext(VietsubProjectManifest manifest)
    {
        var context = RequireContext();
        if (manifest.OrganizationId != context.OrganizationId
            || !string.Equals(manifest.OwnerUserId, context.UserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Dự án Vietsub không thuộc phiên làm việc hiện tại.");
        }
    }

    private void PostError(string? requestId, string code, string message) =>
        Post(new WebMessageResponse(
            "vietsub.error",
            requestId,
            Error: new WebMessageError(code, message)));

    private void Post(WebMessageResponse response)
    {
        if (_disposed)
        {
            return;
        }

        _postJson(JsonSerializer.Serialize(response, _jsonOptions));
    }

    public void Dispose()
    {
        VietsubProjectSession? session;
        lock (_operationSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeOperation?.Cancel();
            _activeOperation?.Dispose();
            _activeOperation = null;
            _activeOperationRequestId = null;
            session = _projectSession;
            _projectSession = null;
        }

        if (_jobManager is not null)
        {
            _jobManager.JobChanged -= JobManagerOnChanged;
        }
        if (_thumbnailService is not null)
        {
            _thumbnailService.ThumbnailReady -= ThumbnailServiceOnReady;
            _thumbnailService.ThumbnailFailed -= ThumbnailServiceOnFailed;
            _thumbnailService.CancelActive();
        }
        _timelineArtifactCancellation?.Cancel();
        _timelineArtifactCancellation?.Dispose();
        _timelineArtifactCancellation = null;

        if (session is not null)
        {
            Task.Run(async () => await session.DisposeAsync().ConfigureAwait(false))
                .GetAwaiter()
                .GetResult();
        }
    }
}
