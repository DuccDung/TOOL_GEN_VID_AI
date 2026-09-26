using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal static class OcrNativeDependencies
{
    internal const string Missing = "OCR_NATIVE_DEPENDENCY_MISSING";
    internal const string Invalid = "OCR_NATIVE_DEPENDENCY_INVALID";
    internal const string MediaMissing = "OCR_WINDOWS_MEDIA_MISSING";
    internal sealed record RuntimeFile(string Name, long Size, string Sha256);
    private sealed record Definition(RuntimeFile[] Files);
    internal sealed record LoadedModule(string Name, bool AppLocal);

    // The trust anchor is compiled into the app, never read from a user-editable sidecar.
    internal static IReadOnlyList<RuntimeFile> Files { get; } = ReadDefinition();

    private static RuntimeFile[] ReadDefinition()
    {
        using var stream = typeof(OcrNativeDependencies).Assembly.GetManifestResourceStream("Ocr.MsvcRuntime")
            ?? throw new InvalidOperationException("Missing embedded OCR runtime definition.");
        return JsonSerializer.Deserialize<Definition>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Files;
    }

    internal static string? CheckFiles(string directory)
    {
        foreach (var entry in Files)
        {
            var path = Path.Combine(directory, entry.Name);
            if (!File.Exists(path)) return Missing;
            try
            {
                var file = new FileInfo(path);
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Length != entry.Size) return Invalid;
                using var stream = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) return Invalid;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Invalid; }
        }
        return null;
    }

    internal static string? CheckWindowsMedia()
    {
        // Check only Windows' own directory, never redistribute or load substitute system DLLs.
        foreach (var name in new[] { "MFPlat.dll", "MF.dll", "MFReadWrite.dll" })
        {
            if (!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, name), out var handle)) return MediaMissing;
            NativeLibrary.Free(handle);
        }
        return null;
    }

    internal static IReadOnlyList<LoadedModule> InspectLoadedModules()
    {
        using var process = Process.GetCurrentProcess();
        var modules = process.Modules.Cast<ProcessModule>().ToArray();
        return Files.Select(entry => new LoadedModule(entry.Name, modules.Any(module =>
            module.ModuleName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFullPath(module.FileName).Equals(Path.Combine(AppContext.BaseDirectory, entry.Name), StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }
}
