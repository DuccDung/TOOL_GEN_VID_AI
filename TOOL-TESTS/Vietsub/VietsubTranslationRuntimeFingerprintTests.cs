using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationRuntimeFingerprintTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qwen-fingerprint-" + Guid.NewGuid().ToString("N"));

    public VietsubTranslationRuntimeFingerprintTests()
    {
        var files = new[] { "VideoMaker.Vietsub.TranslationWorker.exe", "VideoMaker.Vietsub.TranslationWorker.dll",
            "VideoMaker.Vietsub.TranslationWorker.runtimeconfig.json", "VideoMaker.Vietsub.TranslationWorker.deps.json",
            "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "LLamaSharp.dll",
            "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcomp140.dll" };
        Directory.CreateDirectory(_root);
        foreach (var file in files) File.WriteAllText(Path.Combine(_root, file), "approved-fixture:" + file);
        var native = Path.Combine(_root, "runtimes", "win-x64", "native", VietsubTranslationWorkerProfiles.SelectAvxName());
        Directory.CreateDirectory(native);
        foreach (var file in new[] { "llama.dll", "ggml.dll", "ggml-base.dll", "ggml-cpu.dll" })
            File.WriteAllText(Path.Combine(native, file), "native-fixture:" + file);
    }

    [Theory]
    [InlineData("coreclr.dll")]
    [InlineData("hostfxr.dll")]
    [InlineData("hostpolicy.dll")]
    [InlineData("System.Private.CoreLib.dll")]
    [InlineData("VideoMaker.Vietsub.TranslationWorker.runtimeconfig.json")]
    [InlineData("VideoMaker.Vietsub.TranslationWorker.deps.json")]
    public void Runtime_only_update_invalidates_existing_probe_evidence(string file)
    {
        var before = VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(_root);
        Assert.NotEmpty(before);
        File.AppendAllText(Path.Combine(_root, file), "runtime-update");
        Assert.NotEqual(before, VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(_root));
    }

    [Theory]
    [InlineData("coreclr.dll")]
    [InlineData("System.Private.CoreLib.dll")]
    [InlineData("VideoMaker.Vietsub.TranslationWorker.runtimeconfig.json")]
    public void Incomplete_self_contained_worker_cannot_reuse_a_ready_marker(string file)
    {
        Assert.NotEmpty(VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(_root));
        File.Delete(Path.Combine(_root, file));
        Assert.Empty(VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(_root));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
