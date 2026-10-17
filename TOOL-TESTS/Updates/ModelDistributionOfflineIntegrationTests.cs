using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.ModelDistribution;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_TESTS.Updates;

public sealed class ModelDistributionOfflineIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-model-offline-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Cache => Path.Combine(_root, "cache");

    [Fact]
    public async Task Disabling_downloads_retains_sticky_revocations_without_using_keys_or_network()
    {
        var qwen = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId);
        using var handler = new Handler(request => Response(request, Signed([qwen.Sha256])));
        using var http = new HttpClient(handler);
        using (var online = Client(http)) await online.RefreshCatalogAsync(CancellationToken.None);
        using var offline = Client(http, downloadsEnabled: false, emptyKeys: true);
        Assert.False(offline.CanDownload);
        Assert.True(offline.IsKnownRevoked(qwen.Sha256));
        Assert.Equal(1, handler.Calls);
        var refresh = await Assert.ThrowsAsync<ModelDistributionException>(() => offline.RefreshCatalogAsync(CancellationToken.None));
        var download = await Assert.ThrowsAsync<ModelDistributionException>(() => offline.GetArtifactAsync(qwen.ArtifactId, null, CancellationToken.None));
        var piper = await Assert.ThrowsAsync<ModelDistributionException>(() => offline.PreparePiperBundleAsync(null, CancellationToken.None));
        Assert.All(new[] { refresh, download, piper }, error => Assert.Equal(ModelDistributionErrors.Disabled, error.Code));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Offline_qwen_checks_revocation_before_reusing_or_downloading_any_model()
    {
        var approved = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId);
        using var handler = new Handler(request => Response(request, Signed([approved.Sha256])));
        using var http = new HttpClient(handler);
        using (var online = Client(http)) await online.RefreshCatalogAsync(CancellationToken.None);
        using var offline = Client(http, downloadsEnabled: false, emptyKeys: true);
        using var modelNetwork = new HttpClient(new Handler(_ => throw new InvalidOperationException("Revoked model must not contact upstream.")));
        using var store = new VietsubTranslationComponentStore(VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km,
            componentsRoot: Path.Combine(_root, "qwen"), httpClient: modelNetwork,
            approvedLocalModelCandidates: [Path.Combine(_root, "local-candidate.gguf")], distribution: offline);
        var status = store.Inspect(requireProbe: false, checkResources: false);
        Assert.False(status.ModelVerified);
        Assert.Equal(VietsubTranslationErrorCodes.RuntimeInvalid, status.ErrorCode);
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => store.InstallModelAsync(null, CancellationToken.None));
        Assert.Equal(VietsubTranslationErrorCodes.RuntimeInvalid, error.Code);
        Assert.False(File.Exists(store.ModelPath));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_piper_payload_does_not_mask_known_revocation(bool downloadsEnabled)
    {
        var piper = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.PiperArchiveArtifactId);
        using var handler = new Handler(request => Response(request, Signed([piper.Sha256])));
        using var http = new HttpClient(handler);
        using (var online = Client(http)) await online.RefreshCatalogAsync(CancellationToken.None);
        using var distribution = Client(http, downloadsEnabled, emptyKeys: !downloadsEnabled);
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(Path.Combine(_root, "workspace")), true,
            offlineBundle: new PiperOfflineBundle(Path.Combine(_root, "missing-bundle"), PiperOfflineBundle.ApprovedDefinition()),
            distribution: distribution);
        var adapter = new PiperSetupAdapter(store, true);
        var status = adapter.Inspect();
        Assert.Equal("REPAIR_REQUIRED", status.State);
        Assert.False(status.CanInstall);
        Assert.Equal(VietsubVoiceErrorCodes.BundleInvalid, status.ErrorCode);
        Assert.Contains("thu hồi", status.Message, StringComparison.OrdinalIgnoreCase);
        var error = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.InstallAsync(null, CancellationToken.None));
        Assert.Equal(VietsubVoiceErrorCodes.BundleInvalid, error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Readonly_client_keeps_default_bundled_setup_without_forcing_server_payload()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Offline mode cannot access network."));
        using var http = new HttpClient(handler);
        using var offline = Client(http, downloadsEnabled: false, emptyKeys: true);
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(Path.Combine(_root, "workspace")), true,
            offlineBundle: new PiperOfflineBundle(Path.Combine(_root, "missing-bundle"), PiperOfflineBundle.ApprovedDefinition()),
            distribution: offline);
        var status = new PiperSetupAdapter(store, true).Inspect();
        Assert.False(store.CanDownloadDistributionPayload);
        Assert.Equal("REPAIR_REQUIRED", status.State);
        Assert.False(status.CanInstall);
        Assert.Equal(VietsubVoiceErrorCodes.BundleMissing, status.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Disabled_voice_feature_does_not_read_or_install_unready_payload()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Disabled component cannot access network."));
        using var http = new HttpClient(handler);
        using var distribution = Client(http);
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(Path.Combine(_root, "workspace")), false,
            distribution: distribution);
        Assert.Equal("DISABLED", store.GetStatus().Status);
        Assert.Equal("DISABLED", new PiperSetupAdapter(store, false).Inspect().State);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Corrupted_trust_state_fails_closed_in_offline_readiness()
    {
        using var handler = new Handler(request => Response(request, Signed([])));
        using var http = new HttpClient(handler);
        using (var online = Client(http)) await online.RefreshCatalogAsync(CancellationToken.None);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(Cache, "trust"), "*.json"));
        await File.WriteAllTextAsync(path, "{}");
        using var offline = Client(http, downloadsEnabled: false, emptyKeys: true);
        Assert.Throws<InvalidDataException>(() => offline.IsKnownRevoked(ApprovedModelArtifactPins.All[0].Sha256));
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(Path.Combine(_root, "workspace")), true,
            offlineBundle: new PiperOfflineBundle(Path.Combine(_root, "missing-bundle"), PiperOfflineBundle.ApprovedDefinition()),
            distribution: offline);
        var status = new PiperSetupAdapter(store, true).Inspect();
        Assert.False(status.CanInstall);
        Assert.NotEqual("READY", status.State);
        var piperError = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.InstallAsync(null, CancellationToken.None));
        Assert.Equal(VietsubVoiceErrorCodes.BundleInvalid, piperError.Code);
        using var qwen = new VietsubTranslationComponentStore(VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km,
            componentsRoot: Path.Combine(_root, "qwen"), httpClient: http,
            approvedLocalModelCandidates: [], distribution: offline);
        var qwenStatus = qwen.Inspect(requireProbe: false, checkResources: false);
        Assert.False(qwenStatus.ModelVerified);
        Assert.Equal(VietsubTranslationErrorCodes.RuntimeInvalid, qwenStatus.ErrorCode);
        var qwenError = await Assert.ThrowsAsync<VietsubTranslationException>(() => qwen.InstallModelAsync(null, CancellationToken.None));
        Assert.Equal(VietsubTranslationErrorCodes.RuntimeInvalid, qwenError.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Piper_bundle_preparation_lease_prevents_parallel_promote_before_network()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Busy preparation cannot start network."));
        using var http = new HttpClient(handler);
        using var distribution = Client(http);
        var leasePath = Path.Combine(Cache, "bundles", "piper", PiperOfflineBundle.ApprovedDefinition().BundleVersion + ".prepare.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
        using var firstApp = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var error = await Assert.ThrowsAsync<ModelDistributionException>(() => distribution.PreparePiperBundleAsync(null, CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.Busy, error.Code);
        Assert.Equal(0, handler.Calls);
    }

    private DesktopModelDistributionClient Client(HttpClient http, bool downloadsEnabled = true, bool emptyKeys = false) => new(
        new Uri("https://models.invalid/"), new("fixture-models", "Stable", "win-x64", emptyKeys
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["fixture"] = _key.ExportSubjectPublicKeyInfoPem() }),
        Cache, http, new FrozenClock(), "1.0.0", 1, downloadsEnabled);

    private byte[] Signed(IReadOnlyList<string> revoked) => ModelCatalogSigner.CreateEnvelope(
        JsonSerializer.SerializeToUtf8Bytes(new ModelDistributionCatalog(1, "fixture-models", "Stable", "win-x64", 7,
            Now.AddHours(-1), Now.AddDays(1), [], revoked), Json), "fixture", _key);

    private static HttpResponseMessage Response(HttpRequestMessage request, byte[] bytes) => new(HttpStatusCode.OK)
    { RequestMessage = request, Content = new ByteArrayContent(bytes) };
    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(callback(request)); }
    }

    public void Dispose()
    {
        _key.Dispose();
        var path = Path.GetFullPath(_root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup escaped temporary root.");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
