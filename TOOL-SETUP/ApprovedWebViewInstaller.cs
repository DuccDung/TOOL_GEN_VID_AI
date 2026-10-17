using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TOOL_SETUP;

internal static class ApprovedWebViewInstaller
{
    internal const string EmbeddedInstallerResource = "VideoMaker.ApprovedWebView2Installer";

    internal static async Task InstallAsync(string stageRoot, InstallerOptions options, CancellationToken token,
        string? privatePrerequisiteRoot = null)
    {
        var path = StaticSetupTransaction.SafePath(stageRoot, "prerequisites/webview2-runtime-x64.exe");
        StaticSetupTransaction.AssertSafeFile(path);
        ValidatePin(options);
        if (!File.Exists(path) && options.IsSharedBootstrapper && privatePrerequisiteRoot is not null)
        {
            await using var embedded = typeof(ApprovedWebViewInstaller).Assembly.GetManifestResourceStream(EmbeddedInstallerResource);
            if (embedded is not null)
                path = await PrepareEmbeddedInstallerAsync(embedded, privatePrerequisiteRoot, options, token);
        }
        if (!File.Exists(path))
            throw new InvalidOperationException("Thiếu Microsoft Edge WebView2. Bản Setup này chưa kèm installer đã duyệt; hãy cài runtime chính thức rồi thử lại.");
        ValidateInstallerFile(path, options);
        VerifyAuthenticode(path, options.WebView2PublisherThumbprint);
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)! };
        start.ArgumentList.Add("/silent"); start.ArgumentList.Add("/install");
        // Evergreen installer supports installation for the invoking user. Setup itself is never elevated.
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Không khởi động được installer WebView2.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            throw new TimeoutException("Cài WebView2 quá thời gian. Hãy kiểm tra runtime rồi thử lại.");
        }
        if (process.ExitCode == 3010) throw new InvalidOperationException("WebView2 yêu cầu khởi động lại Windows. Sau khi khởi động lại, mở Setup để tiếp tục.");
        if (process.ExitCode != 0) throw new InvalidOperationException($"WebView2 chưa cài thành công (mã {process.ExitCode}). Hãy kiểm tra rồi thử lại.");
    }

    internal static async Task<string> PrepareEmbeddedInstallerAsync(Stream embedded, string privateRoot,
        InstallerOptions options, CancellationToken token)
    {
        ValidatePin(options);
        StaticSetupTransaction.EnsureSafeDirectory(privateRoot);
        var path = StaticSetupTransaction.SafePath(privateRoot, "webview2-runtime-x64.exe");
        StaticSetupTransaction.AssertSafeFile(path);
        if (File.Exists(path))
        {
            // A failed/corrupt cache is not silently replaced or executed.
            ValidateInstallerFile(path, options);
            return path;
        }
        LauncherInstallerService.EnsureDiskBudget([(privateRoot, checked(options.WebView2InstallerSizeBytes + 64L * 1024 * 1024))]);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[128 * 1024];
                long written = 0;
                while (true)
                {
                    var read = await embedded.ReadAsync(buffer, token);
                    if (read == 0) break;
                    if (written + read > options.WebView2InstallerSizeBytes)
                        throw new InvalidDataException("Installer WebView2 nhúng vượt kích thước đã duyệt.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    hash.AppendData(buffer, 0, read);
                    written += read;
                }
                await output.FlushAsync(token);
                output.Flush(true);
                if (written != options.WebView2InstallerSizeBytes ||
                    !Convert.ToHexString(hash.GetHashAndReset()).Equals(options.WebView2InstallerSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer WebView2 nhúng không khớp checksum đã duyệt.");
            }
            token.ThrowIfCancellationRequested();
            StaticSetupTransaction.AssertSafeFile(path);
            File.Move(temporary, path);
            return path;
        }
        finally { StaticSetupTransaction.DeleteOwnedFile(temporary); }
    }

    private static void ValidatePin(InstallerOptions options)
    {
        if (options.WebView2InstallerSizeBytes is <= 0 or > 512L * 1024 * 1024 ||
            options.WebView2InstallerSha256 is not { Length: 64 } || options.WebView2InstallerSha256.Any(c => !Uri.IsHexDigit(c)) ||
            options.WebView2PublisherThumbprint is not { Length: 40 } || options.WebView2PublisherThumbprint.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Bản Setup này thiếu thông tin WebView2 đã duyệt. Hãy tải Setup hiện hành hoặc cài runtime chính thức rồi thử lại.");
    }

    private static void ValidateInstallerFile(string path, InstallerOptions options)
    {
        StaticSetupTransaction.AssertSafeFile(path);
        if (new FileInfo(path).Length != options.WebView2InstallerSizeBytes ||
            !StaticSetupTransaction.HashFile(path).Equals(options.WebView2InstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installer WebView2 không khớp bản đã duyệt.");
    }

    private static void VerifyAuthenticode(string path, string thumbprint)
    {
#pragma warning disable SYSLIB0057
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        if (!certificate.Thumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase) ||
            !certificate.GetNameInfo(X509NameType.SimpleName, false).Equals("Microsoft Corporation", StringComparison.Ordinal))
            throw new InvalidDataException("Chữ ký WebView2 không thuộc nhà phát hành Microsoft đã duyệt.");
        var file = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData { Size = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = 2, RevocationChecks = 1,
                UnionChoice = 1, FileInfo = filePointer, StateAction = 1, ProviderFlags = 0x80 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try
            {
                if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0)
                    throw new InvalidDataException("Windows không xác minh được chữ ký tin cậy của installer WebView2.");
            }
            finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        }
        finally { Marshal.DestroyStructure<WinTrustFileInfo>(filePointer); Marshal.FreeHGlobal(filePointer); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustFileInfo
    { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string FilePath; public IntPtr FileHandle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustData
    {
        public uint Size; public IntPtr PolicyCallbackData, SipClientData; public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr FileInfo; public uint StateAction; public IntPtr StateData; public IntPtr UrlReference; public uint ProviderFlags, UiContext; public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);
}
