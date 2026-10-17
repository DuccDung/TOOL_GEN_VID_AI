using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Translation;

namespace VideoMaker.Vietsub.Translation.Worker;

internal static class TranslationNativeRuntime
{
    private static readonly string[] Names = ["msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcomp140.dll"];
    // Native backend dependencies stay loaded for this isolated worker's lifetime.
    private static readonly List<IntPtr> Handles = [];

    public static void Verify()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Translation.MsvcRuntime")
            ?? throw new InvalidOperationException("Missing pinned MSVC definition.");
        using var definition = JsonDocument.Parse(resource);
        foreach (var entry in definition.RootElement.GetProperty("files").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || new FileInfo(path).Length != entry.GetProperty("size").GetInt64())
                throw Failure();
            using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.GetProperty("sha256").GetString(),
                StringComparison.OrdinalIgnoreCase)) throw Failure();
        }
    }

    public static void LoadPinnedDependencies()
    {
        // Preload before LLamaSharp's restricted DLL search can select System32.
        foreach (var name in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll" })
        {
            var handle = LoadLibraryExW(Path.Combine(AppContext.BaseDirectory, name), IntPtr.Zero,
                0x00000100 | 0x00001000); // DLL directory + default safe directories.
            if (handle == IntPtr.Zero) throw Failure();
            Handles.Add(handle);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

    public static void VerifyLoadedModules()
    {
        using var process = Process.GetCurrentProcess();
        var modules = process.Modules.Cast<ProcessModule>().ToArray();
        foreach (var name in Names)
        {
            var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, name));
            if (!modules.Any(module => module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase)
                && module.FileName.Equals(expected, StringComparison.OrdinalIgnoreCase)))
                throw new TranslationWorkerException(VietsubTranslationErrorCodes.BackendLoadFailed,
                    "Worker chưa nạp đúng runtime Visual C++ đi kèm; hãy sửa bộ ứng dụng.", false);
        }
    }

    private static TranslationWorkerException Failure() => new(VietsubTranslationErrorCodes.BackendLoadFailed,
        "Runtime Visual C++ của worker thiếu hoặc sai checksum; hãy sửa bộ ứng dụng.", false);
}
