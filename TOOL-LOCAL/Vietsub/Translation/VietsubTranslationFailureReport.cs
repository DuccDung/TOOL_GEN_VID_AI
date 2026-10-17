using System.Text.Json;
using TOOL_LOCAL.SystemSetup;

namespace TOOL_LOCAL.Vietsub.Translation;

// Deliberately excludes prompts, native stderr, exception messages, paths and account data.
internal sealed record VietsubTranslationFailureReport(
    string DiagnosticId, DateTime OccurredAtUtc, string ErrorCode, string Operation, string Stage,
    string WorkerVersion, string? WorkerFingerprint, string? RuntimeVersion,
    int? ProcessId, int? ExitCode, string? ResourceProfileId, string? ConfigFingerprint, string? Backend,
    VietsubTranslationMemorySnapshot? MemoryBefore, VietsubTranslationMemorySnapshot? MemoryAfter)
{
    public int SchemaVersion => 1;
    public string? ExitCodeHex => ExitCode is { } code ? $"0x{unchecked((uint)code):X8}" : null;
}

internal static class VietsubTranslationFailureReports
{
    internal const int MaximumReports = 20;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string DefaultDirectory => Path.Combine(SystemSetupPaths.MetadataRoot, "qwen-diagnostics");

    public static bool TryWrite(VietsubTranslationFailureReport report, string? directory = null)
    {
        if (!Guid.TryParseExact(report.DiagnosticId, "N", out _)) return false;
        lock (Sync)
        {
            string? partial = null;
            var ownsPartial = false;
            try
            {
                directory ??= DefaultDirectory;
                SystemSetupPaths.EnsureSafeDirectory(directory);
                var destination = Path.Combine(directory, $"qwen-failure-{report.DiagnosticId}.json");
                partial = destination + ".part";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(report, Json);
                if (bytes.Length > 16 * 1024) return false;
                using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsPartial = true;
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(partial, destination);
                partial = null;
                // Only prune files owned by this writer; never follow a reparse point.
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("qwen-failure-*.json")
                    .Where(file => IsOwned(file)).OrderByDescending(file => file.LastWriteTimeUtc).Skip(MaximumReports))
                    file.Delete();
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                or System.Security.SecurityException or NotSupportedException or OutOfMemoryException)
            {
                // An unwritable/full disk must not replace the original worker failure.
                return false;
            }
            finally
            {
                if (ownsPartial && partial is not null)
                {
                    try { File.Delete(partial); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }

    private static bool IsOwned(FileInfo file) => (file.Attributes & FileAttributes.ReparsePoint) == 0
        && file.Name.Length == "qwen-failure-".Length + 32 + ".json".Length
        && Guid.TryParseExact(file.Name.AsSpan("qwen-failure-".Length, 32), "N", out _);
}
