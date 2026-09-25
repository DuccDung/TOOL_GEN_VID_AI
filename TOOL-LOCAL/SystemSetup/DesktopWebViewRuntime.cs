using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace TOOL_LOCAL.SystemSetup;

internal sealed record DesktopWebViewStatus(string State, string? ErrorCode, string Message)
{
    public bool IsReady => State == "READY";
}

internal static class DesktopWebViewRuntime
{
    internal const string LoaderRelativePath = "runtimes/win-x64/native/WebView2Loader.dll";
    private static readonly object LoaderLock = new();
    private static bool _loaderConfigured;

    public static DesktopWebViewStatus Inspect() => Inspect(
        AppContext.BaseDirectory, ConfigureLoader,
        () => CoreWebView2Environment.GetAvailableBrowserVersionString());

    internal static DesktopWebViewStatus Inspect(
        string applicationDirectory, Action<string> configureLoader, Func<string?> getBrowserVersion)
    {
        try
        {
            var loaderPath = Path.GetFullPath(Path.Combine(applicationDirectory, LoaderRelativePath));
            if (!File.Exists(loaderPath))
                return Failure("webview2_loader_missing",
                    "Gói ứng dụng thiếu WebView2Loader.dll. Hãy giải nén lại đầy đủ gói hoặc cài lại ứng dụng.");

            // Reject damaged/wrong-architecture bundles before invoking native code. Never
            // recover by searching PATH, the current directory or another installed app.
            using (var stream = File.OpenRead(loaderPath))
            using (var reader = new PEReader(stream))
            {
                if (reader.PEHeaders.CoffHeader.Machine != Machine.Amd64 ||
                    !reader.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll))
                    return InvalidLoader();
            }

            configureLoader(Path.GetDirectoryName(loaderPath)!);
            return string.IsNullOrWhiteSpace(getBrowserVersion())
                ? MissingRuntime()
                : new("READY", null, "WebView2 đã sẵn sàng.");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return MissingRuntime();
        }
        catch (BadImageFormatException)
        {
            return InvalidLoader();
        }
        catch (DllNotFoundException)
        {
            return Failure("webview2_loader_load_failed",
                "Không nạp được thư viện WebView2 của gói ứng dụng. Hãy giải nén lại đầy đủ gói hoặc cài lại ứng dụng.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      COMException or InvalidOperationException or ArgumentException)
        {
            return Failure("webview2_initialization_failed",
                "Không kiểm tra được WebView2. Hãy kiểm tra quyền đọc gói ứng dụng và sửa hoặc cài lại Microsoft Edge WebView2 Runtime.");
        }
    }

    private static void ConfigureLoader(string directory)
    {
        lock (LoaderLock)
        {
            if (_loaderConfigured) return;
            // Set before any other WebView2 API. An absolute application-owned path also
            // works when managed assemblies are embedded by PublishSingleFile.
            CoreWebView2Environment.SetLoaderDllFolderPath(directory);
            _loaderConfigured = true;
        }
    }

    private static DesktopWebViewStatus MissingRuntime() => new("NOT_INSTALLED", "webview2_runtime_missing",
        "Máy này chưa có Microsoft Edge WebView2 Runtime. Mở trang Microsoft, cài Evergreen Runtime x64 rồi bấm Kiểm tra lại.");

    private static DesktopWebViewStatus InvalidLoader() => Failure("webview2_loader_invalid",
        "WebView2Loader.dll bị hỏng hoặc không đúng bản Windows x64. Hãy giải nén lại đầy đủ gói hoặc cài lại ứng dụng.");

    private static DesktopWebViewStatus Failure(string code, string message) => new("CHECK_FAILED", code, message);
}
