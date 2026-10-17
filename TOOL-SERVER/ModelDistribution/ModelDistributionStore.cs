using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_SERVER.ModelDistribution;

public interface IModelDistributionStore
{
    Task<PublishedModelCatalog> GetCatalogAsync(string channel, CancellationToken cancellationToken);
    Task<PublishedModelArtifact> OpenArtifactAsync(string channel, string sha256, string fileName, CancellationToken cancellationToken);
}

public sealed record PublishedModelCatalog(byte[] EnvelopeBytes, string EnvelopeSha256, ModelDistributionCatalog Catalog);

public sealed record PublishedModelArtifact(FileStream Stream, string Sha256, string FileName);

public sealed class ModelDistributionStore : IModelDistributionStore, IDisposable
{
    private readonly ModelDistributionOptions _options;
    private readonly string _root;
    private readonly string? _webRoot;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _verificationGate = new(1, 1);

    public ModelDistributionStore(IOptions<ModelDistributionOptions> options, IWebHostEnvironment environment, TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(_options.StorageRoot)
            ? _options.StorageRoot
            : Path.Combine(environment.ContentRootPath, _options.StorageRoot)));
        _webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath) ? null : Path.GetFullPath(environment.WebRootPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        // Do not create directories or mutate the repository from the serving process.
    }

    public async Task<PublishedModelCatalog> GetCatalogAsync(string channel, CancellationToken cancellationToken)
    {
        EnsureConfigured(channel);
        try
        {
            var path = ModelRepositoryPaths.Resolve(_root, $"metadata/{channel}/catalog.json");
            await using var stream = OpenRead(path);
            if (stream.Length <= 0 || stream.Length > _options.MaximumCatalogBytes)
            {
                throw Failure(ModelDistributionErrors.CatalogInvalid);
            }

            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            ModelRepositoryPaths.RejectReparsePoints(path);
            var envelopeHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var pin = _options.TrustedCatalogSha256ByChannel[channel];
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pin), Convert.FromHexString(envelopeHash)))
            {
                throw Failure(ModelDistributionErrors.CatalogInvalid);
            }

            var verified = ModelCatalogVerifier.Verify(bytes,
                new ModelCatalogTrustPolicy(_options.RepositoryId, channel, _options.Platform, _options.PublicKeysPem),
                previous: null, _timeProvider.GetUtcNow());
            if (verified.Catalog.Sequence < _options.MinimumSequence ||
                verified.Catalog.ExpiresAtUtc - verified.Catalog.PublishedAtUtc > TimeSpan.FromDays(_options.MaximumCatalogValidityDays) ||
                verified.Catalog.Artifacts.Any(artifact => artifact.SizeBytes > _options.MaximumArtifactBytes))
            {
                throw Failure(ModelDistributionErrors.CatalogInvalid);
            }

            return new PublishedModelCatalog(bytes, envelopeHash, verified.Catalog);
        }
        catch (ModelDistributionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            // Paths and exception details must never reach an anonymous download response.
            throw Failure(ModelDistributionErrors.CatalogInvalid);
        }
    }

    public async Task<PublishedModelArtifact> OpenArtifactAsync(
        string channel, string sha256, string fileName, CancellationToken cancellationToken)
    {
        EnsureConfigured(channel);
        if (!ModelRepositoryPaths.IsSha256(sha256) || !ModelRepositoryPaths.IsSafeSegment(fileName))
        {
            throw Failure(ModelDistributionErrors.ArtifactNotFound);
        }

        var catalog = await GetCatalogAsync(channel, cancellationToken);
        var artifact = catalog.Catalog.Artifacts.SingleOrDefault(candidate =>
            candidate.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase) &&
            candidate.FileName.Equals(fileName, StringComparison.Ordinal));
        if (artifact is null)
        {
            throw Failure(ModelDistributionErrors.ArtifactNotFound);
        }

        // A range or HEAD request cannot create an unbounded queue of multi-GB hash scans.
        if (!await _verificationGate.WaitAsync(0, cancellationToken))
        {
            throw Failure(ModelDistributionErrors.Busy);
        }

        FileStream? stream = null;
        try
        {
            if (!artifact.RelativePath.StartsWith("artifacts/", StringComparison.Ordinal))
            {
                throw Failure(ModelDistributionErrors.ArtifactInvalid);
            }

            var path = ModelRepositoryPaths.Resolve(_root, artifact.RelativePath);
            stream = OpenRead(path);
            if (stream.Length != artifact.SizeBytes || stream.Length > _options.MaximumArtifactBytes)
            {
                throw Failure(ModelDistributionErrors.ArtifactInvalid);
            }

            var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(artifact.Sha256)))
            {
                throw Failure(ModelDistributionErrors.ArtifactInvalid);
            }

            // Keep the verified handle for MVC; do not reopen a pathname after verification.
            ModelRepositoryPaths.RejectReparsePoints(path);
            stream.Position = 0;
            var response = new PublishedModelArtifact(stream, artifact.Sha256.ToLowerInvariant(), artifact.FileName);
            stream = null;
            return response;
        }
        catch (ModelDistributionException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw Failure(ModelDistributionErrors.ArtifactNotFound);
        }
        catch (DirectoryNotFoundException)
        {
            throw Failure(ModelDistributionErrors.ArtifactNotFound);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            throw Failure(ModelDistributionErrors.ArtifactInvalid);
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
            _verificationGate.Release();
        }
    }

    private void EnsureConfigured(string channel)
    {
        if (!_options.Enabled)
        {
            throw Failure(ModelDistributionErrors.Disabled);
        }

        if (!ModelRepositoryPaths.IsSafeSegment(channel) || !_options.AllowedChannels.Contains(channel, StringComparer.Ordinal))
        {
            throw Failure(ModelDistributionErrors.ArtifactNotFound);
        }

        if (_options.PublicKeysPem.Count == 0 || string.IsNullOrWhiteSpace(_options.RepositoryId) ||
            _options.Platform != "win-x64" ||
            !_options.TrustedCatalogSha256ByChannel.TryGetValue(channel, out var hash) || !ModelRepositoryPaths.IsSha256(hash) ||
            _options.MinimumSequence < 1 || _options.MaximumCatalogValidityDays is < 1 or > 90 ||
            _options.MaximumCatalogBytes is < 1024 or > 16 * 1024 * 1024 || _options.MaximumArtifactBytes <= 0 ||
            (_webRoot is not null && (ModelRepositoryPaths.IsWithin(_root, _webRoot) ||
                _root.Equals(Path.TrimEndingDirectorySeparator(_webRoot), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
        {
            throw Failure(ModelDistributionErrors.TrustNotConfigured);
        }
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static ModelDistributionException Failure(string code) => new(code, "Kho model chưa có thành phần đã xác minh để tải. Hãy thử lại hoặc liên hệ quản trị viên.");

    public void Dispose() => _verificationGate.Dispose();
}
