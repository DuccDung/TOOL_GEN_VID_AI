using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Generation;
using TOOL_SHARED.Contracts.Generation;
using TOOL_LOCAL.Providers;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Payments;
using TOOL_LOCAL.Configuration;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.Projects;

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
    private readonly LicensePaymentApiClient _licensePaymentClient;
    private readonly bool _vietsubEnabled;
    private readonly bool _speechSynchronizationEnabled;
    private readonly string _applicationDirectory;
    private readonly Action<string> _postJson;
    private readonly Action _closeApplication;
    private readonly Func<string?>? _finalVideoExportSelector;
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
        LicensePaymentApiClient licensePaymentClient,
        bool vietsubEnabled,
        Action<string> postJson,
        Action closeApplication,
        bool speechSynchronizationEnabled = false,
        string? applicationDirectory = null,
        Func<string?>? finalVideoExportSelector = null)
    {
        _sessionManager = sessionManager;
        _licenseManager = licenseManager;
        _projectService = projectService;
        _projectRenderService = projectRenderService;
        _generationService = generationService;
        _generationClient = generationClient;
        _mediaToolPreflight = mediaToolPreflight;
        _licensePaymentClient = licensePaymentClient;
        _vietsubEnabled = vietsubEnabled;
        _speechSynchronizationEnabled = speechSynchronizationEnabled;
        _applicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
        _postJson = postJson;
        _closeApplication = closeApplication;
        _finalVideoExportSelector = finalVideoExportSelector;
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

        if (_licenseManager.IsLocked && !IsAllowedWhileLocked(request.Type))
        {
            PostError(
                request.RequestId,
                "license_required",
                _licenseManager.Current?.AccessMessage ?? "Bạn cần có gói sử dụng còn hiệu lực.");
            return;
        }

        try
        {
            switch (request.Type)
            {
                case "app.ready":
                    await RefreshAsync(
                        request.RequestId,
                        cancellationToken,
                        selectDefaultProject: false);
                    break;
                case "dashboard.refresh":
                    await RefreshAsync(
                        request.RequestId,
                        cancellationToken,
                        selectDefaultProject: _selectedProjectId.HasValue);
                    break;
                case "license.refresh":
                    await RefreshLicenseAsync(request.RequestId, cancellationToken);
                    break;
                case "license.offers.get":
                    await GetLicenseOffersAsync(request.RequestId, cancellationToken);
                    break;
                case "license.payment.create":
                    await CreateLicensePaymentAsync(request, cancellationToken);
                    break;
                case "license.payment.current.get":
                    await GetCurrentLicensePaymentAsync(request.RequestId, cancellationToken);
                    break;
                case "license.payment.status":
                    await GetLicensePaymentStatusAsync(request, cancellationToken);
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
                case "short-video.generate":
                    await GenerateShortVideoAsync(request, cancellationToken);
                    break;
                case "generation.content":
                    await GenerateContentAsync(request.RequestId, cancellationToken);
                    break;
                case "generation.content.repair.quote":
                    await GetContentRepairQuoteAsync(request, cancellationToken);
                    break;
                case "generation.content.repair":
                    await RepairContentAsync(request, cancellationToken);
                    break;
                case "generation.video":
                    await GenerateVideosAsync(request, cancellationToken);
                    break;
                case "generation.video.quote":
                    await GetCanonicalVoiceQuoteAsync(request, cancellationToken);
                    break;
                case "voice-profile.draft":
                    await CreateVoiceProfileDraftAsync(request, cancellationToken);
                    break;
                case "voice-catalog.preview.quote":
                    await GetVoiceCatalogPreviewQuoteAsync(request, cancellationToken);
                    break;
                case "voice-catalog.preview":
                    await GenerateVoiceCatalogPreviewAsync(request, cancellationToken);
                    break;
                case "voice-profile.preview.quote":
                    await GetVoiceProfilePreviewQuoteAsync(request, cancellationToken);
                    break;
                case "voice-profile.preview":
                    await GenerateVoiceProfilePreviewAsync(request, cancellationToken);
                    break;
                case "voice-profile.approve":
                    await ApproveVoiceProfileVersionAsync(request, cancellationToken);
                    break;
                case "voice-profile.supersede":
                    await SupersedeVoiceProfileVersionAsync(request, cancellationToken);
                    break;
                case "scene.first-frame.quote":
                    await GetSceneFirstFrameQuoteAsync(request, cancellationToken);
                    break;
                case "scene.first-frame.generate":
                    await GenerateSceneFirstFrameAsync(request, cancellationToken);
                    break;
                case "scene.first-frame.approve":
                    await ChangeSceneFirstFrameStatusAsync(request, approve: true, cancellationToken);
                    break;
                case "scene.first-frame.reject":
                    await ChangeSceneFirstFrameStatusAsync(request, approve: false, cancellationToken);
                    break;
                case "scene.first-frame.download":
                    await RetrySceneFirstFrameDownloadAsync(request, cancellationToken);
                    break;
                case "render.final":
                    await RenderFinalVideoAsync(request.RequestId, cancellationToken);
                    break;
                case "final-video.export":
                    await ExportFinalVideoAsync(request.RequestId, cancellationToken);
                    break;
                case "scene.update":
                    await UpdateSceneAsync(request, cancellationToken);
                    break;
                case "scene.native-audio.approve":
                    await ApproveSceneNativeAudioAsync(request, cancellationToken);
                    break;
                case "scene.audio.unapprove":
                    await UnapproveSceneAudioAsync(request, cancellationToken);
                    break;
                case "scene.speech.verify.quote":
                    await GetSceneSpeechVerificationQuoteAsync(request, cancellationToken);
                    break;
                case "scene.speech.verify":
                    await VerifySceneSpeechAsync(request, cancellationToken);
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
                case "project-asset.create":
                    await CreateProjectAssetAsync(request, cancellationToken);
                    break;
                case "project-asset.materialize":
                    await SynchronizeProjectAssetPlanAsync(request.RequestId, cancellationToken);
                    break;
                case "project-asset.update":
                    await UpdateProjectAssetAsync(request, cancellationToken);
                    break;
                case "project-asset.lock":
                    await ChangeProjectAssetLockAsync(request, lockAsset: true, cancellationToken);
                    break;
                case "project-asset.unlock":
                    await ChangeProjectAssetLockAsync(request, lockAsset: false, cancellationToken);
                    break;
                case "project-assets.approve-ai":
                    await ApproveAiProjectAssetsAsync(request, cancellationToken);
                    break;
                case "project-asset.delete":
                    await DeleteProjectAssetAsync(request, cancellationToken);
                    break;
                case "scene.assets.update":
                    await UpdateSceneAssetsAsync(request, cancellationToken);
                    break;
                case "scene.assets.confirm":
                    await ConfirmSceneAssetsAsync(request, cancellationToken);
                    break;
                case "providers.settings.get":
                    await GetProviderSettingsAsync(request.RequestId, cancellationToken);
                    break;
                case "providers.settings.test":
                    await TestProviderAsync(request, cancellationToken);
                    break;
                case "desktop.settings.get":
                    GetDesktopFeatureSettings(request.RequestId);
                    break;
                case "desktop.settings.update":
                    UpdateDesktopFeatureSettings(request);
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
            PostError(request.RequestId, exception.Code, exception.Message, exception.Errors);
        }
        catch (Exception)
        {
            PostError(request.RequestId, "operation_failed", "Không thể hoàn tất thao tác. Vui lòng thử lại.");
        }
    }

    public Task RefreshInBackgroundAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(
            null,
            cancellationToken,
            selectDefaultProject: _selectedProjectId.HasValue);

    private async Task RefreshLicenseAsync(string? requestId, CancellationToken cancellationToken)
    {
        await _licenseManager.RefreshNowAsync(cancellationToken);
        await RefreshAsync(requestId, cancellationToken, selectDefaultProject: false);
    }

    private async Task GetLicenseOffersAsync(string? requestId, CancellationToken cancellationToken)
    {
        var offers = await _licensePaymentClient.GetOffersAsync(cancellationToken);
        Post(new WebMessageResponse("license.offers", requestId, offers));
    }

    private async Task CreateLicensePaymentAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<CreateLicensePaymentWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Gói thanh toán không hợp lệ.");
        var checkout = await _licensePaymentClient.CreatePaymentAsync(
            new CreateLicensePaymentRequest(payload.LicensePlanId, payload.IdempotencyKey),
            cancellationToken);
        Post(new WebMessageResponse("license.payment.checkout", request.RequestId, checkout));
    }

    private async Task GetCurrentLicensePaymentAsync(
        string? requestId,
        CancellationToken cancellationToken)
    {
        var current = await _licensePaymentClient.GetCurrentPaymentAsync(cancellationToken);
        Post(new WebMessageResponse("license.payment.current", requestId, current));
    }

    private async Task GetLicensePaymentStatusAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<LicensePaymentStatusWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Giao dịch thanh toán không hợp lệ.");
        if (string.IsNullOrWhiteSpace(payload.OrderCode) || payload.OrderCode.Length > 40)
        {
            throw new ArgumentException("Giao dịch thanh toán không hợp lệ.");
        }

        var status = await _licensePaymentClient.GetStatusAsync(payload.OrderCode.Trim(), cancellationToken);
        Post(new WebMessageResponse("license.payment.status", request.RequestId, status));
        if (!status.IsFulfilled)
        {
            return;
        }

        var license = await _licenseManager.RefreshNowAsync(cancellationToken);
        if (!license.HasActiveLicense || !license.CurrentDeviceActivated)
        {
            PostError(
                request.RequestId,
                "license_activation_pending",
                license.AccessMessage ?? "Đã nhận thanh toán nhưng chưa thể kích hoạt thiết bị.");
            return;
        }

        if (license.AssignedOrganizationId is { } assignedOrganizationId)
        {
            await _generationClient.SelectOrganizationAsync(assignedOrganizationId, cancellationToken);
        }

        Post(new WebMessageResponse("license.activated", request.RequestId, license));
        await RefreshAsync(request.RequestId, cancellationToken, selectDefaultProject: false);
    }

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
        // Workflow Video Dài hiện dùng tiếng Việt xuyên suốt. Video Ngắn có
        // contract riêng và không đi qua nhánh tạo project này.
        const string languageCode = "vi-VN";
        var voiceCode = string.IsNullOrWhiteSpace(payload.VoiceCode)
            ? null
            : payload.VoiceCode.Trim();
        if (voiceCode is not null && !OpenAiBuiltInVoiceCatalog.IsSupported(voiceCode))
        {
            throw new ArgumentException("Giọng đọc được chọn không hợp lệ.");
        }
        var voiceSpeakingRate = payload.VoiceSpeakingRate;
        if (voiceSpeakingRate is { } rate && rate is < 0.5m or > 2m)
        {
            throw new ArgumentException("Tốc độ giọng đọc phải nằm trong khoảng 0,5–2,0.");
        }
        var speechProductionPolicy = SpeechProductionPolicies.IsSupported(payload.SpeechProductionPolicy)
            ? payload.SpeechProductionPolicy
            : throw new ArgumentException("Chính sách đồng bộ lời nói không được hỗ trợ.");
        if (speechProductionPolicy == SpeechProductionPolicies.CanonicalVoice &&
            !_speechSynchronizationEnabled)
        {
            throw new ArgumentException("Canonical Voice đang tắt theo cấu hình rollout.");
        }
        if (speechProductionPolicy == SpeechProductionPolicies.CanonicalVoice &&
            (voiceCode is null || voiceSpeakingRate is null))
        {
            throw new ArgumentException("Canonical Voice yêu cầu chọn giọng narrator và tốc độ đọc.");
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
                voiceSpeakingRate,
                speechProductionPolicy),
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

    private Task GenerateShortVideoAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<CreateShortVideoWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Thông tin tạo video ngắn không hợp lệ.");
        var content = payload.Content?.Trim() ?? string.Empty;
        if (content.Length is < 1 or > 2000)
        {
            throw new ArgumentException("Nội dung video phải có từ 1 đến 2.000 ký tự.");
        }
        var aspectRatio = payload.AspectRatio is "16:9" or "9:16" or "1:1"
            ? payload.AspectRatio
            : throw new ArgumentException("Tỷ lệ khung hình không được hỗ trợ.");
        if (payload.DurationSeconds is < 5 or > 15)
        {
            throw new ArgumentException("Thời lượng video phải nằm trong khoảng 5–15 giây.");
        }

        return RunExclusiveGenerationAsync(
            request.RequestId,
            async token =>
            {
                var current = _sessionManager.Current
                    ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
                var organizationId = _generationClient.SelectedOrganizationId
                    ?? throw new ArgumentException("Hãy chọn tổ chức trước khi tạo video.");

                await _mediaToolPreflight.RequireReadyAsync(token);
                var providerStatus = await _generationService.GetProviderStatusAsync(token);
                if (!providerStatus.VideoReady)
                {
                    throw new AccountClientException(
                        providerStatus.VideoUnavailableCode ?? "video_provider_not_ready",
                        providerStatus.VideoUnavailableMessage ??
                        "Kling chưa sẵn sàng cho tổ chức hiện tại. Hãy kiểm tra provider, model và rate Active.",
                        409);
                }
                if (!string.Equals(providerStatus.VideoProviderCode, "kling", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Màn hình này chỉ tạo clip bằng Kling. Hãy chọn Kling làm video policy của tổ chức.");
                }

                var result = await _projectService.CreateShortVideoAsync(
                    new CreateShortVideoCommand(
                        content,
                        aspectRatio,
                        payload.DurationSeconds,
                        payload.AudioEnabled,
                        organizationId),
                    current.User,
                    current.DeviceId,
                    token);
                _selectedProjectId = result.Project.ProjectId;
                Post(new WebMessageResponse(
                    "short-video.started",
                    request.RequestId,
                    new { projectId = result.Project.ProjectId, sceneId = result.SceneId }));
                await RefreshAsync(request.RequestId, token);

                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new { message = $"Đã tạo workflow một cảnh. Kling đang xử lý clip {payload.DurationSeconds} giây..." }));
                var count = await _generationService.GenerateVideosAsync(
                    result.Project.ProjectId,
                    current.User.UserId,
                    [result.SceneId],
                    async (message, progressToken) =>
                    {
                        Post(new WebMessageResponse("operation.notice", request.RequestId, new { message }));
                        await RefreshAsync(request.RequestId, progressToken);
                    },
                    token);
                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new
                    {
                        message = payload.AudioEnabled
                            ? $"Đã tạo xong {count} clip Kling {payload.DurationSeconds} giây. Hãy phát video để kiểm tra hình và Native Audio."
                            : $"Đã tạo xong {count} clip Kling {payload.DurationSeconds} giây. VideoMaker đã loại bỏ audio và tự động duyệt clip đầu ra."
                    }));
            },
            cancellationToken);
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

    private async Task GetContentRepairQuoteAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<ContentRepairWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Request content plan cần sửa không hợp lệ.");
        if (payload.FailedProviderRequestId == Guid.Empty)
        {
            throw new ArgumentException("Request content plan cần sửa không hợp lệ.");
        }

        var (projectId, _) = CurrentProjectOwner();
        var quote = await _generationService.GetContentRepairQuoteAsync(
            projectId,
            payload.FailedProviderRequestId,
            cancellationToken);
        Post(new WebMessageResponse("generation.content.repair.quote", request.RequestId, quote));
    }

    private Task RepairContentAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<ContentRepairWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Request content plan cần sửa không hợp lệ.");
        if (payload.FailedProviderRequestId == Guid.Empty)
        {
            throw new ArgumentException("Request content plan cần sửa không hợp lệ.");
        }

        return RunGenerationAsync(
            request.RequestId,
            async (projectId, userId, token) =>
            {
                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new { message = "OpenAI đang sửa các trường chưa đạt tiếng Việt..." }));
                var result = await _generationService.RepairContentAsync(
                    projectId,
                    userId,
                    payload.FailedProviderRequestId,
                    token);
                Post(new WebMessageResponse(
                    "generation.content.repaired",
                    request.RequestId,
                    new { result.ProviderRequestId, sceneCount = result.Plan.Scenes.Count }));
                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new { message = $"Đã sửa và lưu content plan tiếng Việt với {result.Plan.Scenes.Count} cảnh." }));
            },
            cancellationToken);
    }

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

    private async Task GetCanonicalVoiceQuoteAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = request.Payload.Deserialize<GenerateVideoWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Danh sách cảnh cần báo giá giọng không hợp lệ.");
        var sceneIds = payload.SceneIds?.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (sceneIds is null || sceneIds.Length == 0 || sceneIds.Length > 100)
        {
            throw new ArgumentException("Hãy chọn từ 1 đến 100 cảnh để báo giá giọng.");
        }
        var (projectId, userId) = CurrentProjectOwner();
        await RequireCanonicalVoiceReadyAsync(cancellationToken);
        var quote = await _generationService.GetCanonicalVoiceQuoteAsync(
            projectId,
            userId,
            sceneIds,
            cancellationToken);
        Post(new WebMessageResponse("generation.video.quote", request.RequestId, quote));
    }

    private async Task CreateVoiceProfileDraftAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = request.Payload.Deserialize<CreateVoiceProfileDraftWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cấu hình bản nháp giọng không hợp lệ.");
        if (payload.Scope is not (VoiceProfileScopes.ProjectNarrator or VoiceProfileScopes.Character) ||
            !OpenAiBuiltInVoiceCatalog.IsSupported(payload.VoiceCode) ||
            payload.SpeakingRate is < 0.5m or > 2m)
        {
            throw new ArgumentException("Cấu hình bản nháp giọng không hợp lệ.");
        }
        var (projectId, userId) = CurrentProjectOwner();
        var result = await _generationService.CreateVoiceProfileDraftAsync(
            projectId,
            userId,
            payload.Scope,
            payload.CharacterId,
            payload.VoiceCode,
            payload.SpeakingRate,
            cancellationToken);
        Post(new WebMessageResponse("voice-profile.drafted", request.RequestId, result));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task GetVoiceCatalogPreviewQuoteAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = RequireVoiceCatalogPreview(request);
        VoiceCatalogPreviewQuoteResponse quote;
        if (_selectedProjectId is { } projectId)
        {
            var current = _sessionManager.Current
                ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
            quote = await _generationService.GetVoiceCatalogPreviewQuoteAsync(
                projectId,
                current.User.UserId,
                payload.VoiceCode,
                payload.SpeakingRate,
                cancellationToken);
        }
        else
        {
            quote = await _generationService.GetVoiceCatalogPreviewContextQuoteAsync(
                payload.VoiceCode,
                payload.SpeakingRate,
                cancellationToken);
        }
        Post(new WebMessageResponse("voice-catalog.preview.quote", request.RequestId, quote));
    }

    private Task GenerateVoiceCatalogPreviewAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = RequireVoiceCatalogPreview(request);
        var operationRequestId = request.RequestId
            ?? throw new ArgumentException("Yêu cầu nghe thử giọng thiếu mã đối chiếu.");
        var contextProjectId = payload.ContextProjectId is { } value && value != Guid.Empty
            ? value
            : throw new ArgumentException("Yêu cầu nghe thử giọng thiếu ngữ cảnh báo giá hợp lệ.");
        return RunExclusiveGenerationAsync(
            request.RequestId,
            async token =>
            {
                var current = _sessionManager.Current
                    ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
                var result = await _generationService.GenerateVoiceCatalogPreviewAsync(
                    contextProjectId,
                    current.User.UserId,
                    payload.VoiceCode,
                    payload.SpeakingRate,
                    operationRequestId,
                    token);
                Post(new WebMessageResponse("voice-catalog.previewed", request.RequestId, result));
            },
            cancellationToken,
            // Catalog audition does not mutate a visible project. Refreshing here can
            // auto-select an existing project and unmount the picker opened from setup.
            refreshDashboard: false);
    }

    private async Task GetVoiceProfilePreviewQuoteAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = RequireVoiceProfileAction(request);
        var (projectId, userId) = CurrentProjectOwner();
        var quote = await _generationService.GetVoiceProfilePreviewQuoteAsync(
            projectId,
            userId,
            payload.VoiceProfileVersionId,
            payload.ExpectedVoiceSnapshotHash,
            cancellationToken);
        Post(new WebMessageResponse("voice-profile.preview.quote", request.RequestId, quote));
    }

    private Task GenerateVoiceProfilePreviewAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = RequireVoiceProfileAction(request);
        return RunGenerationAsync(
            request.RequestId,
            async (projectId, userId, token) =>
            {
                var result = await _generationService.GenerateVoiceProfilePreviewAsync(
                    projectId,
                    userId,
                    payload.VoiceProfileVersionId,
                    payload.ExpectedVoiceSnapshotHash,
                    token);
                Post(new WebMessageResponse("voice-profile.previewed", request.RequestId, result));
                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new { message = "Đã tạo và tải audio preview. Hãy phát nghe trước khi duyệt giọng." }));
            },
            cancellationToken);
    }

    private async Task ApproveVoiceProfileVersionAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = RequireVoiceProfileAction(request);
        var (projectId, userId) = CurrentProjectOwner();
        var result = await _generationService.ApproveVoiceProfileVersionAsync(
            projectId,
            userId,
            payload.VoiceProfileVersionId,
            payload.ExpectedVoiceSnapshotHash,
            payload.PlaybackConfirmed,
            cancellationToken);
        Post(new WebMessageResponse("voice-profile.approved", request.RequestId, result));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task SupersedeVoiceProfileVersionAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = RequireVoiceProfileAction(request);
        var (projectId, userId) = CurrentProjectOwner();
        var result = await _generationService.SupersedeVoiceProfileVersionAsync(
            projectId,
            userId,
            payload.VoiceProfileVersionId,
            payload.ExpectedVoiceSnapshotHash,
            cancellationToken);
        Post(new WebMessageResponse("voice-profile.superseded", request.RequestId, result));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private VoiceProfileActionWebRequest RequireVoiceProfileAction(WebMessageRequest request)
    {
        var payload = request.Payload.Deserialize<VoiceProfileActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Phiên bản giọng được chọn không hợp lệ.");
        if (payload.VoiceProfileVersionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(payload.ExpectedVoiceSnapshotHash) ||
            payload.ExpectedVoiceSnapshotHash.Length != 64)
        {
            throw new ArgumentException("Phiên bản giọng được chọn không hợp lệ.");
        }
        return payload;
    }

    private VoiceCatalogPreviewWebRequest RequireVoiceCatalogPreview(WebMessageRequest request)
    {
        var payload = request.Payload.Deserialize<VoiceCatalogPreviewWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Yêu cầu nghe thử giọng không hợp lệ.");
        if (!OpenAiBuiltInVoiceCatalog.IsSupported(payload.VoiceCode) ||
            payload.SpeakingRate is < 0.5m or > 2m)
        {
            throw new ArgumentException("Yêu cầu nghe thử giọng không hợp lệ.");
        }
        return payload with
        {
            VoiceCode = OpenAiBuiltInVoiceCatalog.NormalizeSelection(payload.VoiceCode)
        };
    }

    private Task GenerateVideosCoreAsync(
        string? requestId,
        IReadOnlyCollection<Guid>? sceneIds,
        CancellationToken cancellationToken) =>
        RunGenerationAsync(
            requestId,
            async (projectId, userId, token) =>
            {
                var dashboard = await _projectService.GetDashboardAsync(projectId, userId, token)
                    ?? throw new ArgumentException("Không tìm thấy dự án hiện hành.");
                var canonicalSpeechSelected =
                    dashboard.SpeechProductionPolicy == SpeechProductionPolicies.CanonicalVoice &&
                    dashboard.Scenes.Any(scene =>
                        scene.SpeechMode != KlingSpeechModes.None &&
                        (sceneIds is null || sceneIds.Contains(scene.SceneId)));
                if (canonicalSpeechSelected)
                {
                    await RequireCanonicalVoiceReadyAsync(token);
                }
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
                var completedDashboard = await _projectService.GetDashboardAsync(projectId, userId, token)
                    ?? throw new ArgumentException("Không tìm thấy dự án hiện hành sau khi xử lý cảnh.");
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new
                    {
                        message = BuildVideoGenerationCompletionMessage(
                            completedDashboard,
                            sceneIds,
                            count)
                    }));
            },
            cancellationToken);

    private static string BuildVideoGenerationCompletionMessage(
        ProjectDashboard dashboard,
        IReadOnlyCollection<Guid>? sceneIds,
        int completedCount)
    {
        var scenes = dashboard.Scenes
            .Where(scene => sceneIds is null || sceneIds.Contains(scene.SceneId))
            .ToArray();
        var canonicalWorkflow = dashboard.SpeechProductionPolicy == SpeechProductionPolicies.CanonicalVoice;
        var waitingForCanonicalReview = canonicalWorkflow
            ? scenes.Count(scene =>
                scene.SpeechMode != KlingSpeechModes.None &&
                scene.HasCanonicalVoicePreview &&
                scene.Preview is null &&
                scene.SpeechStatus is "SpeechVerificationRequired" or "SpeechReviewRequired")
            : 0;
        var waitingForLipSync = canonicalWorkflow
            ? scenes.Count(scene => scene.SpeechStatus == "SpeechReadyForLipSync")
            : 0;
        var waitingForVideoReview = scenes.Count(scene =>
            scene.Preview is not null &&
            scene.RequiresAudioReview &&
            !string.Equals(scene.Status, "Approved", StringComparison.OrdinalIgnoreCase));

        if (waitingForCanonicalReview > 0)
        {
            var completedPrefix = completedCount > 0
                ? $"Đã hoàn tất {completedCount} clip; đồng thời "
                : string.Empty;
            return $"{completedPrefix}đã chuẩn bị WAV cho {waitingForCanonicalReview} cảnh. " +
                   "Video chưa được gửi tạo cho các cảnh này. Tiếp theo: phát WAV và duyệt. " +
                   "Sau khi duyệt, chọn lại cảnh và bấm “Tạo video nền”.";
        }

        if (waitingForLipSync > 0)
        {
            return $"Đã duyệt Canonical WAV cho {waitingForLipSync} cảnh. Các cảnh này đang chờ bước lip-sync; chưa có request tạo video ở giai đoạn hiện tại.";
        }

        if (waitingForVideoReview > 0)
        {
            return canonicalWorkflow
                ? $"Đã tạo và ghép Canonical WAV cho {waitingForVideoReview} clip. Hãy phát video để kiểm tra hình và tiếng trước khi duyệt."
                : $"Đã tải {waitingForVideoReview} clip Native Audio vào workspace. Hãy phát và duyệt từng cảnh trước khi dựng video cuối.";
        }

        return completedCount > 0
            ? $"Đã hoàn tất xử lý {completedCount} clip trong workspace."
            : "Chưa có clip video mới. Hãy xem bước tiếp theo được hiển thị tại từng cảnh.";
    }

    private async Task RequireCanonicalVoiceReadyAsync(CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var status = await _generationService.GetProviderStatusAsync(cancellationToken);
        if (!status.CanonicalVoiceReady)
        {
            throw new AccountClientException(
                status.CanonicalVoiceUnavailableCode ?? "canonical_voice_not_ready",
                status.CanonicalVoiceUnavailableMessage ??
                "Canonical Voice chưa đủ TTS, rate hoặc budget tổ chức.",
                409);
        }
    }

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
        if (payload.SpeechVerificationReportId is { } reportId)
        {
            EnsureSpeechSynchronizationEnabled();
            await _generationService.ApproveSpeechVerificationReviewAsync(
                projectId,
                current.User.UserId,
                payload.SceneId,
                reportId,
                payload.SpeechReviewReason ?? string.Empty,
                payload.SpeechVerificationRowVersion ?? string.Empty,
                cancellationToken);
        }
        await _projectService.ApproveSceneNativeAudioAsync(
            projectId,
            current.User.UserId,
            payload.SceneId,
            payload.PlaybackConfirmed,
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã duyệt hình và âm thanh của cảnh." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task UnapproveSceneAudioAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (_generationRunning)
        {
            throw new ArgumentException("Không thể hủy duyệt cảnh khi tác vụ AI đang chạy.");
        }

        var payload = request.Payload.Deserialize<SceneActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cảnh cần hủy duyệt không hợp lệ.");
        var current = _sessionManager.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
        var projectId = _selectedProjectId
            ?? throw new ArgumentException("Hãy chọn dự án trước khi hủy duyệt cảnh.");
        await _projectService.UnapproveSceneAudioAsync(
            projectId,
            current.User.UserId,
            payload.SceneId,
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã hủy duyệt audio của cảnh. Bạn có thể phát nghe và duyệt lại media hiện hành." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task GetSceneSpeechVerificationQuoteAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = request.Payload.Deserialize<SceneActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cảnh cần kiểm tra lời nói không hợp lệ.");
        var (projectId, userId) = CurrentProjectOwner();
        var quote = await _generationService.GetSceneSpeechVerificationQuoteAsync(
            projectId,
            userId,
            payload.SceneId,
            cancellationToken);
        Post(new WebMessageResponse("scene.speech.verify.quote", request.RequestId, quote));
    }

    private Task VerifySceneSpeechAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureSpeechSynchronizationEnabled();
        var payload = request.Payload.Deserialize<SceneActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cảnh cần kiểm tra lời nói không hợp lệ.");
        return RunGenerationAsync(
            request.RequestId,
            async (projectId, userId, token) =>
            {
                var result = await _generationService.VerifySceneSpeechAsync(
                    projectId,
                    userId,
                    payload.SceneId,
                    token);
                Post(new WebMessageResponse("scene.speech.verified", request.RequestId, result));
                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new
                    {
                        message = result.Status == SpeechVerificationStatuses.Failed
                            ? "Transcript không khớp lời nói đã khóa."
                            : $"Đã kiểm tra transcript: {result.Status}, WER {result.WordErrorRate:P1}, CER {result.CharacterErrorRate:P1}."
                    }));
            },
            cancellationToken);
    }

    private async Task ExportFinalVideoAsync(string? requestId, CancellationToken cancellationToken)
    {
        if (_generationRunning)
        {
            throw new ArgumentException("Hãy đợi tác vụ đang chạy hoàn tất trước khi xuất video.");
        }
        if (_finalVideoExportSelector is null)
        {
            throw new ArgumentException("Ứng dụng chưa thể mở hộp thoại chọn vị trí lưu video.");
        }

        var destinationPath = _finalVideoExportSelector();
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            await RefreshAsync(requestId, cancellationToken, selectDefaultProject: false);
            return;
        }

        var (projectId, userId) = CurrentProjectOwner();
        FinalVideoExportResult result;
        try
        {
            result = await _projectRenderService.ExportFinalVideoAsync(
                projectId,
                userId,
                destinationPath,
                cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ArgumentException("Không có quyền ghi file vào vị trí đã chọn.", exception);
        }
        catch (IOException exception)
        {
            throw new ArgumentException("Không thể ghi file MP4 vào vị trí đã chọn.", exception);
        }

        Post(new WebMessageResponse(
            "operation.notice",
            requestId,
            new { message = $"Đã xuất video v{result.Version}: {result.FileName}" }));
        await RefreshAsync(requestId, cancellationToken, selectDefaultProject: false);
    }

    private void EnsureSpeechSynchronizationEnabled()
    {
        if (!_speechSynchronizationEnabled)
        {
            throw new ArgumentException("Đồng bộ lời nói đang bị tắt bằng feature flag vận hành.");
        }
    }

    private void GetDesktopFeatureSettings(string? requestId)
    {
        var configuredValue = DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(
            _applicationDirectory,
            _speechSynchronizationEnabled);
        PostDesktopFeatureSettings("desktop.settings", requestId, configuredValue);
    }

    private void UpdateDesktopFeatureSettings(WebMessageRequest request)
    {
        var payload = request.Payload.Deserialize<UpdateDesktopFeatureSettingsWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cấu hình Desktop không hợp lệ.");
        try
        {
            DesktopUserSettingsStore.WriteSpeechSynchronizationEnabled(
                _applicationDirectory,
                payload.SpeechSynchronizationEnabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PostError(
                request.RequestId,
                "desktop_settings_save_failed",
                "Không thể lưu cài đặt trên máy này. Hãy kiểm tra quyền ghi thư mục ứng dụng.");
            return;
        }

        PostDesktopFeatureSettings(
            "desktop.settings.updated",
            request.RequestId,
            payload.SpeechSynchronizationEnabled);
    }

    private void PostDesktopFeatureSettings(string responseType, string? requestId, bool configuredValue) =>
        Post(new WebMessageResponse(
            responseType,
            requestId,
            new DesktopFeatureSettingsResponse(
                configuredValue,
                _speechSynchronizationEnabled,
                configuredValue != _speechSynchronizationEnabled)));

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
                payload.ForbiddenChanges,
                payload.VoiceCode,
                payload.VoiceSpeakingRate),
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

    private async Task GetSceneFirstFrameQuoteAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<SceneFirstFrameActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cảnh cần tạo first-frame không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        var quote = await _generationService.GetSceneFirstFrameQuoteAsync(projectId, payload.SceneId, cancellationToken);
        Post(new WebMessageResponse("scene.first-frame.quote", request.RequestId, quote));
    }

    private Task GenerateSceneFirstFrameAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<SceneFirstFrameActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Cảnh cần tạo first-frame không hợp lệ.");
        return RunGenerationAsync(
            request.RequestId,
            async (projectId, userId, token) =>
            {
                var frame = await _generationService.GenerateSceneFirstFrameAsync(
                    projectId,
                    userId,
                    payload.SceneId,
                    payload.Attempt,
                    token);
                Post(new WebMessageResponse(
                    "operation.notice",
                    request.RequestId,
                    new { message = $"Đã tạo first-frame bản {frame.Version}. Hãy xem ảnh và duyệt trước khi tạo video Veo." }));
            },
            cancellationToken);
    }

    private async Task ChangeSceneFirstFrameStatusAsync(
        WebMessageRequest request,
        bool approve,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<SceneFirstFrameActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("First-frame cần cập nhật không hợp lệ.");
        if (payload.FrameId is null || string.IsNullOrWhiteSpace(payload.RowVersion))
        {
            throw new ArgumentException("First-frame hoặc RowVersion không hợp lệ.");
        }
        var (projectId, _) = CurrentProjectOwner();
        if (approve)
        {
            await _generationService.ApproveSceneFirstFrameAsync(
                projectId,
                payload.SceneId,
                payload.FrameId.Value,
                payload.RowVersion,
                cancellationToken);
        }
        else
        {
            await _generationService.RejectSceneFirstFrameAsync(
                projectId,
                payload.SceneId,
                payload.FrameId.Value,
                payload.RowVersion,
                cancellationToken);
        }
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = approve ? "Đã duyệt first-frame cho cảnh." : "Đã từ chối first-frame." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task RetrySceneFirstFrameDownloadAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload.Deserialize<SceneFirstFrameActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("First-frame cần tải lại không hợp lệ.");
        if (payload.FrameId is null)
        {
            throw new ArgumentException("First-frame cần tải lại không hợp lệ.");
        }
        var (projectId, _) = CurrentProjectOwner();
        await _generationService.RetrySceneFirstFrameDownloadAsync(
            projectId,
            payload.SceneId,
            payload.FrameId.Value,
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã tải lại first-frame từ output hiện có; không tạo request AI mới." }));
        await RefreshAsync(request.RequestId, cancellationToken);
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

    private async Task CreateProjectAssetAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureProjectAssetOperationAllowed();
        var payload = request.Payload.Deserialize<CreateProjectAssetWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Nội dung tài sản cần tạo không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        await _generationClient.CreateProjectAssetAsync(
            projectId,
            new CreateProjectAssetRequest(payload.AssetType, payload.Name, payload.CanonicalDescription),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã tạo hồ sơ text ở trạng thái nháp." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private Task SynchronizeProjectAssetPlanAsync(string? requestId, CancellationToken cancellationToken) =>
        RunGenerationAsync(
            requestId,
            async (projectId, userId, token) =>
            {
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new { message = "Đang đồng bộ thư viện tài sản từ content plan đã lưu..." }));
                var result = await _generationService.SynchronizeProjectAssetPlanAsync(projectId, userId, token);
                Post(new WebMessageResponse(
                    "operation.notice",
                    requestId,
                    new
                    {
                        message = $"Đã đồng bộ {result.CreatedAssets + result.UpdatedDraftAssets + result.PreservedAssets} tài sản cho {result.SceneAssignments} liên kết cảnh."
                    }));
            },
            cancellationToken);

    private async Task UpdateProjectAssetAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureProjectAssetOperationAllowed();
        var payload = request.Payload.Deserialize<UpdateProjectAssetWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Nội dung tài sản cần cập nhật không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        await _generationClient.UpdateProjectAssetAsync(
            projectId,
            payload.ProjectAssetId,
            new UpdateProjectAssetRequest(
                payload.AssetType,
                payload.Name,
                payload.CanonicalDescription,
                payload.ConcurrencyToken),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã cập nhật mô tả text của tài sản." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task ChangeProjectAssetLockAsync(
        WebMessageRequest request,
        bool lockAsset,
        CancellationToken cancellationToken)
    {
        EnsureProjectAssetOperationAllowed();
        var payload = request.Payload.Deserialize<ProjectAssetActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Tài sản được chọn không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        var changeRequest = new ChangeProjectAssetLockRequest(payload.ConcurrencyToken);
        if (lockAsset)
        {
            await _generationClient.LockProjectAssetAsync(
                projectId,
                payload.ProjectAssetId,
                changeRequest,
                cancellationToken);
        }
        else
        {
            await _generationClient.UnlockProjectAssetAsync(
                projectId,
                payload.ProjectAssetId,
                changeRequest,
                cancellationToken);
        }
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = lockAsset ? "Đã khóa phiên bản text cho các cảnh." : "Đã mở khóa tài sản để chỉnh sửa." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task DeleteProjectAssetAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureProjectAssetOperationAllowed();
        var payload = request.Payload.Deserialize<ProjectAssetActionWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Tài sản cần xóa không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        await _generationClient.DeleteProjectAssetAsync(
            projectId,
            payload.ProjectAssetId,
            new DeleteProjectAssetRequest(payload.ConcurrencyToken),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã xóa tài sản nháp chưa từng được sử dụng." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task ApproveAiProjectAssetsAsync(
        WebMessageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureProjectAssetOperationAllowed();
        var payload = request.Payload.Deserialize<ApproveAiProjectAssetsWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Danh sách tài sản AI cần duyệt không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        var result = await _generationClient.ApproveAiProjectAssetsAsync(
            projectId,
            new ApproveAiProjectAssetsRequest(payload.Assets ?? []),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new
            {
                message = result.LockedAssets == 0
                    ? "Không còn tài sản AI đang dùng cần khóa."
                    : $"Đã duyệt và khóa {result.LockedAssets} tài sản AI. {result.ReadyScenes}/{result.TotalScenes} cảnh đã sẵn sàng về tài sản."
            }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task UpdateSceneAssetsAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureProjectAssetOperationAllowed();
        var payload = request.Payload.Deserialize<UpdateSceneAssetsWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Danh sách tài sản của cảnh không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        await _generationClient.UpdateSceneAssetAssignmentsAsync(
            projectId,
            payload.SceneId,
            new UpdateSceneAssetAssignmentsRequest(payload.ProjectAssetIds ?? []),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new { message = "Đã cập nhật tài sản áp dụng cho cảnh." }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private async Task ConfirmSceneAssetsAsync(WebMessageRequest request, CancellationToken cancellationToken)
    {
        EnsureProjectAssetOperationAllowed();
        var payload = request.Payload.Deserialize<ConfirmSceneAssetsWebRequest>(_jsonOptions)
            ?? throw new ArgumentException("Tài sản của cảnh cần xác nhận không hợp lệ.");
        var (projectId, _) = CurrentProjectOwner();
        var result = await _generationClient.ConfirmSceneProjectAssetsAsync(
            projectId,
            payload.SceneId,
            new ConfirmSceneProjectAssetsRequest(payload.Assets ?? []),
            cancellationToken);
        Post(new WebMessageResponse(
            "operation.notice",
            request.RequestId,
            new
            {
                message = result.LockedAssets > 0
                    ? $"Đã xác nhận {result.LockedAssets} tài sản. Cảnh đã sẵn sàng để tạo clip."
                    : "Tài sản của cảnh đã sẵn sàng để tạo clip."
            }));
        await RefreshAsync(request.RequestId, cancellationToken);
    }

    private void EnsureProjectAssetOperationAllowed()
    {
        if (_generationRunning)
        {
            throw new ArgumentException("Không thể thay đổi thư viện tài sản khi tác vụ AI đang chạy.");
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

    private Task RunGenerationAsync(
        string? requestId,
        Func<Guid, string, CancellationToken, Task> operation,
        CancellationToken cancellationToken) =>
        RunExclusiveGenerationAsync(
            requestId,
            async token =>
            {
                var current = _sessionManager.Current
                    ?? throw new InvalidOperationException("Phiên đăng nhập không còn hiệu lực.");
                var projectId = _selectedProjectId
                    ?? throw new ArgumentException("Hãy chọn hoặc tạo một dự án trước.");
                await operation(projectId, current.User.UserId, token);
            },
            cancellationToken);

    private async Task RunExclusiveGenerationAsync(
        string? requestId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool refreshDashboard = true)
    {
        if (!_generationLock.Wait(0))
        {
            throw new ArgumentException("Một tác vụ tạo AI khác đang chạy.");
        }

        try
        {
            _generationRunning = true;
            if (refreshDashboard)
            {
                await RefreshAsync(requestId, cancellationToken);
            }
            await operation(cancellationToken);
        }
        finally
        {
            _generationRunning = false;
            _generationLock.Release();
            if (refreshDashboard)
            {
                try
                {
                    await RefreshAsync(requestId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        }
    }

    private async Task RefreshAsync(
        string? requestId,
        CancellationToken cancellationToken,
        bool selectDefaultProject = true)
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
            if (_licenseManager.Current is { HasActiveLicense: true } && !_licenseManager.HasValidLease)
            {
                await _licenseManager.RefreshNowAsync(cancellationToken);
            }
            if (_licenseManager.IsLocked)
            {
                _selectedProjectId = null;
                Post(new WebMessageResponse(
                    "dashboard.state",
                    requestId,
                    new DashboardStateResponse(
                        current.User,
                        [],
                        Guid.Empty,
                        [],
                        null,
                        null,
                        [],
                        new GenerationProviderStatusResponse(false, null, false, null),
                        new MediaToolStatusSummary(
                            false,
                            "license_required",
                            "Kích hoạt gói sử dụng để kiểm tra công cụ media.",
                            null,
                            null,
                            DateTime.UtcNow),
                        _licenseManager.Current,
                        false,
                        new DashboardFeatureFlagsResponse(_vietsubEnabled, _speechSynchronizationEnabled),
                        [])));
                return;
            }
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
            _selectedProjectId = ResolveSelectedProjectId(
                _selectedProjectId,
                projects.Select(x => x.ProjectId).ToArray(),
                selectDefaultProject);

            var selectedProject = _selectedProjectId.HasValue
                ? await _projectService.GetDashboardAsync(
                    _selectedProjectId.Value,
                    current.User.UserId,
                    cancellationToken)
                : null;
            ProjectAssetLibraryResponse? assetLibrary = null;
            IReadOnlyList<SceneFirstFrameSummary> sceneFirstFrames = [];
            ContentLanguageFailureResponse? contentLanguageFailure = null;
            if (selectedProject is not null)
            {
                try
                {
                    contentLanguageFailure = await _generationService.GetLatestContentLanguageFailureAsync(
                        selectedProject.Project.ProjectId,
                        cancellationToken);
                }
                catch (AccountClientException exception) when (exception.Code == "content_failure_schema_not_ready")
                {
                    contentLanguageFailure = new ContentLanguageFailureResponse(
                        Guid.Empty,
                        exception.Code,
                        exception.Message,
                        [],
                        false);
                }
                assetLibrary = await _generationClient.GetProjectAssetLibraryAsync(
                    selectedProject.Project.ProjectId,
                    cancellationToken);
                if (string.Equals(selectedProject.VideoProviderCode, "fal", StringComparison.OrdinalIgnoreCase))
                {
                    sceneFirstFrames = (await _generationService.GetProjectSceneFirstFramesAsync(
                        selectedProject.Project.ProjectId,
                        cancellationToken)).Frames;
                }
            }
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
                    assetLibrary,
                    models,
                    providerStatus,
                    mediaToolStatus,
                    _licenseManager.Current,
                    _generationRunning,
                    new DashboardFeatureFlagsResponse(_vietsubEnabled, _speechSynchronizationEnabled),
                    sceneFirstFrames,
                    contentLanguageFailure)));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    internal static Guid? ResolveSelectedProjectId(
        Guid? selectedProjectId,
        IReadOnlyList<Guid> availableProjectIds,
        bool selectDefaultProject)
    {
        if (selectedProjectId.HasValue && availableProjectIds.Contains(selectedProjectId.Value))
        {
            return selectedProjectId;
        }

        return selectDefaultProject && availableProjectIds.Count > 0
            ? availableProjectIds[0]
            : null;
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

    internal static bool IsAllowedWhileLocked(string operationType) => operationType is
        "app.ready" or
        "dashboard.refresh" or
        "license.refresh" or
        "license.offers.get" or
        "license.payment.create" or
        "license.payment.current.get" or
        "license.payment.status" or
        "auth.logout";

    private static string CreateProjectName(string topic)
    {
        const int maxLength = 70;
        return topic.Length <= maxLength ? topic : $"{topic[..(maxLength - 1)].TrimEnd()}…";
    }

    private void PostError(
        string? requestId,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        Post(new WebMessageResponse(
            "operation.error",
            requestId,
            Error: new WebMessageError(code, message, errors)));

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
