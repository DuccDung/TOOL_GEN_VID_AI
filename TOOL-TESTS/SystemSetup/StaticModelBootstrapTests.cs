using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.ModelDistribution;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.SystemSetup;

public sealed class StaticModelBootstrapTests
{
    [Fact]
    public void Complete_result_requires_exactly_the_five_successful_components()
    {
        var ready = Result(["media", "ocr", "qwen", "piper", "webview2"]);
        Assert.True(StaticModelRuntimePolicy.HasAllRequiredComponents(ready));
        Assert.False(StaticModelRuntimePolicy.HasAllRequiredComponents(ready with { Succeeded = false }));
        Assert.False(StaticModelRuntimePolicy.HasAllRequiredComponents(ready with { BundleId = "another-release" }));
        Assert.False(StaticModelRuntimePolicy.HasAllRequiredComponents(Result(["media", "ocr", "qwen", "piper"])));
        Assert.False(StaticModelRuntimePolicy.HasAllRequiredComponents(Result(["media", "ocr", "qwen", "piper", "piper"])));
        Assert.False(StaticModelRuntimePolicy.HasAllRequiredComponents(ready with
        {
            Components = ready.Components.Select(x => x.ComponentId == "qwen" ? x with { Ready = false } : x).ToArray()
        }));
    }

    [Fact]
    public void Static_archive_mode_does_not_require_enabling_the_signed_catalog_client()
    {
        var options = new DesktopOptions { ModelDistribution = new() { Mode = "StaticArchive", Enabled = false } };
        Assert.True(StaticModelRuntimePolicy.IsStaticMode(options));
    }

    [Fact]
    public void Preserved_user_feature_flags_do_not_create_a_static_setup_repair_loop()
    {
        var root = Path.Combine(Path.GetTempPath(), "static-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var application = new DesktopOptions
        {
            Server = new() { BaseUrl = "https://gateway.example.invalid/" },
            Database = new() { ConnectionString = "test-only-fixture" },
            Storage = new() { WorkspaceRoot = Path.Combine(root, "workspace") },
            ModelDistribution = new() { Mode = "StaticArchive", Enabled = true }
        };
        var userText = """{"Features":{"VietsubEnabled":false,"VietsubOcrEnabled":false,"VietsubLocalTranslationEnabled":false,"VietsubLocalVoiceEnabled":false,"SpeechSynchronizationEnabled":false}}""";
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), JsonSerializer.Serialize(application));
            File.WriteAllText(Path.Combine(root, "appsettings.user.json"), userText);
            var options = DesktopOptions.Load(root, preferencesDirectory: null, requireDatabase: false);
            Assert.True(options.Features.VietsubEnabled);
            Assert.True(options.Features.VietsubOcrEnabled);
            Assert.True(options.Features.VietsubLocalTranslationEnabled);
            Assert.True(options.Features.VietsubLocalVoiceEnabled);
            Assert.False(options.ModelDistribution.Enabled);
            Assert.Equal("StaticArchive", options.ModelDistribution.Mode);
            Assert.Equal(application.Server.BaseUrl, options.Server.BaseUrl);
            Assert.Equal(application.Database.ConnectionString, options.Database.ConnectionString);
            Assert.Equal(application.Storage.WorkspaceRoot, options.Storage.WorkspaceRoot);
            Assert.False(options.Features.SpeechSynchronizationEnabled);
            Assert.Equal(userText, File.ReadAllText(Path.Combine(root, "appsettings.user.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Compiled_static_policy_wins_over_case_variant_legacy_mode_and_disabled_flags()
    {
        var root = JsonNode.Parse("""{"features":{"vietsubenabled":false,"vietsublocaltranslationenabled":false,"SpeechSynchronizationEnabled":false},"modeldistribution":{"mode":"Bundled","enabled":true,"CacheRoot":"C:\\approved-cache"},"Server":{"BaseUrl":"https://gateway.example.invalid/"}}""")!.AsObject();
        var server = root["Server"]!.ToJsonString();
        DesktopOptions.ApplyStaticModelReleasePolicy(root, requiredByRelease: true);
        var options = root.Deserialize<DesktopOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(options.Features.VietsubEnabled && options.Features.VietsubLocalTranslationEnabled
            && options.Features.VietsubLocalVoiceEnabled && options.Features.VietsubOcrEnabled);
        Assert.False(options.ModelDistribution.Enabled);
        Assert.Equal("StaticArchive", options.ModelDistribution.Mode);
        Assert.Equal("C:\\approved-cache", options.ModelDistribution.CacheRoot);
        Assert.False(options.Features.SpeechSynchronizationEnabled);
        Assert.Equal(server, root["Server"]!.ToJsonString());
    }

    [Fact]
    public void Uncompiled_bundled_policy_preserves_existing_disabled_features()
    {
        var root = JsonNode.Parse("""{"Features":{"VietsubEnabled":false,"VietsubLocalTranslationEnabled":false},"ModelDistribution":{"Mode":"Bundled","Enabled":false}}""")!.AsObject();
        var before = root.ToJsonString();
        DesktopOptions.ApplyStaticModelReleasePolicy(root, requiredByRelease: false);
        Assert.Equal(before, root.ToJsonString());
    }

    [Fact]
    public void Setup_handoff_keeps_pending_barrier_after_component_receipt_is_written()
    {
        var root = Path.Combine(Path.GetTempPath(), "static-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pending = Path.Combine(root, "pending.json");
        var receiptPath = Path.Combine(root, "receipt.json");
        var receipt = new StaticModelInstallReceipt(1, Path.Combine(root, "cache"), "machine", "application",
            DateTime.UtcNow, Result(["media", "ocr", "qwen", "piper", "webview2"]));
        try
        {
            File.WriteAllText(pending, "pending-owned-by-setup");
            StaticModelRuntimePolicy.WriteCommittedReceipt(receiptPath, pending, receipt, keepPending: true);
            Assert.True(File.Exists(receiptPath));
            Assert.Equal("pending-owned-by-setup", File.ReadAllText(pending));
            StaticModelRuntimePolicy.WriteCommittedReceipt(receiptPath, pending, receipt, keepPending: false);
            Assert.False(File.Exists(pending));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Bootstrap_options_require_operation_parent_and_absolute_paths_without_writing_them()
    {
        var root = Path.Combine(Path.GetTempPath(), "static-bootstrap-" + Guid.NewGuid().ToString("N"));
        var operation = Guid.NewGuid().ToString("N");
        var args = Arguments(operation, Path.Combine(root, "result.json"), Path.Combine(root, "cache"));
        var parsed = StaticModelBootstrapCommand.ParseOptions(args);
        Assert.Equal(operation, parsed.OperationId);
        Assert.False(parsed.AcceptResourceWarning);
        Assert.True(StaticModelBootstrapCommand.ParseOptions([.. args, "--accept-resource-warning",
            "--confirmed-resource-profile", VietsubTranslationWorkerProfiles.LowMemoryProfileId]).AcceptResourceWarning);
        Assert.Throws<ArgumentException>(() => StaticModelBootstrapCommand.ParseOptions([.. args, "--accept-resource-warning"]));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("--operation", "not-a-guid")]
    [InlineData("--parent-pid", "0")]
    [InlineData("--result", "relative.json")]
    [InlineData("--result", "C:\\result.txt")]
    [InlineData("--cache-root", "\\\\server\\models")]
    [InlineData("--cache-root", "C:\\models:stream")]
    public void Invalid_options_are_rejected_before_runtime_changes(string key, string value)
    {
        var args = Arguments(Guid.NewGuid().ToString("N"), "C:\\vm-check\\result.json", "C:\\vm-check\\cache");
        args[Array.IndexOf(args, key) + 1] = value;
        Assert.Throws<ArgumentException>(() => StaticModelBootstrapCommand.ParseOptions(args));
    }

    [Fact]
    public void Duplicate_confirmation_or_unknown_source_override_is_rejected()
    {
        var args = Arguments(Guid.NewGuid().ToString("N"), "C:\\vm-check\\result.json", "C:\\vm-check\\cache");
        Assert.Throws<ArgumentException>(() => StaticModelBootstrapCommand.ParseOptions([.. args, "--operation", Guid.NewGuid().ToString("N")]));
        Assert.Throws<ArgumentException>(() => StaticModelBootstrapCommand.ParseOptions([.. args, "--archive-url", "https://other.invalid/file.zip"]));
        Assert.Throws<ArgumentException>(() => StaticModelBootstrapCommand.ParseOptions([.. args, "--accept-resource-warning", "--accept-resource-warning"]));
    }

    [Fact]
    public async Task Static_model_store_never_falls_back_to_upstream_when_payload_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "static-bootstrap-" + Guid.NewGuid().ToString("N"));
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        try
        {
            using var store = new VietsubTranslationComponentStore(VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km,
                componentsRoot: root, httpClient: http, allowUpstreamDownload: false);
            var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => store.InstallModelAsync(null, CancellationToken.None));
            Assert.Equal(VietsubTranslationErrorCodes.RuntimeNotInstalled, error.Code);
            Assert.Equal(0, handler.Attempts);
            Assert.False(File.Exists(store.ModelPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static StaticModelBootstrapResult Result(string[] ids) => new(1, Guid.NewGuid().ToString("N"),
        ApprovedStaticModelBundle.Current.BundleId, true, ids.Select(x => new StaticModelComponentResult(x, true,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-evidence:" + x))).ToLowerInvariant())).ToArray());
    private static string[] Arguments(string operation, string result, string cache) =>
        ["--prepare-static-models", "--operation", operation, "--result", result, "--cache-root", cache,
            "--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)];
    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
