using System.Text.Json;
using TOOL_LOCAL.ModelDistribution;

namespace TOOL_LOCAL.SystemSetup;

// An explicit, read-only qualification entry point, before options/login/SQL composition.
internal static class InstalledStaticModelCommand
{
    internal const string Flag = "--qualify-installed-static-models";
    internal static bool Matches(string[] args) => args.Length > 0 && args[0] == Flag;

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(2));
            var report = await InstalledStaticModelQualification.RunAsync(options,
                InstalledStaticModelSession.OpenAsync, timeout.Token);
            Console.WriteLine(JsonSerializer.Serialize(report, InstalledStaticModelQualification.Json));
            return report.Succeeded ? 0 : 2;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "installed-static-model-qualification",
                schemaVersion = 1, succeeded = false, errorCode = InstalledStaticModelQualification.ErrorCode(error),
                productionReadinessChanged = false }, InstalledStaticModelQualification.Json));
            return 2;
        }
    }

    internal static InstalledStaticModelOptions ParseOptions(string[] args)
    {
        if (!Matches(args)) throw new ArgumentException("Invalid qualification command.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var accepted = false;
        for (var i = 1; i < args.Length; i++)
        {
            var name = args[i];
            if (name == "--accept-resource-warning" && !accepted) { accepted = true; continue; }
            if (name is not ("--engine" or "--output" or "--iterations" or "--confirmed-resource-profile")
                || ++i >= args.Length || !values.TryAdd(name, args[i])) throw new ArgumentException("Invalid qualification arguments.");
        }
        if (!values.TryGetValue("--engine", out var engine) || engine is not ("qwen" or "piper")
            || !values.TryGetValue("--output", out var output)
            || !values.TryGetValue("--iterations", out var count) || !int.TryParse(count, out var iterations)
            || iterations is < 1 or > 100) throw new ArgumentException("Invalid qualification arguments.");
        values.TryGetValue("--confirmed-resource-profile", out var profile);
        if (accepted != (profile is not null) || (profile is not null && profile is not
            (Vietsub.Translation.VietsubTranslationWorkerProfiles.StandardProfileId or
             Vietsub.Translation.VietsubTranslationWorkerProfiles.LowMemoryProfileId))
            || (engine == "piper" && accepted)) throw new ArgumentException("Confirmation must identify the approved Qwen profile.");
        StaticModelRuntimePolicy.ValidateLocalPath(output);
        return new(engine, Path.GetFullPath(output), iterations, accepted, profile);
    }
}
