using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_MODEL_PUBLISHER;

public sealed record ModelRevokeRequest(string CatalogPath, string CatalogEnvelopeSha256, string PublicTrustRootPath,
    string OutputRoot, long Sequence, IReadOnlyList<string> RevokeSha256, string KeyId, string PrivateKeyPath, int ValidDays = 7);

public sealed record ModelRevokeResult(string RepositoryId, string Channel, long PreviousSequence, long Sequence,
    string SourceCatalogEnvelopeSha256, string CatalogEnvelopeSha256, IReadOnlyList<string> RevokedArtifactSha256, bool Published);

public static partial class ModelPublisher
{
    /// <summary>Creates metadata in a NEW directory; it never edits a live repository or existing payloads.</summary>
    public static async Task<ModelRevokeResult> RevokeAsync(ModelRevokeRequest request, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(request.CatalogPath) || !Path.IsPathFullyQualified(request.PublicTrustRootPath) ||
            !Path.IsPathFullyQualified(request.OutputRoot) || !Path.IsPathFullyQualified(request.PrivateKeyPath))
            throw Failure("absolute_paths_required");
        if (!Hash(request.CatalogEnvelopeSha256) || !Identifier(request.KeyId) || request.Sequence < 1 || request.ValidDays is < 1 or > 30 ||
            request.RevokeSha256 is null || request.RevokeSha256.Count is < 1 or > 3 || request.RevokeSha256.Any(hash => !Hash(hash)) ||
            request.RevokeSha256.Distinct(StringComparer.Ordinal).Count() != request.RevokeSha256.Count)
            throw Failure("arguments_invalid");

        var sourcePath = Path.GetFullPath(request.CatalogPath);
        var sourceRoot = CatalogSourceRoot(sourcePath);
        var output = Path.GetFullPath(request.OutputRoot);
        if (Directory.Exists(output) || File.Exists(output) || Contains(sourceRoot, output) || Contains(output, sourceRoot) ||
            Contains(output, request.PublicTrustRootPath)) throw Failure("output_exists_or_overlaps");
        RejectLinks(output, allowMissing: true);

        var envelope = await ReadSmallFileAsync(sourcePath, ModelCatalogVerifier.MaximumEnvelopeBytes, token);
        if (!Matches(SHA256.HashData(envelope), request.CatalogEnvelopeSha256)) throw Failure("catalog_hash_mismatch");
        var trustBytes = await ReadSmallFileAsync(request.PublicTrustRootPath, 64 * 1024, token);
        var trust = JsonSerializer.Deserialize<PublicRoot>(trustBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 5
        });
        if (trust is not { SchemaVersion: 1 } || !Identifier(trust.RepositoryId) || trust.PublicKeys is null ||
            !trust.PublicKeys.TryGetValue(request.KeyId, out var signingPublicKey)) throw Failure("signing_key_not_trusted");

        // The channel comes from the signed payload. Try only the four channels compiled into the verifier.
        VerifiedModelCatalog? current = null;
        foreach (var channel in new[] { "Stable", "Beta", "Development", "Staging" })
        {
            try
            {
                current = ModelCatalogVerifier.Verify(envelope, new(trust.RepositoryId, channel, "win-x64", trust.PublicKeys), null,
                    DateTimeOffset.UtcNow);
                break;
            }
            catch (ModelDistributionException) { }
        }
        if (current is null) throw Failure("source_catalog_untrusted");
        if (request.Sequence <= current.Catalog.Sequence) throw Failure("revocation_sequence_not_increasing");

        var approved = ApprovedModelArtifactPins.All.Select(item => item.Sha256).ToHashSet(StringComparer.Ordinal);
        if (request.RevokeSha256.Any(hash => !approved.Contains(hash))) throw Failure("revocation_hash_not_approved");
        var revoked = (current.Catalog.RevokedArtifactSha256 ?? []).ToHashSet(StringComparer.Ordinal);
        if (request.RevokeSha256.All(hash => revoked.Contains(hash))) throw Failure("revocation_no_change");
        revoked.UnionWith(request.RevokeSha256);

        using var key = LoadExternalPrivateKey(request.PrivateKeyPath, sourceRoot, output);
        // A key ID is not enough: the provided signing key must match the pinned public key bytes.
        using var expected = ECDsa.Create();
        expected.ImportFromPem(signingPublicKey);
        if (!CryptographicOperations.FixedTimeEquals(expected.ExportSubjectPublicKeyInfo(), key.ExportSubjectPublicKeyInfo()))
            throw Failure("signing_key_not_trusted");
        if (Contains(Path.GetDirectoryName(Path.GetFullPath(request.PublicTrustRootPath))!, request.PrivateKeyPath))
            throw Failure("private_key_must_be_external");

        var now = DateTimeOffset.UtcNow;
        var next = current.Catalog with
        {
            Sequence = request.Sequence, PublishedAtUtc = now, ExpiresAtUtc = now.AddDays(request.ValidDays),
            Artifacts = current.Catalog.Artifacts.Where(artifact => !revoked.Contains(artifact.Sha256)).ToArray(),
            RevokedArtifactSha256 = revoked.OrderBy(hash => hash, StringComparer.Ordinal).ToArray()
        };
        var resultEnvelope = ModelCatalogSigner.CreateEnvelope(JsonSerializer.SerializeToUtf8Bytes(next, Json), request.KeyId, key);
        ModelCatalogVerifier.Verify(resultEnvelope, new(trust.RepositoryId, next.Channel, "win-x64", trust.PublicKeys),
            current.NextState, now);
        var hash = Convert.ToHexString(SHA256.HashData(resultEnvelope)).ToLowerInvariant();
        var result = new ModelRevokeResult(next.RepositoryId, next.Channel, current.Catalog.Sequence, next.Sequence,
            request.CatalogEnvelopeSha256, hash, next.RevokedArtifactSha256, Published: false);
        if (Directory.Exists(output) || File.Exists(output)) throw Failure("output_exists");
        Directory.CreateDirectory(output);
        await WriteNewAsync(Path.Combine(output, ".incomplete"), Encoding.UTF8.GetBytes("Metadata staging only. Not published."), token);
        await WriteNewAsync(Path.Combine(output, "public-trust-root.json"), trustBytes, token);
        await WriteNewAsync(Path.Combine(output, "operation-records", "revocation-result.json"), JsonSerializer.SerializeToUtf8Bytes(result, Json), token);
        await WriteNewAsync(Path.Combine(output, "operation-records", "previous-catalog.json"), envelope, token);
        await WriteNewAsync(Path.Combine(output, "server-options.example.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            ModelDistribution = new
            {
                Enabled = false, StorageRoot = "<existing-verified-artifact-repository-root>", RepositoryId = trust.RepositoryId,
                Platform = "win-x64", AllowedChannels = new[] { next.Channel }, PublicKeysPem = trust.PublicKeys,
                TrustedCatalogSha256ByChannel = new Dictionary<string, string> { [next.Channel] = hash }, MinimumSequence = next.Sequence
            }
        }, new JsonSerializerOptions { WriteIndented = true }), token);
        await WriteNewAsync(ResolveUnder(output, $"metadata/{next.Channel}/catalog.json", allowMissing: true), resultEnvelope, token);
        File.Delete(Path.Combine(output, ".incomplete"));
        return result;
    }

    private static string CatalogSourceRoot(string catalogPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(catalogPath)!);
        return Path.GetFileName(catalogPath) == "catalog.json" && directory.Parent?.Name == "metadata" && directory.Parent.Parent is { } root
            ? root.FullName : directory.FullName;
    }

    private sealed record PublicRoot(int SchemaVersion, string RepositoryId, Dictionary<string, string> PublicKeys);
}
