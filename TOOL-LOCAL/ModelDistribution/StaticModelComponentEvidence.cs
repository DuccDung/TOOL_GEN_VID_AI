using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Web.WebView2.Core;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_LOCAL.ModelDistribution;

// These digests identify evidence collected after a successful native probe. They
// never replace that probe, the approved artifact hashes, or the startup gates.
internal static class StaticModelComponentEvidence
{
    internal static string WebView2()
    {
        var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
        if (string.IsNullOrWhiteSpace(version) || version.Length > 256)
            throw new InvalidDataException("Missing WebView2 probe identity.");
        return DigestFiles("webview2", AppContext.BaseDirectory,
            [DesktopWebViewRuntime.LoaderRelativePath], [version, SystemSetupPaths.MachineFingerprint]);
    }

    internal static string Media() => DigestFiles("media", AppContext.BaseDirectory,
        ["tools/ffmpeg/ffmpeg.exe", "tools/ffmpeg/ffprobe.exe"]);

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Embedded OCR assemblies have no Location; their containing application executable is hashed below.")]
    internal static string Ocr()
    {
        if (OcrNativeDependencies.InspectLoadedModules().Any(x => !x.AppLocal))
            throw new InvalidDataException("OCR probe used a native dependency outside the package.");
        var root = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var process = Process.GetCurrentProcess();
        var loaded = process.Modules.Cast<ProcessModule>().Select(x => Path.GetFullPath(x.FileName))
            .Where(x => x.StartsWith(root, StringComparison.OrdinalIgnoreCase) && x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(x => Path.GetRelativePath(root, x));
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name is { } name
            && (name.StartsWith("Sdcb.Paddle", StringComparison.Ordinal) || name.StartsWith("OpenCvSharp", StringComparison.Ordinal)))
            .OrderBy(x => x.FullName, StringComparer.Ordinal).ToArray();
        var assemblyFiles = assemblies.Select(x => x.Location).Where(x => !string.IsNullOrWhiteSpace(x)
            && Path.GetFullPath(x).StartsWith(root, StringComparison.OrdinalIgnoreCase)).Select(x => Path.GetRelativePath(root, x));
        var files = new[] { "setup-fixtures/en.png", "setup-fixtures/zh.png", "TOOL-LOCAL.exe" }
            .Concat(OcrNativeDependencies.Files.Select(x => x.Name)).Concat(loaded).Concat(assemblyFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var claims = assemblies.Select(x => x.FullName + "|" + x.ManifestModule.ModuleVersionId)
            .Prepend("fixtures:en,zh").Prepend(SystemSetupPaths.MachineFingerprint);
        return DigestFiles("ocr", root, files, claims);
    }

    internal static string Qwen(string profileId)
    {
        var definition = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        var directory = Path.Combine(StaticModelRuntimePolicy.TranslationRoot, definition.ComponentId, definition.EngineVersion);
        var worker = VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(Path.Combine(AppContext.BaseDirectory, "_translation_worker"));
        if (string.IsNullOrWhiteSpace(worker)) throw new InvalidDataException("Missing translation worker evidence.");
        // GetRuntimeStatus already verified the GGUF hash and the machine/build/profile
        // bound probe marker. Hash that verified marker and worker identity here.
        return DigestFiles("qwen", directory, ["probe.json"],
            [definition.ModelSha256, worker, profileId, SystemSetupPaths.MachineFingerprint]);
    }

    internal static string Piper(string runtimeRoot)
    {
        var definition = PiperOfflineBundle.ApprovedDefinition();
        var worker = DigestFiles("piper-worker", AppContext.BaseDirectory,
            ["workers/piper_worker.py", "workers/piper-requirements.lock"]);
        return DigestFiles("piper", runtimeRoot, [".ready.json", ".venv/pyvenv.cfg"],
            [definition.ArchiveSha256, definition.ManifestSha256, worker, SystemSetupPaths.MachineFingerprint]);
    }

    internal static string DigestFiles(string componentId, string root, IEnumerable<string> files, IEnumerable<string>? claims = null)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var paths = files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length is < 1 or > 512) throw new InvalidDataException("Incomplete component evidence.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        Append("VideoMaker.StaticModelEvidence.v1");
        Append(componentId);
        foreach (var claim in claims ?? []) Append(claim);
        foreach (var relative in paths)
        {
            var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Component evidence escaped its root.");
            StaticModelRuntimePolicy.AssertSafeFile(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            if (stream.Length == 0) throw new InvalidDataException("Empty component evidence.");
            Append(Path.GetRelativePath(fullRoot, path).Replace('\\', '/').ToLowerInvariant());
            Append(stream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
