using System.Diagnostics;
using System.Runtime.InteropServices;
using TOOL_LOCAL.Authentication;

namespace TOOL_LOCAL.SystemSetup;

internal static class DesktopPrerequisites
{
    internal const string WebViewDownloadPage = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download";
    public static bool EnsureAvailable()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 || !Environment.Is64BitProcess)
        {
            MessageBox.Show("Bản taphoatool này yêu cầu Windows x64 để chạy OCR, Qwen và Piper.",
                "Hệ thống chưa phù hợp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var status = DesktopWebViewRuntime.Inspect();
        if (status.IsReady) return true;
        using var form = new Form { Icon = BrandIdentity.WindowIcon, Text = "Chuẩn bị giao diện taphoatool", StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(580, 210), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var text = new Label { Left = 24, Top = 22, Width = 530, Height = 100,
            Text = status.Message };
        var install = new Button { Text = "Mở trang cài WebView2", Left = 24, Top = 140, Width = 210, Height = 40 };
        install.Enabled = status.ErrorCode is "webview2_runtime_missing" or "webview2_initialization_failed";
        var check = new Button { Text = "Kiểm tra lại", Left = 248, Top = 140, Width = 140, Height = 40 };
        var close = new Button { Text = "Đóng", Left = 402, Top = 140, Width = 140, Height = 40, DialogResult = DialogResult.Cancel };
        install.Click += (_, _) => {
            try { Process.Start(new ProcessStartInfo(WebViewDownloadPage) { UseShellExecute = true }); }
            catch { text.Text = "Không mở được trình duyệt. Hãy cài Microsoft Edge WebView2 Runtime từ trang Microsoft rồi kiểm tra lại."; }
        };
        check.Click += (_, _) => {
            status = DesktopWebViewRuntime.Inspect();
            if (status.IsReady) { form.DialogResult = DialogResult.OK; form.Close(); }
            else
            {
                text.Text = status.Message;
                install.Enabled = status.ErrorCode is "webview2_runtime_missing" or "webview2_initialization_failed";
            }
        };
        form.Controls.AddRange([text, install, check, close]);
        return form.ShowDialog() == DialogResult.OK;
    }
}
