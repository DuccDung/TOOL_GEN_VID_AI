using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Playback;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
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
    string Speaker);

internal sealed record VietsubSubtitleCueTimelineRequest(Guid CueId, long PositionMilliseconds);

internal sealed record VietsubSubtitleCueRequest(Guid CueId);

internal sealed record ExportVietsubSrtRequest(string Mode);

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
    private readonly VietsubSubtitleService? _subtitleService;
    private readonly Func<string?>? _subtitleFileSelector;
    private readonly Func<string?>? _subtitleExportSelector;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly object _operationSync = new();
    private CancellationTokenSource? _activeOperation;
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
        Func<string?>? subtitleExportSelector = null)
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
                        cancellationToken);
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
                case "vietsub.subtitle.cue.update":
                    await RunProjectOperationAsync(
                        request.RequestId,
                        token => UpdateSubtitleCueAsync(request, request.RequestId, token),
                        cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var operationToken = BeginOperation(requestId, cancellationToken);
        await PostStateAsync(requestId, cancellationToken);
        try
        {
            await operation(operationToken);
        }
        finally
        {
            CompleteOperation(requestId);
            await PostStateAsync(requestId, cancellationToken);
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
        if (_thumbnailService is not null)
        {
            var thumbnailProgress = new Progress<double>(percent =>
                Post(new WebMessageResponse(
                    "vietsub.thumbnail.progress",
                    requestId,
                    new { percent })));
            try
            {
                await _thumbnailService.EnsureAsync(
                    session.Manifest,
                    thumbnailProgress,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is VietsubMediaException
                    or TOOL_LOCAL.Media.MediaToolUnavailableException
                    or IOException
                    or UnauthorizedAccessException
                    or TimeoutException)
            {
                Post(new WebMessageResponse(
                    "vietsub.thumbnail.failed",
                    requestId,
                    new { message = "Video đã được nhập nhưng chưa thể tạo đủ ảnh timeline." }));
            }
        }
    }

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
        await RequireSubtitleService().UpdateCueAsync(
            session.Manifest,
            payload.CueId,
            payload.OriginalText,
            payload.TranslatedText,
            payload.Speaker,
            cancellationToken);
        PostSubtitleChanged(requestId, resetPage: false);
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

    private void PostSubtitleChanged(string requestId, bool resetPage) =>
        Post(new WebMessageResponse(
            "vietsub.subtitle.changed",
            requestId,
            new { resetPage }));

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
        }
        catch
        {
            await session.DisposeAsync();
            throw;
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

            projects = await _projectStore.ListAsync(
                context.OrganizationId,
                context.UserId,
                cancellationToken);
            selectedProject = _projectSession is null
                ? null
                : ToSelectedProjectSummary(_projectSession.Manifest);
            if (_projectSession is not null && _subtitleService is not null)
            {
                subtitleWorkspace = await _subtitleService.GetWorkspaceAsync(
                    _projectSession.Manifest,
                    cancellationToken);
            }
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
                subtitleWorkspace
            }));
    }

    private VietsubProjectSummary ToSelectedProjectSummary(VietsubProjectManifest manifest)
    {
        var summary = VietsubProjectStore.ToSummary(manifest);
        if (manifest.SourceVideo is null || _mediaImportService is null)
        {
            return summary;
        }

        var media = manifest.SourceVideo;
        var sourceStatus = _mediaImportService.GetSourceStatus(manifest.ProjectId, media);
        var playbackUrl = sourceStatus.Available && !sourceStatus.Changed
            ? VietsubMediaPlaybackService.CreatePlaybackUrl(manifest.ProjectId, media.MediaId)
            : string.Empty;
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
                _thumbnailService?.GetExistingUrls(manifest) ?? [])
        };
    }

    public VietsubPlaybackResponse? TryOpenPlaybackRequest(
        Uri requestUri,
        string method,
        string? rangeHeader)
    {
        if (!_enabled
            || _disposed
            || _mediaPlaybackService is null
            || _projectSession?.Manifest is not { } manifest)
        {
            return null;
        }

        var context = _contextProvider?.Invoke();
        if (context is null
            || context.OrganizationId != manifest.OrganizationId
            || !string.Equals(context.UserId, manifest.OwnerUserId, StringComparison.Ordinal))
        {
            return null;
        }

        return _mediaPlaybackService.Open(requestUri, method, rangeHeader, manifest);
    }

    private VietsubProjectStore RequireProjectStore() =>
        _projectStore ?? throw new InvalidOperationException("Kho dự án Vietsub chưa được cấu hình.");

    private VietsubProjectSession RequireProjectSession() =>
        _projectSession ?? throw new InvalidOperationException("Hãy mở một dự án Vietsub trước.");

    private VietsubSubtitleService RequireSubtitleService() =>
        _subtitleService ?? throw new InvalidOperationException("Dịch vụ phụ đề Vietsub chưa được cấu hình.");

    private VietsubUserContext RequireContext() =>
        _contextProvider?.Invoke() is { } context
            && context.OrganizationId != Guid.Empty
            && !string.IsNullOrWhiteSpace(context.UserId)
                ? context
                : throw new InvalidOperationException("Hãy chọn tổ chức trước khi dùng Vietsub.");

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

        if (session is not null)
        {
            Task.Run(async () => await session.DisposeAsync().ConfigureAwait(false))
                .GetAwaiter()
                .GetResult();
        }
    }
}
