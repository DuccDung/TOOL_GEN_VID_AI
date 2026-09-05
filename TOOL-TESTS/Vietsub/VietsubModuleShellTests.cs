using System.Text.Json;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Vietsub;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubModuleShellTests
{
    [Fact]
    public async Task Bridge_HandlesOnlyPrefixedMessages_AndReturnsIndependentState()
    {
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(enabled: true, responses.Add);

        var unrelatedHandled = await bridge.TryHandleAsync(
            """{"type":"dashboard.refresh","requestId":"dashboard-1","payload":{}}""");
        var vietsubHandled = await bridge.TryHandleAsync(
            """{"type":"vietsub.state.get","requestId":"vietsub-1","payload":{}}""");

        Assert.False(unrelatedHandled);
        Assert.True(vietsubHandled);
        var response = ParseSingleResponse(responses);
        Assert.Equal("vietsub.state", response.RootElement.GetProperty("type").GetString());
        Assert.Equal("vietsub-1", response.RootElement.GetProperty("requestId").GetString());
        Assert.True(response.RootElement.GetProperty("payload").GetProperty("enabled").GetBoolean());
        Assert.False(response.RootElement.GetProperty("payload").GetProperty("busy").GetBoolean());
        Assert.Equal(
            "shell_ready",
            response.RootElement.GetProperty("payload").GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Bridge_RejectsDisabledFeatureWithStableErrorCode()
    {
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(enabled: false, responses.Add);

        var handled = await bridge.TryHandleAsync(
            """{"type":"vietsub.state.get","requestId":"vietsub-disabled","payload":{}}""");

        Assert.True(handled);
        var response = ParseSingleResponse(responses);
        Assert.Equal("vietsub.error", response.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "vietsub_feature_disabled",
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Bridge_RequiresCorrelationIdForEveryVietsubMessage()
    {
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(enabled: true, responses.Add);

        var handled = await bridge.TryHandleAsync(
            """{"type":"vietsub.state.get","payload":{}}""");

        Assert.True(handled);
        var response = ParseSingleResponse(responses);
        Assert.Equal(
            "vietsub_request_id_required",
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Bridge_CancelsOnlyItsOwnActiveOperation()
    {
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(enabled: true, responses.Add);
        var operationToken = bridge.BeginOperation("vietsub-operation-1", CancellationToken.None);

        var handled = await bridge.TryHandleAsync(
            """{"type":"vietsub.operation.cancel","requestId":"cancel-1","payload":{}}""");

        Assert.True(handled);
        Assert.True(operationToken.IsCancellationRequested);
        Assert.Equal(2, responses.Count);
        using var cancelledResponse = JsonDocument.Parse(responses[0]);
        Assert.Equal(
            "vietsub.operation.cancelled",
            cancelledResponse.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "vietsub-operation-1",
            cancelledResponse.RootElement.GetProperty("payload").GetProperty("cancelledRequestId").GetString());

        bridge.CompleteOperation("vietsub-operation-1");
    }

    [Fact]
    public void DesktopFeatureFlag_CanBeOverriddenWithoutChangingOtherSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-vietsub-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), """
                {
                  "Server": { "BaseUrl": "https://localhost:7202/" },
                  "Database": { "ConnectionString": "Server=test;Database=test" },
                  "Storage": { "WorkspaceRoot": "workspace" },
                  "Update": {
                    "Enabled": true,
                    "Channel": "Stable",
                    "Platform": "win-x64",
                    "CheckIntervalSeconds": 120
                  },
                  "Features": { "VietsubEnabled": false, "VietsubOcrEnabled": false }
                }
                """);
            File.WriteAllText(Path.Combine(root, "appsettings.user.json"), """
                { "Features": { "VietsubEnabled": true, "VietsubOcrEnabled": false } }
                """);

            var options = DesktopOptions.Load(root);

            Assert.True(options.Features.VietsubEnabled);
            Assert.False(options.Features.VietsubOcrEnabled);
            Assert.Equal("https://localhost:7202/", options.Server.BaseUrl);
            Assert.Equal("Stable", options.Update.Channel);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UiAndHost_KeepVietsubBehindFeatureAndBridgeBoundaries()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var hook = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "useVietsubModule.ts");
        var form = ReadRepositoryFile("TOOL-LOCAL", "Form1.cs");
        var dashboardBridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");

        Assert.Contains("feature: 'vietsubEnabled'", app);
        Assert.Contains("dashboard.features[feature]", app);
        Assert.Contains("page === 'vietsub'", app);
        Assert.Contains("dashboard.selectedOrganizationId", app);
        Assert.Contains("postToHost('vietsub.state.get')", hook);
        Assert.Contains("_vietsubBridge.TryHandleAsync", form);
        Assert.DoesNotContain("case \"vietsub.", dashboardBridge, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_SeparatesProjectLibraryFromTheActiveEditorWorkspace()
    {
        var page = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "VietsubPage.tsx");
        var library = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "VietsubProjectLibrary.tsx");
        var editor = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "VietsubEditorWorkspace.tsx");
        var preview = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "VietsubPreviewPanel.tsx");
        var timeline = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "VietsubTimeline.tsx");
        var hook = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "useVietsubModule.ts");
        var bridge = ReadRepositoryFile(
            "TOOL-LOCAL", "Vietsub", "VietsubWebBridge.cs");
        var package = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "package.json");

        Assert.Contains("state.selectedProject ?", page);
        Assert.Contains("<VietsubEditorWorkspace", page);
        Assert.Contains("<VietsubProjectLibrary", page);
        Assert.Contains("onCreateProject", library);
        Assert.Contains("<VietsubSettingsPanel", editor);
        Assert.Contains("<VietsubPreviewPanel", editor);
        Assert.Contains("<VietsubTimeline", editor);
        Assert.Contains("event.code === 'Space'", editor);
        Assert.Contains("selectedCueId", editor);
        Assert.Contains("onToggleSubtitles", preview);
        Assert.Contains("playbackRate", preview);
        Assert.Contains("onSelectCue", timeline);
        Assert.Contains("expectedTrackRevision", timeline);
        Assert.Contains("calculateViewportRange", timeline);
        Assert.Contains("timelineThumbnails", timeline);
        Assert.Contains("waveformUrl", timeline);
        Assert.Contains("Voice gốc", timeline);
        Assert.Contains("cue.previewText", timeline);
        Assert.Contains("vietsub-timeline-grid", timeline);
        Assert.Contains("flushPendingEdits", editor);
        Assert.Contains("onRegisterBeforeLeave", editor);
        Assert.Contains("vietsub.timeline.window.get", hook);
        Assert.Contains("vietsub.timeline.cue.update", bridge);
        Assert.Contains("\"test\": \"vitest run\"", package);
        Assert.Contains("keepsCurrentEditor", hook);
        Assert.Contains("invalidatesEditor", hook);
    }

    [Fact]
    public void Timeline_updates_media_viewport_before_requiring_an_active_subtitle_track()
    {
        var timeline = ReadRepositoryFile(
            "TOOL-LOCAL", "Web", "src", "features", "vietsub", "VietsubTimeline.tsx");
        var methodStart = timeline.IndexOf(
            "const requestVisibleWindow = useCallback", StringComparison.Ordinal);
        var methodEnd = timeline.IndexOf(
            "const scheduleWindowRequest = useCallback", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = timeline[methodStart..methodEnd];
        var mediaGuard = method.IndexOf(
            "if (!viewport || durationMilliseconds <= 0) return;", StringComparison.Ordinal);
        var visibleRangeUpdate = method.IndexOf("setVisibleRange(range);", StringComparison.Ordinal);
        var subtitleTrackGuard = method.IndexOf("if (!trackId) return;", StringComparison.Ordinal);
        var subtitleWindowRequest = method.IndexOf("onLoadWindow({", StringComparison.Ordinal);

        Assert.True(mediaGuard >= 0);
        Assert.True(visibleRangeUpdate > mediaGuard);
        Assert.True(subtitleTrackGuard > visibleRangeUpdate);
        Assert.True(subtitleWindowRequest > subtitleTrackGuard);
        Assert.DoesNotContain("!viewport || !trackId", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_MarshalsBackgroundNotificationsBeforeAccessingCoreWebView()
    {
        var form = ReadRepositoryFile("TOOL-LOCAL", "Form1.cs");
        var methodStart = form.IndexOf("private void PostJsonToWebView", StringComparison.Ordinal);
        var methodEnd = form.IndexOf("private string? SelectVietsubMediaFile", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = form[methodStart..methodEnd];
        var marshalCheck = method.IndexOf("if (InvokeRequired)", StringComparison.Ordinal);
        var webViewAccess = method.IndexOf("_webView?.CoreWebView2", StringComparison.Ordinal);

        Assert.True(marshalCheck >= 0);
        Assert.True(webViewAccess > marshalCheck);
        Assert.Contains("BeginInvoke(() => PostJsonToWebView(json))", method);
    }

    [Fact]
    public void Host_registers_media_filter_before_navigation_and_assigns_typed_response_synchronously()
    {
        var form = ReadRepositoryFile("TOOL-LOCAL", "Form1.cs");
        var configureCall = form.IndexOf("ConfigureWebViewSecurity(", StringComparison.Ordinal);
        var firstNavigation = form.IndexOf(".Navigate(", configureCall, StringComparison.Ordinal);
        var configureMethodStart = form.IndexOf("private void ConfigureWebViewSecurity", StringComparison.Ordinal);
        var configureMethodEnd = form.IndexOf("private async void WebViewOnWebMessageReceived", configureMethodStart, StringComparison.Ordinal);
        var handlerStart = form.IndexOf("private void WebViewOnVietsubMediaRequested", StringComparison.Ordinal);
        var handlerEnd = form.IndexOf("private void WebViewOnNavigationCompleted", handlerStart, StringComparison.Ordinal);

        Assert.True(configureCall >= 0);
        Assert.True(firstNavigation > configureCall);
        Assert.True(configureMethodStart >= 0 && configureMethodEnd > configureMethodStart);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);

        var configureMethod = form[configureMethodStart..configureMethodEnd];
        var handler = form[handlerStart..handlerEnd];
        Assert.Contains("AddWebResourceRequestedFilter(", configureMethod);
        Assert.Contains("CoreWebView2WebResourceContext.All", configureMethod);
        Assert.Contains("CoreWebView2WebResourceRequestSourceKinds.All", configureMethod);
        Assert.Contains("WebResourceRequested += WebViewOnVietsubMediaRequested", configureMethod);
        Assert.Contains("WebResourceResponseReceived += WebViewOnVietsubMediaResponseReceived", configureMethod);
        Assert.Contains("eventArgs.Response =", handler);
        Assert.Contains("CreateVietsubWebResourceResponse", handler);
        Assert.Contains("response.StatusCode", handler);
        Assert.Contains("response.ReasonPhrase", handler);
        Assert.Contains("response.Headers", handler);
        Assert.Contains("response.Content", handler);
        Assert.Contains("response.ResourceType", handler);
        Assert.Contains("response.ErrorCode", handler);
        Assert.Contains("_vietsubMediaLog.Write", handler);
        Assert.Contains("PostVietsubMediaFailure", handler);
        Assert.Contains("ReadVietsubRangeHeader", handler);
        Assert.Contains("VietsubPlaybackResourceTypes.Video", handler);
        Assert.Contains("headers.Contains(\"Range\")", handler);
        Assert.Contains("exception is ArgumentException or COMException", handler);
        Assert.Contains("ReadVietsubResponseHeader", handler);
        Assert.DoesNotContain("eventArgs.Request.Uri,\n            correlationId", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("404,\n                \"Not Found\"", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_versions_dashboard_navigation_and_build_removes_stale_asset_hashes()
    {
        var form = ReadRepositoryFile("TOOL-LOCAL", "Form1.cs");
        var project = ReadRepositoryFile("TOOL-LOCAL", "TOOL-LOCAL.csproj");

        Assert.Contains("File.GetLastWriteTimeUtc(indexPath).Ticks", form, StringComparison.Ordinal);
        Assert.Contains("index.html?v={webVersion}", form, StringComparison.Ordinal);
        Assert.Contains("<RemoveDir Directories=\"$(TargetDir)wwwroot\\assets\" />", project, StringComparison.Ordinal);
        Assert.Contains("CleanDashboardPublishAssets", project, StringComparison.Ordinal);
        Assert.Contains("<RemoveDir Directories=\"$(PublishDir)wwwroot\\assets\" />", project, StringComparison.Ordinal);
    }

    private static JsonDocument ParseSingleResponse(IReadOnlyCollection<string> responses)
    {
        var response = Assert.Single(responses);
        return JsonDocument.Parse(response);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
