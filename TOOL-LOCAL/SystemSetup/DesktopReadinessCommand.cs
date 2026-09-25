using System.Runtime.InteropServices;
using System.Text.Json;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_LOCAL.SystemSetup;

// Maintenance only. Never authenticates, connects to SQL, downloads components or opens a project.
internal static class DesktopReadinessCommand
{
    internal sealed record ComponentResult(string Id, string State, string? ErrorCode);

    public static bool Matches(string[] args) => args is ["--check-desktop"] or ["--check-webview2"];

    public static async Task<int> RunAsync(bool webViewOnly = false)
    {
        try
        {
            var windowsX64 = OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64 && Environment.Is64BitProcess;
            if (!windowsX64)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { LocalComponentsReady = false, ErrorCode = "desktop_platform_unsupported" }));
                return 2;
            }
            var webView = DesktopWebViewRuntime.Inspect();
            if (webViewOnly)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    WindowsX64 = windowsX64,
                    WebView2 = webView,
                    ServerAccessChecked = false
                }));
                return webView.IsReady ? 0 : 2;
            }
            var options = DesktopOptions.Load(AppContext.BaseDirectory, DesktopUserSettingsStore.PreferencesDirectory, requireDatabase: false);
            var mediaPaths = new MediaToolPathResolver(options.MediaTools).Resolve();
            var media = new MediaToolPreflightService(mediaPaths, new ExternalProcessRunner(), TimeProvider.System);
            var ocrEnabled = options.Features.VietsubEnabled && options.Features.VietsubOcrEnabled;
            await using var ocr = ocrEnabled ? new PaddleVietsubOcrRecognizer() : null;
            await using var qwen = DesktopComponentComposition.CreateTranslationProvider(options.Features,
                () => new QwenGgufVietsubTranslationProvider(new VietsubTranslationComponentStore(VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km)));
            var voiceEnabled = options.Features.VietsubEnabled && options.Features.VietsubLocalVoiceEnabled;
            using var voice = voiceEnabled ? DesktopComponentComposition.CreateVoiceComponents(
                new VietsubAppPaths(options.Storage.WorkspaceRoot), options.Features) : null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var components = await InspectAsync([
                new MediaSetupAdapter(media, mediaPaths),
                new OcrSetupAdapter(ocr, ocrEnabled),
                new QwenSetupAdapter(qwen),
                new PiperSetupAdapter(voice, voiceEnabled)
            ], timeout.Token);
            var ready = webView.IsReady && components.All(x => x.State is "READY" or "DISABLED");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                AppVersion = typeof(DesktopReadinessCommand).Assembly.GetName().Version?.ToString(),
                WindowsX64 = windowsX64,
                WebView2Installed = webView.IsReady,
                WebView2 = webView,
                LocalComponentsReady = ready,
                WorkflowAccess = "DIRECT_SQL_REQUIRED_NOT_CHECKED",
                ServerAccessChecked = false,
                OptionalVoiceEnginesChecked = false,
                Components = components
            }));
            return ready ? 0 : 2;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            // Deliberately omit exception text: paths, configuration and native stderr can be sensitive.
            Console.Error.WriteLine("{\"LocalComponentsReady\":false,\"ErrorCode\":\"desktop_diagnostics_failed\"}");
            return 1;
        }
    }

    internal static async Task<IReadOnlyList<ComponentResult>> InspectAsync(
        IEnumerable<ISetupComponentAdapter> adapters, CancellationToken token)
    {
        var results = new List<ComponentResult>();
        foreach (var adapter in adapters)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                // Model inspectors only inspect installed evidence. Inference/download are explicitly
                // left to System Setup; OCR and FFmpeg probe their bundled, synthetic fixtures here.
                var component = adapter is ISetupComponentStatusInspector inspector
                    ? inspector.Inspect()
                    : await adapter.RunAsync(false, false, (_, _, _, _) => { }, token);
                results.Add(new(component.Id, component.State, component.ErrorCode));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            {
                results.Add(new(adapter.Component.Id, "CHECK_FAILED", "component_check_failed"));
            }
        }
        return results;
    }
}
