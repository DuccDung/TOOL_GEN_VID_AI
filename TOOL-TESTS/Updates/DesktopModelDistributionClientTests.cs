using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.ModelDistribution;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_TESTS.Updates;

public sealed class DesktopModelDistributionClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-model-client-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Origin = new("https://models.invalid/");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Persisted_high_water_state_blocks_rollback_after_process_reopen()
    {
        using var handler = new Handler(request => Response(request, Signed(Catalog(7))));
        using var http = new HttpClient(handler);
        using (var first = Client(http)) await first.RefreshCatalogAsync(CancellationToken.None);
        handler.Callback = request => Response(request, Signed(Catalog(6)));
        using var reopened = Client(http);
        var error = await Assert.ThrowsAsync<ModelDistributionException>(() => reopened.RefreshCatalogAsync(CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.CatalogRollback, error.Code);
        Assert.Equal(2, handler.Paths.Count);
        Assert.All(handler.Paths, path => Assert.EndsWith("/catalog", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Corrupted_signature_never_replaces_last_trusted_state()
    {
        using var handler = new Handler(request => Response(request, Signed(Catalog(7))));
        using var http = new HttpClient(handler);
        using var client = Client(http);
        await client.RefreshCatalogAsync(CancellationToken.None);
        var statePath = Assert.Single(Directory.GetFiles(Path.Combine(_root, "trust"), "*.json"));
        var before = File.ReadAllBytes(statePath);
        var envelope = JsonSerializer.Deserialize<ModelDistributionEnvelope>(Signed(Catalog(8)), Json)!;
        var signature = Convert.FromBase64String(envelope.SignatureBase64);
        signature[0] ^= 1;
        handler.Callback = request => Response(request, JsonSerializer.SerializeToUtf8Bytes(
            envelope with { SignatureBase64 = Convert.ToBase64String(signature) }, Json));
        var error = await Assert.ThrowsAsync<ModelDistributionException>(() => client.RefreshCatalogAsync(CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.SignatureInvalid, error.Code);
        Assert.Equal(before, File.ReadAllBytes(statePath));
    }

    [Fact]
    public async Task Expired_catalog_and_unknown_artifact_fail_before_any_artifact_request()
    {
        using var handler = new Handler(request => Response(request, Signed(Catalog(7) with { ExpiresAtUtc = Now })));
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var expired = await Assert.ThrowsAsync<ModelDistributionException>(() => client.GetArtifactAsync("unknown", null, CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.CatalogExpired, expired.Code);
        handler.Callback = request => Response(request, Signed(Catalog(8)));
        var absent = await Assert.ThrowsAsync<ModelDistributionException>(() => client.GetArtifactAsync("unknown", null, CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.ArtifactNotFound, absent.Code);
        Assert.All(handler.Paths, path => Assert.EndsWith("/catalog", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Incompatible_build_is_rejected_before_artifact_transfer()
    {
        var qwen = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId) with
        {
            MinimumDesktopVersion = "99.0.0",
            MinimumDesktopBuildNumber = 9999
        };
        using var handler = new Handler(request => Response(request, Signed(Catalog(7) with { Artifacts = [qwen] })));
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var error = await Assert.ThrowsAsync<ModelDistributionException>(() => client.GetArtifactAsync(qwen.ArtifactId, null, CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.Incompatible, error.Code);
        Assert.Single(handler.Paths);
        Assert.EndsWith("/catalog", handler.Paths[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Known_revocations_survive_reopen_and_cannot_be_removed_by_a_later_catalog()
    {
        var qwen = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId);
        using var handler = new Handler(request => Response(request, Signed(Catalog(7) with
        {
            Artifacts = [], RevokedArtifactSha256 = [qwen.Sha256]
        })));
        using var http = new HttpClient(handler);
        using (var first = Client(http))
        {
            await first.RefreshCatalogAsync(CancellationToken.None);
            Assert.True(first.IsKnownRevoked(qwen.Sha256));
        }
        handler.Callback = request => Response(request, Signed(Catalog(8) with { Artifacts = [] }));
        using var reopened = Client(http);
        Assert.True(reopened.IsKnownRevoked(qwen.Sha256));
        await reopened.RefreshCatalogAsync(CancellationToken.None);
        Assert.True(reopened.IsKnownRevoked(qwen.Sha256));
        handler.Callback = request => Response(request, Signed(Catalog(9) with { Artifacts = [qwen] }));
        var error = await Assert.ThrowsAsync<ModelDistributionException>(() => reopened.GetArtifactAsync(qwen.ArtifactId, null, CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.ArtifactNotApproved, error.Code);
        Assert.All(handler.Paths, path => Assert.EndsWith("/catalog", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Response_outside_configured_origin_is_not_trusted()
    {
        using var handler = new Handler(_ => Response(new HttpRequestMessage(HttpMethod.Get,
            "https://other.invalid/api/model-distribution/Stable/catalog"), Signed(Catalog(7))));
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var error = await Assert.ThrowsAsync<ModelDistributionException>(() => client.RefreshCatalogAsync(CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.CatalogInvalid, error.Code);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "trust"), "*.json"));
    }

    [Fact]
    public async Task Oversized_metadata_is_rejected_before_state_is_written()
    {
        using var handler = new Handler(request => Response(request, new byte[ModelCatalogVerifier.MaximumEnvelopeBytes + 1]));
        using var http = new HttpClient(handler);
        using var client = Client(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.RefreshCatalogAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "trust"), "*.json"));
    }

    private DesktopModelDistributionClient Client(HttpClient http) => new(Origin,
        new("fixture-models", "Stable", "win-x64", new Dictionary<string, string>
        {
            ["fixture"] = _key.ExportSubjectPublicKeyInfoPem()
        }), _root, http, new FrozenClock(), "1.0.0", 1);

    private ModelDistributionCatalog Catalog(long sequence) => new(1, "fixture-models", "Stable", "win-x64", sequence,
        Now.AddHours(-1), Now.AddDays(1), ApprovedModelArtifactPins.All);

    private byte[] Signed(ModelDistributionCatalog catalog) => ModelCatalogSigner.CreateEnvelope(
        JsonSerializer.SerializeToUtf8Bytes(catalog, Json), "fixture", _key);
    private static HttpResponseMessage Response(HttpRequestMessage request, byte[] bytes) => new(HttpStatusCode.OK)
    {
        RequestMessage = request,
        Content = new ByteArrayContent(bytes)
    };

    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Callback { get; set; } = callback;
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Callback(request));
        }
    }

    public void Dispose()
    {
        _key.Dispose();
        var path = Path.GetFullPath(_root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup escaped its temporary root.");
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
