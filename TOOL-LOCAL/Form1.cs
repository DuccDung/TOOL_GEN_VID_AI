using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_LOCAL.WebView;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Updates;
using TOOL_SHARED.Contracts.Updates;
using System.Text.Json;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Playback;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Payments;
using System.Runtime.InteropServices;

namespace TOOL_LOCAL;

public partial class Form1 : Form
{
    private const string AppHostName = "app.local";
    private const string MediaHostName = "media.app.local";
    private readonly AccountSessionManager? _sessionManager;
    private readonly LicenseSessionManager? _licenseManager;
    private readonly IProjectService? _projectService;
    private readonly IProjectRenderService? _projectRenderService;
    private readonly IProjectGenerationService? _generationService;
    private readonly IGenerationClient? _generationClient;
    private readonly ProjectWorkspaceService? _workspaceService;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly BackgroundRefreshErrorTracker _backgroundRefreshErrors = new();
    private readonly DesktopUpdateApiClient? _updateApiClient;
    private readonly DesktopPackageUpdateService? _packageUpdateService;
    private readonly DesktopUpdateOptions? _updateOptions;
    private readonly IMediaToolPreflightService? _mediaToolPreflight;
    private readonly DesktopFeatureOptions? _featureOptions;
    private readonly VietsubProjectStore? _vietsubProjectStore;
    private readonly IVietsubProjectRegistryClient? _vietsubProjectRegistryClient;
    private readonly VietsubMediaImportService? _vietsubMediaImportService;
    private readonly VietsubTimelineThumbnailService? _vietsubThumbnailService;
    private readonly VietsubTimelineWaveformService? _vietsubWaveformService;
    private readonly VietsubSubtitleService? _vietsubSubtitleService;
    private readonly VietsubJobManager? _vietsubJobManager;
    private readonly VietsubOcrService? _vietsubOcrService;
    private readonly LicensePaymentApiClient? _licensePaymentClient;
    private readonly VietsubMediaRuntimeLog _vietsubMediaLog = VietsubMediaRuntimeLog.CreateDefault();
    private WebView2? _webView;
    private Panel? _loadingPanel;
    private Label? _loadingLabel;
    private DashboardBridge? _bridge;
    private VietsubWebBridge? _vietsubBridge;
    private bool _refreshing;
    private bool _closing;
    private bool _checkingUpdate;
    private bool _applyingUpdate;
    private bool _preparingMediaRepair;
    private Guid? _dismissedReleaseId;
    private DesktopUpdateCheckResponse? _availableUpdate;
    private DesktopReleaseResponse? _mediaRepairRelease;

    internal bool ReturnToLoginRequested { get; private set; }

    public Form1()
    {
        InitializeComponent();
    }

    internal Form1(
        AccountSessionManager sessionManager,
        LicenseSessionManager licenseManager,
        IProjectService projectService,
        IProjectRenderService projectRenderService,
        IProjectGenerationService generationService,
        IGenerationClient generationClient,
        ProjectWorkspaceService workspaceService,
        DesktopUpdateApiClient updateApiClient,
        DesktopPackageUpdateService packageUpdateService,
        DesktopUpdateOptions updateOptions,
        IMediaToolPreflightService mediaToolPreflight,
        DesktopFeatureOptions featureOptions,
        VietsubProjectStore? vietsubProjectStore,
        IVietsubProjectRegistryClient? vietsubProjectRegistryClient,
        VietsubMediaImportService? vietsubMediaImportService,
        VietsubTimelineThumbnailService? vietsubThumbnailService,
        VietsubTimelineWaveformService? vietsubWaveformService,
        VietsubSubtitleService? vietsubSubtitleService,
        VietsubJobManager? vietsubJobManager,
        VietsubOcrService? vietsubOcrService,
        LicensePaymentApiClient licensePaymentClient) : this()
    {
        _sessionManager = sessionManager;
        _licenseManager = licenseManager;
        _projectService = projectService;
        _projectRenderService = projectRenderService;
        _generationService = generationService;
        _generationClient = generationClient;
        _workspaceService = workspaceService;
        _updateApiClient = updateApiClient;
        _packageUpdateService = packageUpdateService;
        _updateOptions = updateOptions;
        _mediaToolPreflight = mediaToolPreflight;
        _featureOptions = featureOptions;
        _vietsubProjectStore = vietsubProjectStore;
        _vietsubProjectRegistryClient = vietsubProjectRegistryClient;
        _vietsubMediaImportService = vietsubMediaImportService;
        _vietsubThumbnailService = vietsubThumbnailService;
        _vietsubWaveformService = vietsubWaveformService;
        _vietsubSubtitleService = vietsubSubtitleService;
        _vietsubJobManager = vietsubJobManager;
        _vietsubOcrService = vietsubOcrService;
        _licensePaymentClient = licensePaymentClient;
        _updateTimer.Interval = Math.Max(30, updateOptions.CheckIntervalSeconds) * 1000;
        ConfigureWindow();
        Shown += InitializeDashboardOnShown;
        FormClosed += FormOnClosed;
        _refreshTimer.Tick += RefreshTimerOnTick;
        _updateTimer.Tick += UpdateTimerOnTick;
        _licenseManager.LicenseInvalidated += LicenseManagerOnInvalidated;
        _sessionManager.SessionInvalidated += SessionManagerOnInvalidated;
    }

    private void ConfigureWindow()
    {
        SuspendLayout();
        Text = "VideoMaker";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1024, 700);
        ClientSize = new Size(1440, 900);
        WindowState = FormWindowState.Maximized;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(247, 249, 252);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
            DefaultBackgroundColor = Color.FromArgb(247, 249, 252)
        };
        _loadingPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(247, 249, 252)
        };
        _loadingLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Đang khởi tạo giao diện VideoMaker...",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11f),
            ForeColor = Color.FromArgb(91, 105, 128)
        };
        _loadingPanel.Controls.Add(_loadingLabel);
        Controls.Add(_webView);
        Controls.Add(_loadingPanel);
        _loadingPanel.BringToFront();
        ResumeLayout(false);
    }

    private async void InitializeDashboardOnShown(object? sender, EventArgs eventArgs)
    {
        if (_webView is null || _sessionManager is null || _licenseManager is null || _projectService is null || _projectRenderService is null || _generationService is null || _generationClient is null || _workspaceService is null || _mediaToolPreflight is null || _featureOptions is null || _licensePaymentClient is null)
        {
            return;
        }

        try
        {
            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var indexPath = Path.Combine(webRoot, "index.html");
            if (!File.Exists(indexPath))
            {
                throw new InvalidOperationException("Không tìm thấy giao diện dashboard đã build.");
            }

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolGenPostVideo",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            ConfigureWebViewSecurity(_webView.CoreWebView2, webRoot, _workspaceService.WorkspaceRoot);

            _bridge = new DashboardBridge(
                _sessionManager,
                _licenseManager,
                _projectService,
                _projectRenderService,
                _generationService,
                _generationClient,
                _mediaToolPreflight,
                _licensePaymentClient,
                _featureOptions.VietsubEnabled,
                PostJsonToWebView,
                CloseAfterLogout);
            _vietsubBridge = new VietsubWebBridge(
                _featureOptions.VietsubEnabled,
                PostJsonToWebView,
                _vietsubProjectStore,
                () =>
                {
                    var current = _sessionManager.Current;
                    var organizationId = _generationClient.SelectedOrganizationId;
                    return current is null || organizationId is null
                        ? null
                        : new VietsubUserContext(current.User.UserId, organizationId.Value);
                },
                _vietsubProjectRegistryClient,
                _vietsubMediaImportService,
                SelectVietsubMediaFile,
                _vietsubMediaImportService is null
                    ? null
                    : new VietsubMediaPlaybackService(
                        _vietsubMediaImportService,
                        _vietsubThumbnailService,
                        _vietsubWaveformService),
                _vietsubThumbnailService,
                _vietsubSubtitleService,
                SelectVietsubSrtFile,
                SelectVietsubSrtDestination,
                _vietsubJobManager,
                _vietsubOcrService,
                _vietsubWaveformService);
            _webView.CoreWebView2.WebMessageReceived += WebViewOnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += WebViewOnNavigationCompleted;
            var webVersion = File.GetLastWriteTimeUtc(indexPath).Ticks;
            _webView.CoreWebView2.Navigate($"https://{AppHostName}/index.html?v={webVersion}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError("Máy tính chưa cài Microsoft Edge WebView2 Runtime.");
        }
        catch (Exception exception)
        {
            ShowStartupError(exception.Message);
        }
    }

    private void ConfigureWebViewSecurity(CoreWebView2 coreWebView, string webRoot, string workspaceRoot)
    {
        coreWebView.SetVirtualHostNameToFolderMapping(
            AppHostName,
            webRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        coreWebView.SetVirtualHostNameToFolderMapping(
            MediaHostName,
            workspaceRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        coreWebView.AddWebResourceRequestedFilter(
            $"https://{VietsubMediaPlaybackService.HostName}/*",
            CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        coreWebView.WebResourceRequested += WebViewOnVietsubMediaRequested;
        coreWebView.WebResourceResponseReceived += WebViewOnVietsubMediaResponseReceived;

        var settings = coreWebView.Settings;
        settings.IsWebMessageEnabled = true;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif

        coreWebView.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedTopLevelNavigation(args.Uri))
            {
                args.Cancel = true;
            }
        };
        coreWebView.NewWindowRequested += (_, args) => args.Handled = true;
        coreWebView.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        coreWebView.DownloadStarting += (_, args) => args.Cancel = true;
    }

    private async void WebViewOnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (_bridge is null || !IsTrustedWebMessageSource(eventArgs.Source))
        {
            return;
        }

        string message;
        try
        {
            message = eventArgs.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        if (await TryHandleUpdateMessageAsync(message))
        {
            return;
        }

        if (_vietsubBridge is not null &&
            await _vietsubBridge.TryHandleAsync(message, _shutdown.Token))
        {
            return;
        }

        await _bridge.HandleAsync(message, _shutdown.Token);
    }

    private void WebViewOnVietsubMediaRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (_webView?.CoreWebView2 is not { } coreWebView
            || !Uri.TryCreate(eventArgs.Request.Uri, UriKind.Absolute, out var requestUri)
            || !requestUri.Host.Equals(VietsubMediaPlaybackService.HostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var resourceType = VietsubMediaPlaybackService.ClassifyResource(requestUri);
        var method = eventArgs.Request.Method;
        _vietsubMediaLog.Write(
            correlationId,
            resourceType,
            method,
            null,
            null,
            "filter");

        var rangeHeader = ReadVietsubRangeHeader(
            eventArgs.Request.Headers,
            resourceType,
            out var rangeHeaderExceptionType);
        if (rangeHeaderExceptionType is not null)
        {
            _vietsubMediaLog.Write(
                correlationId,
                resourceType,
                method,
                null,
                "vietsub_media_range_header_unavailable",
                "request_headers",
                rangeHeaderExceptionType);
        }

        VietsubPlaybackResponse response;
        try
        {
            _vietsubMediaLog.Write(
                correlationId,
                resourceType,
                method,
                null,
                null,
                "bridge");
            response = _vietsubBridge?.TryOpenPlaybackRequest(
                requestUri,
                method,
                rangeHeader)
                ?? VietsubMediaPlaybackService.Error(
                    503,
                    "Service Unavailable",
                    "vietsub_media_bridge_unavailable",
                    resourceType);
        }
        catch (Exception exception)
        {
            _vietsubMediaLog.Write(
                correlationId,
                resourceType,
                method,
                500,
                "vietsub_media_request_failed",
                "bridge",
                exception.GetType().Name);
            response = VietsubMediaPlaybackService.Error(
                500,
                "Internal Server Error",
                "vietsub_media_request_failed",
                resourceType);
        }
        _vietsubMediaLog.Write(
            correlationId,
            response.ResourceType,
            method,
            response.StatusCode,
            response.ErrorCode,
            "playback");
        if (response.StatusCode >= 400)
        {
            PostVietsubMediaFailure(
                response.ResourceType,
                correlationId,
                response.ErrorCode ?? "vietsub_media_unknown_error");
        }
        try
        {
            eventArgs.Response = CreateVietsubWebResourceResponse(
                coreWebView.Environment,
                response,
                correlationId);
            _vietsubMediaLog.Write(
                correlationId,
                response.ResourceType,
                method,
                response.StatusCode,
                response.ErrorCode,
                "response_creation");
        }
        catch (Exception exception)
        {
            response.Content.Dispose();
            const string responseErrorCode = "vietsub_media_response_creation_failed";
            _vietsubMediaLog.Write(
                correlationId,
                response.ResourceType,
                method,
                500,
                responseErrorCode,
                "response_creation",
                exception.GetType().Name);
            PostVietsubMediaFailure(response.ResourceType, correlationId, responseErrorCode);
            eventArgs.Response = coreWebView.Environment.CreateWebResourceResponse(
                Stream.Null,
                500,
                "Internal Server Error",
                "Content-Length: 0\r\n" +
                "Cache-Control: no-store\r\n" +
                $"X-Vietsub-Error-Code: {responseErrorCode}\r\n" +
                $"X-Vietsub-Correlation-Id: {correlationId}\r\n");
        }
    }

    internal static CoreWebView2WebResourceResponse CreateVietsubWebResourceResponse(
        CoreWebView2Environment environment,
        VietsubPlaybackResponse response,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(response);
        var webResponse = environment.CreateWebResourceResponse(
            response.Content,
            response.StatusCode,
            response.ReasonPhrase,
            string.Empty);
        foreach (var header in response.Headers.Split(
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = header.IndexOf(':');
            if (separator <= 0 || separator == header.Length - 1)
            {
                throw new InvalidDataException("Header media nội bộ không hợp lệ.");
            }

            webResponse.Headers.AppendHeader(
                header[..separator].Trim(),
                header[(separator + 1)..].Trim());
        }
        if (Guid.TryParseExact(correlationId, "N", out var parsedCorrelation))
        {
            webResponse.Headers.AppendHeader(
                "X-Vietsub-Correlation-Id",
                parsedCorrelation.ToString("N"));
        }
        return webResponse;
    }

    internal static string? ReadVietsubRangeHeader(
        CoreWebView2HttpRequestHeaders headers,
        string resourceType,
        out string? exceptionType)
    {
        ArgumentNullException.ThrowIfNull(headers);
        exceptionType = null;
        if (!string.Equals(
            resourceType,
            VietsubPlaybackResourceTypes.Video,
            StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return headers.Contains("Range")
                ? headers.GetHeader("Range")
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or COMException)
        {
            exceptionType = exception.GetType().Name;
            return null;
        }
    }

    internal static string ReadVietsubResponseHeader(
        CoreWebView2HttpResponseHeaders headers,
        string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            return headers.Contains(name) ? headers.GetHeader(name) : string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or COMException)
        {
            return string.Empty;
        }
    }

    private void PostVietsubMediaFailure(
        string resourceType,
        string correlationId,
        string errorCode) =>
        PostHostMessage(
            "vietsub.media.load.failed",
            VietsubMediaLoadFailure.Create(resourceType, correlationId, errorCode));

    private void WebViewOnVietsubMediaResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs eventArgs)
    {
        if (!Uri.TryCreate(eventArgs.Request.Uri, UriKind.Absolute, out var requestUri)
            || !requestUri.Host.Equals(VietsubMediaPlaybackService.HostName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var errorCode = ReadVietsubResponseHeader(
            eventArgs.Response.Headers,
            "X-Vietsub-Error-Code");
        var correlationId = ReadVietsubResponseHeader(
            eventArgs.Response.Headers,
            "X-Vietsub-Correlation-Id");

        var resourceType = VietsubMediaPlaybackService.ClassifyResource(requestUri);
        _vietsubMediaLog.Write(
            correlationId,
            resourceType,
            eventArgs.Request.Method,
            eventArgs.Response.StatusCode,
            errorCode,
            "response_received");
    }

    private void WebViewOnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            ShowStartupError("Không thể tải giao diện dashboard.");
            return;
        }

        if (_webView is not null)
        {
            _webView.Visible = true;
        }

        if (_loadingPanel is not null)
        {
            _loadingPanel.Visible = false;
        }

        _refreshTimer.Start();
        if (_updateOptions?.Enabled == true)
        {
            _updateTimer.Start();
            _ = CheckForUpdateAsync();
        }
    }

    private async void UpdateTimerOnTick(object? sender, EventArgs eventArgs) =>
        await CheckForUpdateAsync();

    private async Task CheckForUpdateAsync()
    {
        if (_checkingUpdate || _applyingUpdate || _closing || _updateApiClient is null || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _checkingUpdate = true;
        try
        {
            var response = await _updateApiClient.CheckAsync(_shutdown.Token);
            if (!response.IsUpdateAvailable || response.Release is null)
            {
                _availableUpdate = null;
                PostHostMessage("update.none");
                return;
            }

            _availableUpdate = response;
            if (!response.IsMandatory && _dismissedReleaseId == response.Release.ReleaseId)
            {
                return;
            }

            PostHostMessage("update.available", response);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch
        {
            // Automatic checks are intentionally quiet. A later timer retries.
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    private async Task<bool> TryHandleUpdateMessageAsync(string json)
    {
        WebMessageRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<WebMessageRequest>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return false;
        }

        switch (request?.Type)
        {
            case "media.tools.install.prepare":
                await PrepareMediaToolRepairAsync();
                return true;
            case "media.tools.install":
                await RepairMediaToolsAsync();
                return true;
            case "update.dismiss":
                if (_availableUpdate is { IsMandatory: false, Release: not null })
                {
                    _dismissedReleaseId = _availableUpdate.Release.ReleaseId;
                }
                return true;
            case "update.exit":
                Close();
                return true;
            case "update.apply":
                await ApplyAvailableUpdateAsync();
                return true;
            default:
                return false;
        }
    }

    private async Task PrepareMediaToolRepairAsync()
    {
        if (_preparingMediaRepair || _applyingUpdate || _updateApiClient is null)
        {
            return;
        }

        _preparingMediaRepair = true;
        try
        {
            _mediaRepairRelease = await _updateApiClient.GetRepairReleaseAsync(_shutdown.Token);
            PostHostMessage("media.tools.install.available", _mediaRepairRelease);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _mediaRepairRelease = null;
            PostHostMessage("media.tools.install.failed", new { message = exception.Message });
        }
        finally
        {
            _preparingMediaRepair = false;
        }
    }

    private async Task RepairMediaToolsAsync()
    {
        if (_applyingUpdate || _updateApiClient is null || _packageUpdateService is null)
        {
            return;
        }

        _applyingUpdate = true;
        _updateTimer.Stop();
        try
        {
            var release = _mediaRepairRelease
                ?? await _updateApiClient.GetRepairReleaseAsync(_shutdown.Token);
            _mediaRepairRelease = null;
            var progress = new Progress<DesktopUpdateProgress>(update =>
                PostHostMessage("media.tools.install.progress", update));
            await _packageUpdateService.StartAsync(release, progress, _shutdown.Token);
            Close();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _applyingUpdate = false;
            if (_updateOptions?.Enabled == true)
            {
                _updateTimer.Start();
            }
            PostHostMessage("media.tools.install.failed", new { message = exception.Message });
        }
    }

    private async Task ApplyAvailableUpdateAsync()
    {
        if (_applyingUpdate || _availableUpdate?.Release is not { } release || _packageUpdateService is null)
        {
            return;
        }

        _applyingUpdate = true;
        _updateTimer.Stop();
        try
        {
            var progress = new Progress<DesktopUpdateProgress>(update => PostHostMessage("update.progress", update));
            await _packageUpdateService.StartAsync(release, progress, _shutdown.Token);
            Close();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _applyingUpdate = false;
            _updateTimer.Start();
            PostHostMessage("update.failed", new { message = exception.Message });
        }
    }

    private void PostHostMessage(string type, object? payload = null) =>
        PostJsonToWebView(JsonSerializer.Serialize(
            new WebMessageResponse(type, null, payload),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private async void RefreshTimerOnTick(object? sender, EventArgs eventArgs)
    {
        if (_refreshing || _bridge is null || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _refreshing = true;
        try
        {
            await _bridge.RefreshInBackgroundAsync(_shutdown.Token);
            _backgroundRefreshErrors.MarkSuccessful();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Expected while the form is closing.
        }
        catch (AccountClientException exception) when (exception.StatusCode == 401)
        {
            if (_sessionManager is not null)
            {
                await _sessionManager.InvalidateAsync(CancellationToken.None);
            }
        }
        catch (AccountClientException exception)
        {
            var response = _backgroundRefreshErrors.TryCreateResponse(exception.Code, exception.Message);
            if (response is not null)
            {
                PostJsonToWebView(JsonSerializer.Serialize(
                    response,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
        }
        catch (HttpRequestException)
        {
            // A later timer retries while the current offline lease remains valid.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void PostJsonToWebView(string json)
    {
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(() => PostJsonToWebView(json));
            }
            catch (ObjectDisposedException)
            {
                // The form closed before the queued WebView notification could be delivered.
            }
            catch (InvalidOperationException)
            {
                // The form handle was destroyed while a background job was reporting progress.
            }
            return;
        }

        if (_closing || IsDisposed || _webView?.CoreWebView2 is not { } coreWebView)
        {
            return;
        }

        coreWebView.PostWebMessageAsJson(json);
    }

    private string? SelectVietsubMediaFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn video nguồn cho dự án Vietsub",
            Filter = "Video được hỗ trợ|*.mp4;*.mkv;*.mov;*.webm|Tất cả tệp|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private string? SelectVietsubSrtFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Nhập phụ đề SRT vào dự án Vietsub",
            Filter = "Phụ đề SubRip (*.srt)|*.srt",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private string? SelectVietsubSrtDestination()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Xuất phụ đề SRT",
            Filter = "Phụ đề SubRip (*.srt)|*.srt",
            AddExtension = true,
            DefaultExt = "srt",
            OverwritePrompt = true,
            RestoreDirectory = true,
            FileName = "phu-de-tieng-viet.srt"
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private void CloseAfterLogout()
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(CloseAfterLogout);
            return;
        }

        ReturnToLoginRequested = true;
        Close();
    }

    private void SessionManagerOnInvalidated(string reason)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => ReturnToLogin(reason));
        }
        catch (InvalidOperationException)
        {
            // The form handle was destroyed while the invalidation event was being delivered.
        }
    }

    private void ReturnToLogin(string reason)
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        ReturnToLoginRequested = true;
        _refreshTimer.Stop();
        _updateTimer.Stop();
        MessageBox.Show(
            this,
            reason,
            "Phiên đăng nhập đã hết hạn",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        Close();
    }

    private void LicenseManagerOnInvalidated(string reason) =>
        PostHostMessage("license.invalidated", new { message = reason });

    private void ShowStartupError(string message)
    {
        _refreshTimer.Stop();
        _updateTimer.Stop();
        if (_loadingLabel is not null)
        {
            _loadingLabel.Text = $"Không thể khởi tạo giao diện.\n\n{message}";
            _loadingLabel.ForeColor = Color.Firebrick;
        }
    }

    private static bool IsAllowedTopLevelNavigation(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == "about" ||
         (parsed.Scheme == Uri.UriSchemeHttps &&
          parsed.Host.Equals(AppHostName, StringComparison.OrdinalIgnoreCase)));

    private static bool IsTrustedWebMessageSource(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var parsed) &&
        parsed.Scheme == Uri.UriSchemeHttps &&
        parsed.Host.Equals(AppHostName, StringComparison.OrdinalIgnoreCase);

    private void FormOnClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        _closing = true;
        _refreshTimer.Stop();
        _shutdown.Cancel();
        _bridge?.Dispose();
        _vietsubBridge?.Dispose();
        if (_licenseManager is not null)
        {
            _licenseManager.LicenseInvalidated -= LicenseManagerOnInvalidated;
        }
        if (_sessionManager is not null)
        {
            _sessionManager.SessionInvalidated -= SessionManagerOnInvalidated;
        }
        _refreshTimer.Dispose();
        _updateTimer.Dispose();
        _shutdown.Dispose();
    }
}
