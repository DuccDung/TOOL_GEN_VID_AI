using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_SHARED.Distribution.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_LOCAL.ModelDistribution;

internal sealed record InstalledStaticModelOptions(string Engine, string OutputDirectory, int Iterations,
    bool AcceptResourceWarning = false, string? ConfirmedResourceProfile = null);
internal sealed record InstalledStaticModelSample(int Iteration, long ElapsedMilliseconds, long? WorkerPrivateBytes,
    string? OutputSha256 = null, long? AudioDurationMilliseconds = null, int? AudioSampleRate = null);
internal sealed record InstalledStaticModelBinding(string OperationId, string MachineFingerprint,
    string ApplicationFingerprint, string RuntimeFingerprint, string? RuntimeProfileId);
internal sealed record InstalledStaticModelReport(int SchemaVersion, string Kind, string Engine, string BundleId,
    string ArchiveSha256, InstalledStaticModelBinding Binding, int RequestedIterations, int CompletedIterations,
    IReadOnlyList<InstalledStaticModelSample> Samples, long? PrivateMemoryGrowthBytes, bool Succeeded,
    string? ErrorCode, DateTime CheckedAtUtc, bool ProductionReadinessChanged = false,
    bool BusinessAccessVerified = false, bool NetworkDownloadsAllowed = false, bool ListeningQualityVerified = false);

internal interface IInstalledStaticModelSession : IAsyncDisposable
{
    InstalledStaticModelBinding Binding { get; }
    void AssertUnchanged();
    Task<InstalledStaticModelSample> RunIterationAsync(int iteration, string outputDirectory, CancellationToken token);
}

internal static class InstalledStaticModelQualification
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const long MaximumPrivateMemoryGrowthBytes = 256L * 1024 * 1024;

    internal static async Task<InstalledStaticModelReport> RunAsync(InstalledStaticModelOptions options,
        Func<InstalledStaticModelOptions, CancellationToken, Task<IInstalledStaticModelSession>> open,
        CancellationToken token)
    {
        // Trust, installed receipts and leases precede even creation of the QA directory.
        await using var session = await open(options, token);
        session.AssertUnchanged();
        StaticModelRuntimePolicy.ValidateLocalPath(options.OutputDirectory);
        if (Directory.Exists(options.OutputDirectory) || File.Exists(options.OutputDirectory))
            throw new IOException("Qualification output must be a new directory.");
        Directory.CreateDirectory(options.OutputDirectory);
        await using var outputLease = new FileStream(Path.Combine(options.OutputDirectory, ".qualification.lock"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (Directory.EnumerateFileSystemEntries(options.OutputDirectory).Count() != 1)
            throw new IOException("Qualification output is not empty.");
        var samples = new List<InstalledStaticModelSample>();
        string? failure = null;
        try
        {
            for (var i = 1; i <= options.Iterations; i++)
            {
                token.ThrowIfCancellationRequested();
                session.AssertUnchanged();
                var sample = await session.RunIterationAsync(i, options.OutputDirectory, token);
                if (sample.Iteration != i || sample.ElapsedMilliseconds < 0
                    || (options.Engine == "qwen" && sample.WorkerPrivateBytes is not > 0)
                    || (options.Engine == "piper" && (!StaticModelRuntimePolicy.IsSha256(sample.OutputSha256)
                        || sample.AudioDurationMilliseconds is not > 0 || sample.AudioSampleRate is not > 0)))
                    throw new InvalidDataException("Incomplete native qualification evidence.");
                session.AssertUnchanged();
                samples.Add(sample);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { failure = ErrorCode(error); }
        var growth = options.Engine == "qwen" ? MemoryGrowth(samples) : null;
        if (failure is null && growth >= MaximumPrivateMemoryGrowthBytes) failure = "installed_model_memory_growth";
        var report = new InstalledStaticModelReport(1, "installed-static-model-qualification", options.Engine,
            ApprovedStaticModelBundle.Current.BundleId, ApprovedStaticModelBundle.Current.ArchiveSha256,
            session.Binding, options.Iterations, samples.Count, samples, growth,
            failure is null && samples.Count == options.Iterations, failure, DateTime.UtcNow);
        // Never overwrite evidence from another run and never write production readiness.
        await using var result = new FileStream(Path.Combine(options.OutputDirectory, "qualification.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(result, report, Json, CancellationToken.None);
        return report;
    }

    // Match the bundled benchmark exactly: upper median of the last ten
    // minus upper median of samples 11..20, after at least thirty iterations.
    internal static long? MemoryGrowth(IReadOnlyList<InstalledStaticModelSample> samples)
    {
        if (samples.Count < 30 || samples.Any(x => x.WorkerPrivateBytes is not > 0)) return null;
        static long Median(IEnumerable<long> values)
        {
            var sorted = values.Order().ToArray();
            return sorted[sorted.Length / 2];
        }
        return checked(Median(samples.TakeLast(10).Select(x => x.WorkerPrivateBytes!.Value))
            - Median(samples.Skip(10).Take(10).Select(x => x.WorkerPrivateBytes!.Value)));
    }

    internal static string ErrorCode(Exception error) => error switch
    {
        SetupException setup => setup.Code,
        VietsubTranslationException translation => translation.Code,
        VietsubVoiceException voice => voice.Code,
        ModelDistributionException distribution => distribution.Code,
        OperationCanceledException => "installed_model_cancelled",
        ArgumentException => "installed_model_arguments_invalid",
        UnauthorizedAccessException => "installed_model_access_denied",
        IOException or InvalidDataException => "installed_model_evidence_invalid",
        _ => "installed_model_qualification_failed"
    };
}
