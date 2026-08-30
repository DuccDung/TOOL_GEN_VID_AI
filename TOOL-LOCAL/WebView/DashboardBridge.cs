using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Generation;
using TOOL_SHARED.Contracts.Generation;
using TOOL_LOCAL.Providers;
using TOOL_LOCAL.Media;

namespace TOOL_LOCAL.WebView;

internal sealed class DashboardBridge : IDisposable
{
    private const int MaxMessageLength = 64 * 1024;
    private readonly AccountSessionManager _sessionManager;
    private readonly LicenseSessionManager _licenseManager;
    private readonly IProjectService _projectService;
    private readonly IProjectRenderService _projectRenderService;
    private readonly IProjectGenerationService _generationService;
    private readonly IGenerationClient _generationClient;
    private readonly IMediaToolPreflightService _mediaToolPreflight;
    private readonly Action<string> _postJson;
    private readonly Action _closeApplication;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly SemaphoreSlim _generationLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private Guid? _selectedProjectId;
    private bool _disposed;
    private volatile bool _generationRunning;

    public DashboardBridge(
        AccountSessionManager sessionManager,
        LicenseSessionManager licenseManager,
        IProjectService projectService,
        IProjectRenderService projectRenderService,
        IProjectGenerationService generationService,
        IGenerationClient generationClient,
        IMediaToolPreflightService mediaToolPreflight,
        Action<string> postJson,
        Action closeApplication)
    {
        _sessionManager = sessionManager;
        _licenseManager = licenseManager;
        _projectService = projectService;
        _projectRenderService = projectRenderService;
        _generationService = generationService;
        _generationClient = generationClient;
        _mediaToolPreflight = mediaToolPreflight;
        _postJson = postJson;
        _closeApplication = closeApplication;
    }

    public async Task HandleAsync(string json, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxMessageLength)
        {
            PostError(null, "invalid_message", "Yêu cầu từ giao diện không hợp lệ.");
            return;
        }

        WebMessageRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<WebMessageRequest>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            PostError(null, "invalid_json", "Yêu cầu từ giao diện không đúng định dạng.");
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Type))
        {
            PostError(request?.RequestId, "invalid_message", "Yêu cầu từ giao diện không hợp lệ.");
            return;
        }

        try
        {
            switch (request.Type)
            {
                case "app.ready":
                case "dashboard.refresh":
                    await RefreshAsync(request.RequestId, cancellationToken);
                    break;
                case "project.select":
                    await SelectProjectAsync(request, cancellationToken);
                    break;
                case "organization.select":
                    await SelectOrganizationAsync(request, cancellationToken);
                    break;
                case "project.create":
                    await CreateProjectAsync(request, cancellationToken);
                    break;
                case "generation.content":
                    await GenerateContentAsync(request.RequestId, cancellationToken);
                    break;
                case "generation.video":
                    await GenerateVideosAsync(request, cancellationToken);
                    break;
                case "render.final":
                    await RenderFinalVideoAsync(request.RequestId, cancellationToken);
                    break;
                case "scene.update":
                    await UpdateSceneAsync(request, cancellationToken);
                    break;
                case "scene.native-audio.approve":
                    await ApproveSceneNativeAudioAsync(request, cancellationToken);
                    break;
                case "character.update":
                    await UpdateCharacterAsync(request, cancellationToken);
                    break;
                case "character.reference.select":
                    await SelectCharacterReferenceAsync(request, cancellationToken);
                    break;
                case "character.reference.generate":
                    await GenerateCharacterReferenceAsync(request, cancellationToken);
                    break;
                case "character.approve":
                    await ApproveCharacterAsync(request, cancellationToken);
                    break;
                case "providers.settings.get":
                    await GetProviderSettingsAsync(request.RequestId, cancellationToken);
                    break;
                case "providers.settings.test":
                    await TestProviderAsync(request, cancellationToken);
                    break;
                case "media.tools.check":
                    await CheckMediaToolsAsync(request.RequestId, cancellationToken);
                    break;
                case "auth.logout":
                    await LogoutAsync(request.RequestId, cancellationToken);
                    break;
                default:
                    PostError(request.RequestId, "unsupported_operation", "Chức năng này chưa được hỗ trợ.");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The form is closing; no response should be posted to the disposed WebView.
        }
        catch (ArgumentException exception)
        {
            PostError(request.RequestId, "validation_failed", exception.Message);
        }
        catch (JsonException)
        {
            PostError(
                request.RequestId,
                "invalid_payload",
                request.Type == "scene.update"
                    ? "Dữ liệu chỉnh sửa cảnh không đúng định dạng. Nội dung bạn vừa nhập vẫn được giữ lại."
                    : "Dữ liệu thao tác không đúng định dạng.");
        }
        catch (DbUpdateConcurrencyException)
        {
            PostError(
                request.RequestId,
                "workflow_data_conflict",
                request.Type == "scene.update"
                    ? "Dữ liệu cảnh vừa được cập nhật ở tác vụ khác. Nội dung bạn vừa nhập vẫn được giữ; hãy bấm Lưu cảnh lại."
                    : "Dữ liệu vừa thay đổi ở tác vụ khác. Hãy làm mới và thử lại.");
        }
        catch (DbUpdateException exception)
        {
            WorkflowDatabaseErrorLog.Write(exception, request.Type);
            PostError(
                request.RequestId,
                "workflow_save_failed",
                request.Type == "scene.update"
                    ? "Không thể lưu cảnh vào database. Nội dung bạn vừa nhập vẫn được giữ; hãy làm mới và thử lại. Nếu lỗi lặp lại, cần kiểm tra migration database."
                    : "Không thể lưu dữ liệu workflow. Hãy thử lại; nếu lỗi lặp lại, cần kiểm tra migration database.");
        }
        catch (MediaToolUnavailableException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (AccountClientException exception) when (exception.StatusCode == 401)
        {
            await _sessionManager.InvalidateAsync(CancellationToken.None);
        }
        catch (AccountClientException exception)
        {
            PostError(request.RequestId, exception.Code, exception.Message);
        }
        catch (Exception)
        {
            PostError(request.RequestId, "operation_failed", "Không thể hoàn tất thao tác. Vui lòng thử lại.");
        }
    }

    public Task RefreshInBackgroundAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(null, cancellationToken);

    private async Task SelectProjectAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<SelectProjectWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Dự án được chọn không hợp lệ.");
        if (payload.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("Dự án được chọn không hợp lệ.");
        }

        _selectedProjectId = payload.ProjectId;
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task SelectOrganizationAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        if (_generationRunning)
        {
            throw new ArgumentException("Không thể đổi tổ chức khi tác vụ AI đang chạy.");
        }
        var payload = request.Payload.Deserialize<SelectOrganizationWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Tổ chức được chọn không hợp lệ.");
        await _generationClient.SelectOrganizationAsync(payload.OrganizationId, cancellationToken);
        _selectedProjectId = null;
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task CreateProjectAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<CreateProjectWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Thông tin tạo dự án không hợp lệ.");
        var topic = NormalizeTopic(payload.Topic);
        var aspectRatio = payload.AspectRatio is "16:9" or "9:16" or "1:1"
            ? payload.AspectRatio
            : throw new ArgumentException("Tỷ lệ khung hình không được hỗ trợ.");
        var languageCode = payload.LanguageCode is "vi-VN" or "en-US"
            ? payload.LanguageCode
            : throw new ArgumentException("Ngôn ngữ không được hỗ trợ.");
        var voiceCode = payload.VoiceCode?.Trim() switch
        {
            null or "" => null,
            "female-sweet" => "female-sweet",
            "male-warm" => "male-warm",
            _ => throw new ArgumentException("Giọng đọc được chọn không hợp lệ.")
        };
        var voiceSpeakingRate = payload.VoiceSpeakingRate;
        if (voiceSpeakingRate is { } rate && rate is < 0.5m or > 2m)
        {
            throw new ArgumentException("Tốc độ giọng đọc phải nằm trong khoảng 0,5–2,0.");
        }

        var current = _sessionManager.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
        var platform = aspectRatio switch
        {
            "9:16" => "YouTubeShorts",
            "1:1" => "InstagramReels",
            _ => "YouTube"
        };
        var project = await _projectService.CreateAsync(
            new CreateProjectCommand(
                CreateProjectName(topic),
                topic,
                platform,
                aspectRatio,
                75,
                null,
                languageCode,
                _generationClient.SelectedOrganizationId
                    ?? throw new ArgumentException("Hãy chọn tổ chức trước khi tạo dự án."),
                voiceCode,
                voiceSpeakingRate),
            current.User,
            current.DeviceId,
            cancellationToken);
        _selectedProjectId = project.ProjectId;
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã tạo dự án mới." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task LogoutAsync(string? requestId, CancellationToken cancellationToken)
    {
        await _sessionManager.LogoutAsync(cancellationToken: cancellationToken);
        Post(new WebMessageResponse("auth.loggedOut", requestId));
        _closeApplication();
    }

    private async Task GetProviderSettingsAsync(string? requestId, CancellationToken cancellationToken)
    {
        var settings = await _generationClient.GetSettingsAsync(cancellationToken);
        Post(new WebMessageResponse("providers.settings", requestId, settings));
    }

    private async Task TestProviderAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<TestProviderWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Provider cần kiểm tra không hợp lệ.");
        await _generationClient.TestProviderAsync(payload.ProviderCode.Trim().ToLowerInvariant(), cancellationToken);
        Post(new WebMessageResponse("operation.notice", request.RequestId, new { message = $"Kết nối {payload.ProviderCode} thành công." }));
    }

    private async Task CheckMediaToolsAsync(string? requestId, CancellationToken cancellationToken)
    {
        var status = await _mediaToolPreflight.GetStatusAsync(force: true, cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            requestId,
            new { message = status.Message }));
        await RefreshAsync(requestId, cancellationToken);
    }

    private Task GenerateContentAsync(string? requestId, CancellationToken cancellationToken) =>
        RunGenerationAsync(
            requestId,
            async (projectId, userId, token) =>
            {
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new { message = "OpenAI đang viết content plan và prompt cho từng cảnh..." }));
                var result = await _generationService.GenerateContentAsync(projectId, userId, token);
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new { message = $"Đã tạo nội dung với {result.Plan.Scenes.Count} cảnh bằng {result.ModelCode}." }));
            },
            cancellationToken);

    private Task GenerateVideosAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new GenerateVideoWebRequest(null)
            : request.Payload.Deserialize<GenerateVideoWebRequest>(_jsonOptions)
              ?? throw new ArgumentException("Danh sách cảnh cần tạo không hợp lệ.");
        var sceneIds = payload.SceneIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        if (payload.SceneIds is not null && (sceneIds is null || sceneIds.Length == 0))
        {
            throw new ArgumentException("Hãy chọn ít nhất một cảnh để tạo video.");
        }
        if (sceneIds?.Length > 100)
        {
            throw new ArgumentException("Mỗi lần chỉ được tạo tối đa 100 cảnh.");
        }

        return GenerateVideosCoreAsync(request.RequestId, sceneIds, cancellationToken);
    }

    private Task GenerateVideosCoreAsync(
        string? requestId,
        IReadOnlyCollection<Guid>? sceneIds,
        CancellationToken cancellationToken) =>
        RunGenerationAsync(
            requestId,
            async (projectId, userId, token) =>
            {
                var count = await _generationService.GenerateVideosAsync(
                    projectId,
                    userId,
                    sceneIds,
                    async (message, progressToken) =>
                    {
                        Post(new WebMessageResponse("operation.notice", requestId, new { message }));
                        await RefreshAsync(requestId, progressToken);
                    },
                    token);
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new { message = $"Đã tải {count} clip Native Audio vào workspace. Hãy nghe và duyệt từng cảnh trước khi dựng video cuối." }));
            },
            cancellationToken);

    private Task RenderFinalVideoAsync(string? requestId, CancellationToken cancellationToken) =>
        RunGenerationAsync(
            requestId,
            async (projectId, userId, token) =>
            {
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new { message = "Đang dựng video cuối từ các clip Native Audio đã duyệt..." }));
                var result = await _projectRenderService.RenderFinalVideoAsync(projectId, userId, token);
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new { message = $"Đã dựng xong video v{result.Version}; hình ảnh và Native Audio đều đạt kiểm tra." }));
            },
            cancellationToken);

    private async Task UpdateSceneAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        if (_generationRunning)
        {
            throw new ArgumentException("Không thể sửa cảnh khi tác vụ AI đang chạy.");
        }

        var payload = request.Payload.Deserialize<UpdateSceneWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Nội dung cảnh cần cập nhật không hợp lệ.");
        var current = _sessionManager.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
        var projectId = _selectedProjectId
            ?? throw new ArgumentException("Hãy chọn hoặc tạo một dự án trước.");
        await _projectService.UpdateSceneAsync(
            projectId,
            current.User.UserId,
            new UpdateSceneCommand(
                payload.SceneId,
                payload.Narration,
                payload.VisualDescription,
                payload.Prompt,
                payload.SpeechMode,
                payload.VoiceStyle,
                payload.AmbientAudio,
                payload.SoundEffects),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã lưu nội dung và prompt của cảnh." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task ApproveSceneNativeAudioAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (_generationRunning)
        {
            throw new ArgumentException("Không thể duyệt cảnh khi tác vụ AI đang chạy.");
        }

        var payload = request.Payload.Deserialize<SceneActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cảnh cần duyệt không hợp lệ.");
        var current = _sessionManager.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
        var projectId = _selectedProjectId
            ?? throw new ArgumentException("Hãy chọn dự án trước khi duyệt cảnh.");
        await _projectService.ApproveSceneNativeAudioAsync(
            projectId,
            current.User.UserId,
            payload.SceneId,
            payload.PlaybackConfirmed,
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã duyệt Native Audio của cảnh." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task UpdateCharacterAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureCharacterOperationAllowed();
        var payload = request.Payload.Deserialize<UpdateCharacterWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Nội dung nhân vật cần cập nhật không hợp lệ.");
        var (projectId, userId) = CurrentProjectOwner();
        await _projectService.UpdateCharacterAsync(
            projectId,
            userId,
            new UpdateCharacterCommand(
                payload.CharacterId,
                payload.Name,
                payload.Role,
                payload.VisualIdentity,
                payload.Wardrobe,
                payload.ImmutableTraits,
                payload.ForbiddenChanges),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã lưu hồ sơ nhân vật." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task SelectCharacterReferenceAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureCharacterOperationAllowed();
        var payload = request.Payload.Deserialize<CharacterActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Nhân vật cần chọn ảnh không hợp lệ.");
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh tham chiếu nhân vật",
            Filter = "Ảnh JPEG hoặc PNG|*.jpg;*.jpeg;*.png",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var (projectId, userId) = CurrentProjectOwner();
        await _projectService.ImportCharacterReferenceAsync(
            projectId,
            userId,
            payload.CharacterId,
            dialog.FileName,
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã lưu ảnh tham chiếu. Hãy kiểm tra và khóa nhân vật." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task GenerateCharacterReferenceAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCharacterOperationAllowed();
        var payload = request.Payload.Deserialize<CharacterActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Nhân vật cần tạo ảnh không hợp lệ.");
        if (payload.CharacterId == Guid.Empty)
        {
            throw new ArgumentException("Nhân vật cần tạo ảnh không hợp lệ.");
        }

        await RunGenerationAsync(
            request.RequestId,
            async (projectId, userId, token) =>
            {
                var response = await _generationService.GenerateCharacterReferenceImageAsync(
                    projectId,
                    userId,
                    payload.CharacterId,
                    token);
                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new { message = $"Đã tạo ảnh nhân vật bằng {response.ModelCode}. Hãy kiểm tra trước khi khóa nhân vật." }));
            },
            cancellationToken);
    }

    private async Task ApproveCharacterAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureCharacterOperationAllowed();
        var payload = request.Payload.Deserialize<CharacterActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Nhân vật cần khóa không hợp lệ.");
        var (projectId, userId) = CurrentProjectOwner();
        await _projectService.ApproveCharacterAsync(
            projectId,
            userId,
            payload.CharacterId,
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã khóa nhân vật và ảnh tham chiếu cho các cảnh." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private void EnsureCharacterOperationAllowed()
    {
        if (_generationRunning)
        {
            throw new ArgumentException("Không thể thay đổi nhân vật khi tác vụ AI đang chạy.");
        }
    }

    private (Guid ProjectId, string UserId) CurrentProjectOwner()
    {
        var current = _sessionManager.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
        var projectId = _selectedProjectId
            ?? throw new ArgumentException("Hãy chọn hoặc tạo một dự án trước.");
        return (projectId, current.User.UserId);
    }

    private async Task RunGenerationAsync(
        string? requestId,
        Func<Guid, string, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (!_generationLock.Wait(0))
        {
            throw new ArgumentException("Một tác vụ tạo AI khác đang chạy.");
        }

        try
        {
            var current = _sessionManager.Current
                ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
            var projectId = _selectedProjectId
                ?? throw new ArgumentException("Hãy chọn hoặc tạo một dự án trước.");
            _generationRunning = true;
            await RefreshAsync(requestId, cancellationToken);
            await operation(projectId, current.User.UserId, cancellationToken);
        }
        finally
        {
            _generationRunning = false;
            _generationLock.Release();
            try
            {
                await RefreshAsync(requestId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RefreshAsync(string? requestId, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var current = _sessionManager.Current
                ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
            var organizations = await _generationClient.GetOrganizationsAsync(cancellationToken);
            if (organizations.Count == 0)
            {
                throw new AccountClientException(
                    "organization_access_denied",
                    "Tài khoản chưa được gán vào tổ chức. Hãy liên hệ quản trị viên.",
                    403);
            }
            var selectedOrganizationId = _generationClient.SelectedOrganizationId;
            if (selectedOrganizationId is null || organizations.All(x => x.OrganizationId != selectedOrganizationId.Value))
            {
                selectedOrganizationId = organizations[0].OrganizationId;
                await _generationClient.SelectOrganizationAsync(selectedOrganizationId.Value, cancellationToken);
            }

            var allProjects = await _projectService.ListAsync(current.User.UserId, cancellationToken);
            var projects = allProjects
                .Where(x => x.OrganizationId == selectedOrganizationId.Value)
                .ToArray();
            if (_selectedProjectId is null || projects.All(x => x.ProjectId != _selectedProjectId.Value))
            {
                _selectedProjectId = projects.FirstOrDefault()?.ProjectId;
            }

            var selectedProject = _selectedProjectId.HasValue
                ? await _projectService.GetDashboardAsync(
                    _selectedProjectId.Value,
                    current.User.UserId,
                    cancellationToken)
                : null;
            var models = await _projectService.ListAvailableModelsAsync(cancellationToken);
            GenerationProviderStatusResponse providerStatus;
            try
            {
                providerStatus = await _generationService.GetProviderStatusAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is AccountClientException or HttpRequestException)
            {
                providerStatus = new GenerationProviderStatusResponse(false, null, false, null);
            }
            var mediaToolStatus = await _mediaToolPreflight.GetStatusAsync(force: false, cancellationToken);
            Post(new WebMessageResponse(
                "dashboard.state",
                requestId,
                new DashboardStateResponse(
                    current.User,
                    organizations,
                    selectedOrganizationId.Value,
                    projects,
                    selectedProject,
                    models,
                    providerStatus,
                    mediaToolStatus,
                    _licenseManager.Current,
                    _generationRunning)));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private static string NormalizeTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Vui lòng nhập chủ đề video.");
        }

        var normalized = string.Join(' ', topic.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 300)
        {
            throw new ArgumentException("Chủ đề video không được vượt quá 300 ký tự.");
        }

        return normalized;
    }

    private static string CreateProjectName(string topic)
    {
        const int maxLength = 70;
        return topic.Length <= maxLength ? topic : $"{topic[..(maxLength - 1)].TrimEnd()}…";
    }

    private void PostError(string? requestId, string code, string message) =>
        Post(new WebMessageResponse("operation.error", requestId, Error: new WebMessageError(code, message)));

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
        _disposed = true;
    }
}
