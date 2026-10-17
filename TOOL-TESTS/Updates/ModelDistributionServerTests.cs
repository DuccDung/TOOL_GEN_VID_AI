using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.ModelDistribution;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_TESTS.Updates;

public sealed class ModelDistributionServerTests
{
    [Fact]
    public async Task DisabledRepository_DoesNotCreateOrReadTheStorageRoot()
    {
        using var fixture = new RepositoryFixture(createCatalog: false);
        var missing = Path.Combine(fixture.Root, "not-created");
        using var store = fixture.CreateStore(new() { StorageRoot = missing });

        await AssertCode(ModelDistributionErrors.Disabled, () => store.GetCatalogAsync("Stable", default));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task Catalog_RequiresBothOperatorSignatureTrustAndExactEnvelopePin()
    {
        using var fixture = new RepositoryFixture();
        using var noKey = fixture.CreateStore(fixture.Options(keys: new Dictionary<string, string>()));
        using var noPin = fixture.CreateStore(fixture.Options(pins: new Dictionary<string, string>()));
        using var wrongPin = fixture.CreateStore(fixture.Options(pins: new() { ["Stable"] = new string('0', 64) }));

        await AssertCode(ModelDistributionErrors.TrustNotConfigured, () => noKey.GetCatalogAsync("Stable", default));
        await AssertCode(ModelDistributionErrors.TrustNotConfigured, () => noPin.GetCatalogAsync("Stable", default));
        await AssertCode(ModelDistributionErrors.CatalogInvalid, () => wrongPin.GetCatalogAsync("Stable", default));
    }

    [Fact]
    public async Task Catalog_ReturnsTheExactAuthenticatedEnvelopeWithoutWritingRepositoryState()
    {
        using var fixture = new RepositoryFixture();
        var before = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories);
        using var store = fixture.CreateStore();

        var published = await store.GetCatalogAsync("Stable", default);

        Assert.Equal(fixture.EnvelopeBytes, published.EnvelopeBytes);
        Assert.Equal(fixture.EnvelopeHash, published.EnvelopeSha256);
        Assert.Equal(10, published.Catalog.Sequence);
        Assert.Equal(before, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task EnvelopePinDoesNotReplaceCryptographicSignatureVerification()
    {
        using var fixture = new RepositoryFixture();
        using var unrelatedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var store = fixture.CreateStore(fixture.Options(keys: new() { ["test-key"] = unrelatedKey.ExportSubjectPublicKeyInfoPem() }));

        await AssertCode(ModelDistributionErrors.SignatureInvalid, () => store.GetCatalogAsync("Stable", default));
    }

    [Fact]
    public async Task Catalog_RejectsExpiredMetadataAndSequenceBelowOperatorMinimum()
    {
        using var expired = new RepositoryFixture(expires: DateTimeOffset.UtcNow.AddHours(-1));
        using var expiredStore = expired.CreateStore();
        await AssertCode(ModelDistributionErrors.CatalogExpired, () => expiredStore.GetCatalogAsync("Stable", default));

        using var fixture = new RepositoryFixture();
        using var minimumStore = fixture.CreateStore(fixture.Options(minimumSequence: 11));
        await AssertCode(ModelDistributionErrors.CatalogInvalid, () => minimumStore.GetCatalogAsync("Stable", default));
    }

    [Theory]
    [InlineData("../staging")]
    [InlineData("stable")]
    [InlineData("Stable:stream")]
    [InlineData("Stable%2f..")]
    [InlineData("Stable\\..")]
    public async Task Catalog_RejectsNoncanonicalOrUnconfiguredChannels(string channel)
    {
        using var fixture = new RepositoryFixture();
        using var store = fixture.CreateStore();
        await AssertCode(ModelDistributionErrors.ArtifactNotFound, () => store.GetCatalogAsync(channel, default));
    }

    [Fact]
    public async Task Catalog_NeverReadsFromThePublicWebRoot()
    {
        using var fixture = new RepositoryFixture();
        using var store = fixture.CreateStore(webRoot: fixture.Root);
        await AssertCode(ModelDistributionErrors.TrustNotConfigured, () => store.GetCatalogAsync("Stable", default));
    }

    [Fact]
    public async Task Catalog_RejectsOversizedEnvelopeBeforeDeserializing()
    {
        using var fixture = new RepositoryFixture();
        await File.WriteAllBytesAsync(fixture.CatalogPath, new byte[2 * 1024 * 1024 + 1]);
        using var store = fixture.CreateStore();
        await AssertCode(ModelDistributionErrors.CatalogInvalid, () => store.GetCatalogAsync("Stable", default));
    }

    [Theory]
    [InlineData("../manifest.json")]
    [InlineData("manifest.json:stream")]
    [InlineData("manifest.json.")]
    [InlineData("CON.json")]
    [InlineData("manifest%2ejson")]
    public async Task Artifact_RejectsUnsafeNamesBeforeOpeningPayload(string fileName)
    {
        using var fixture = new RepositoryFixture();
        using var store = fixture.CreateStore();
        await AssertCode(ModelDistributionErrors.ArtifactNotFound,
            () => store.OpenArtifactAsync("Stable", fixture.Artifact.Sha256, fileName, default));
    }

    [Fact]
    public async Task UnreferencedOrMissingArtifactIsNeverServed()
    {
        using var fixture = new RepositoryFixture();
        using var store = fixture.CreateStore();
        await AssertCode(ModelDistributionErrors.ArtifactNotFound,
            () => store.OpenArtifactAsync("Stable", new string('0', 64), fixture.Artifact.FileName, default));
        await AssertCode(ModelDistributionErrors.ArtifactNotFound,
            () => store.OpenArtifactAsync("Stable", fixture.Artifact.Sha256, fixture.Artifact.FileName, default));
    }

    [Fact]
    public async Task Artifact_RejectsCorruptionWithTheSameLengthAsTheApprovedPayload()
    {
        using var fixture = new RepositoryFixture();
        var path = fixture.WriteArtifact(new byte[checked((int)fixture.Artifact.SizeBytes)]);
        using var store = fixture.CreateStore();

        await AssertCode(ModelDistributionErrors.ArtifactInvalid,
            () => store.OpenArtifactAsync("Stable", fixture.Artifact.Sha256, fixture.Artifact.FileName, default));

        // A failed scan releases its handle, so a repair process can replace the broken file.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task Artifact_RejectsTruncatedPayload()
    {
        using var fixture = new RepositoryFixture();
        fixture.WriteArtifact([1, 2, 3]);
        using var store = fixture.CreateStore();
        await AssertCode(ModelDistributionErrors.ArtifactInvalid,
            () => store.OpenArtifactAsync("Stable", fixture.Artifact.Sha256, fixture.Artifact.FileName, default));
    }

    [Fact]
    public async Task RevokedArtifactCannotBeOpenedEvenIfTheOldPayloadRemainsOnDisk()
    {
        using var fixture = new RepositoryFixture(revoked: true);
        fixture.WriteArtifact([1, 2, 3]);
        using var store = fixture.CreateStore();
        var catalog = await store.GetCatalogAsync("Stable", default);
        Assert.Empty(catalog.Catalog.Artifacts);
        Assert.Contains(fixture.Artifact.Sha256, catalog.Catalog.RevokedArtifactSha256!);
        await AssertCode(ModelDistributionErrors.ArtifactNotFound,
            () => store.OpenArtifactAsync("Stable", fixture.Artifact.Sha256, fixture.Artifact.FileName, default));
    }

    [Fact]
    public void Paths_RejectJunctionOrSymbolicLinkBeforeOpeningFiles()
    {
        using var fixture = new RepositoryFixture();
        var outside = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "file.gguf"), "untrusted");
        var link = Path.Combine(fixture.Root, "linked");
        if (OperatingSystem.IsWindows())
        {
            // Junctions require no symlink privilege and exercise the same reparse-point rejection.
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(link);
            start.ArgumentList.Add(outside);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        try
        {
            Assert.Throws<InvalidDataException>(() => ModelRepositoryPaths.Resolve(fixture.Root, "linked/file.gguf"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task Artifact_ResponseSupportsSingleRangeAndStrongETag()
    {
        using var fixture = new ControllerFixture("GET");
        fixture.Context.Request.Headers.Range = "bytes=3-6";
        await fixture.ExecuteArtifact();

        Assert.Equal(StatusCodes.Status206PartialContent, fixture.Context.Response.StatusCode);
        Assert.Equal("bytes 3-6/10", fixture.Context.Response.Headers.ContentRange.ToString());
        Assert.Equal(4, fixture.Context.Response.ContentLength);
        Assert.Equal("3456", Encoding.ASCII.GetString(fixture.Body.ToArray()));
        Assert.Equal($"\"{fixture.Hash}\"", fixture.Context.Response.Headers.ETag.ToString());
        Assert.Equal("nosniff", fixture.Context.Response.Headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public async Task Artifact_HeadReturnsLengthAndETagWithoutBody()
    {
        using var fixture = new ControllerFixture("HEAD");
        await fixture.ExecuteArtifact();
        Assert.Equal(10, fixture.Context.Response.ContentLength);
        Assert.Empty(fixture.Body.ToArray());
        Assert.Equal($"\"{fixture.Hash}\"", fixture.Context.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Artifact_IfRangeMismatchReturnsWholeVerifiedPayload()
    {
        using var fixture = new ControllerFixture("GET");
        fixture.Context.Request.Headers.Range = "bytes=3-6";
        fixture.Context.Request.Headers.IfRange = "\"different-version\"";
        await fixture.ExecuteArtifact();
        Assert.Equal(StatusCodes.Status200OK, fixture.Context.Response.StatusCode);
        Assert.Equal("0123456789", Encoding.ASCII.GetString(fixture.Body.ToArray()));
    }

    [Fact]
    public async Task Artifact_IfNoneMatchReturns304AndUnsatisfiableRangeReturns416()
    {
        using var conditional = new ControllerFixture("GET");
        conditional.Context.Request.Headers.IfNoneMatch = $"\"{conditional.Hash}\"";
        await conditional.ExecuteArtifact();
        Assert.Equal(StatusCodes.Status304NotModified, conditional.Context.Response.StatusCode);
        Assert.Empty(conditional.Body.ToArray());

        using var range = new ControllerFixture("GET");
        range.Context.Request.Headers.Range = "bytes=30-40";
        await range.ExecuteArtifact();
        Assert.Equal(StatusCodes.Status416RangeNotSatisfiable, range.Context.Response.StatusCode);
        Assert.Equal("bytes */10", range.Context.Response.Headers.ContentRange.ToString());
    }

    [Fact]
    public async Task Controller_DoesNotExposeFailureDetailsOrPhysicalPaths()
    {
        var context = new DefaultHttpContext();
        var controller = new ModelDistributionController(new FailureStore())
        {
            ControllerContext = new() { HttpContext = context }
        };
        var result = Assert.IsType<ObjectResult>(await controller.Catalog("Stable", default));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal(ModelDistributionErrors.Busy, error.Code);
        Assert.DoesNotContain("private", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("5", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    private static async Task AssertCode(string expected, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<ModelDistributionException>(action);
        Assert.Equal(expected, error.Code);
    }

    private sealed class RepositoryFixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mds-" + Guid.NewGuid().ToString("N"));
        public ModelDistributionArtifact Artifact { get; } = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.PiperManifestArtifactId);
        public string CatalogPath => Path.Combine(Root, "metadata", "Stable", "catalog.json");
        public byte[] EnvelopeBytes { get; }
        public string EnvelopeHash { get; }

        public RepositoryFixture(bool createCatalog = true, DateTimeOffset? expires = null, bool revoked = false)
        {
            var catalog = new ModelDistributionCatalog(1, "test-repository", "Stable", "win-x64", 10,
                DateTimeOffset.UtcNow.AddDays(-1), expires ?? DateTimeOffset.UtcNow.AddDays(1),
                revoked ? [] : [Artifact], revoked ? [Artifact.Sha256] : null);
            EnvelopeBytes = ModelCatalogSigner.CreateEnvelope(JsonSerializer.SerializeToUtf8Bytes(catalog,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), "test-key", _key);
            EnvelopeHash = Convert.ToHexString(SHA256.HashData(EnvelopeBytes)).ToLowerInvariant();
            Directory.CreateDirectory(Root);
            if (createCatalog)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
                File.WriteAllBytes(CatalogPath, EnvelopeBytes);
            }
        }

        public ModelDistributionOptions Options(Dictionary<string, string>? keys = null,
            Dictionary<string, string>? pins = null, long minimumSequence = 1) => new()
        {
            Enabled = true,
            StorageRoot = Root,
            RepositoryId = "test-repository",
            PublicKeysPem = keys ?? new() { ["test-key"] = _key.ExportSubjectPublicKeyInfoPem() },
            TrustedCatalogSha256ByChannel = pins ?? new() { ["Stable"] = EnvelopeHash },
            MinimumSequence = minimumSequence
        };

        public ModelDistributionStore CreateStore(ModelDistributionOptions? options = null, string? webRoot = null) =>
            new(Microsoft.Extensions.Options.Options.Create(options ?? Options()), new TestEnvironment(Root, webRoot));

        public string WriteArtifact(byte[] bytes)
        {
            var path = Path.Combine(Root, Artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            _key.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string root, string? webRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "model-test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = webRoot ?? Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ControllerFixture : IDisposable
    {
        private readonly string _path = Path.GetTempFileName();
        private readonly ServiceProvider _services;
        public MemoryStream Body { get; } = new();
        public DefaultHttpContext Context { get; } = new();
        public string Hash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("0123456789"))).ToLowerInvariant();

        public ControllerFixture(string method)
        {
            File.WriteAllText(_path, "0123456789", new UTF8Encoding(false));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMvcCore();
            _services = services.BuildServiceProvider();
            Context.Request.Method = method;
            Context.Request.Scheme = "https";
            Context.RequestServices = _services;
            Context.Response.Body = Body;
        }

        public async Task ExecuteArtifact()
        {
            var controller = new ModelDistributionController(new StreamingStore(_path, Hash))
            {
                ControllerContext = new() { HttpContext = Context }
            };
            var result = await controller.Artifact("Stable", Hash, "model.gguf", default);
            await result.ExecuteResultAsync(new ActionContext(Context, new RouteData(), new ActionDescriptor()));
        }

        public void Dispose()
        {
            Body.Dispose();
            _services.Dispose();
            File.Delete(_path);
        }
    }

    private sealed class StreamingStore(string path, string hash) : IModelDistributionStore
    {
        public Task<PublishedModelArtifact> OpenArtifactAsync(string channel, string sha256, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(new PublishedModelArtifact(File.OpenRead(path), hash, fileName));
        public Task<PublishedModelCatalog> GetCatalogAsync(string channel, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailureStore : IModelDistributionStore
    {
        public Task<PublishedModelCatalog> GetCatalogAsync(string channel, CancellationToken cancellationToken) =>
            throw new ModelDistributionException(ModelDistributionErrors.Busy, "C:\\private\\server\\catalog.json");
        public Task<PublishedModelArtifact> OpenArtifactAsync(string channel, string sha256, string fileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
