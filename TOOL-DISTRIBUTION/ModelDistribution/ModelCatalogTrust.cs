using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TOOL_SHARED.Contracts.ModelDistribution;

namespace TOOL_SHARED.Distribution.ModelDistribution;

public sealed record ModelCatalogTrustPolicy(string RepositoryId, string Channel, string Platform,
    IReadOnlyDictionary<string, string> PublicKeys);

public sealed record ModelCatalogTrustState(string RepositoryId, string Channel, string Platform,
    long Sequence, string CatalogSha256, IReadOnlyList<string>? RevokedArtifactSha256 = null);

public sealed record VerifiedModelCatalog(ModelDistributionCatalog Catalog, string CatalogSha256,
    ModelCatalogTrustState NextState);

/// <summary>
/// Application-specific exact-byte signature protocol, not a TUF implementation.
/// Callers must persist NextState atomically under their repository lock before using new metadata.
/// </summary>
public static class ModelCatalogVerifier
{
    public const int MaximumEnvelopeBytes = 128 * 1024;
    public const int MaximumPayloadBytes = 64 * 1024;
    public static readonly TimeSpan MaximumCatalogValidity = TimeSpan.FromDays(30);
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };

    public static VerifiedModelCatalog Verify(ReadOnlySpan<byte> envelopeBytes, ModelCatalogTrustPolicy policy,
        ModelCatalogTrustState? previous, DateTimeOffset now)
    {
        ValidatePolicy(policy);
        if (envelopeBytes.Length is <= 0 or > MaximumEnvelopeBytes) throw Invalid();
        var envelope = Parse<ModelDistributionEnvelope>(envelopeBytes);
        if (envelope.SchemaVersion != 1 || !Identifier(envelope.KeyId)
            || envelope.Algorithm != ModelCatalogSigner.Algorithm || string.IsNullOrEmpty(envelope.PayloadBase64)
            || envelope.PayloadBase64.Length > (MaximumPayloadBytes + 2) / 3 * 4
            || string.IsNullOrEmpty(envelope.SignatureBase64) || envelope.SignatureBase64.Length != 88)
            throw Invalid();
        if (!policy.PublicKeys.TryGetValue(envelope.KeyId, out var publicKey)) throw SignatureInvalid();

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Convert.FromBase64String(envelope.PayloadBase64);
            signature = Convert.FromBase64String(envelope.SignatureBase64);
        }
        catch (FormatException) { throw Invalid(); }
        if (payload.Length is <= 0 or > MaximumPayloadBytes || signature.Length != 64) throw Invalid();
        VerifySignature(payload, signature, publicKey);

        // Parse the SAME authenticated bytes, with duplicate members and unknown fields rejected.
        var catalog = Parse<ModelDistributionCatalog>(payload);
        ValidateCatalog(catalog, policy, now);
        var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        if (previous is not null)
        {
            ValidateTrustState(previous, policy);
            if (catalog.Sequence < previous.Sequence
                || (catalog.Sequence == previous.Sequence && digest != previous.CatalogSha256))
                throw new ModelDistributionException(ModelDistributionErrors.CatalogRollback,
                    "Danh mục model bị lùi phiên bản hoặc thay nội dung của phiên bản đã xác thực.");
        }
        var revoked = new HashSet<string>(StringComparer.Ordinal);
        AddRevocations(previous?.RevokedArtifactSha256, revoked);
        AddRevocations(catalog.RevokedArtifactSha256, revoked);
        if (catalog.Artifacts.Any(artifact => revoked.Contains(artifact.Sha256)))
            throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotApproved,
                "Thành phần đã bị thu hồi và không được phép cài lại.");
        IReadOnlyList<string>? knownRevocations = revoked.Count == 0 ? null
            : Array.AsReadOnly(revoked.Order(StringComparer.Ordinal).ToArray());
        return new(catalog, digest, new(policy.RepositoryId, policy.Channel, policy.Platform, catalog.Sequence,
            digest, knownRevocations));
    }

    /// <summary>Validates local high-water state even when downloads and public-key use are disabled.</summary>
    public static void ValidateTrustState(ModelCatalogTrustState state, ModelCatalogTrustPolicy policy)
    {
        if (state is null || policy is null || state.RepositoryId != policy.RepositoryId
            || state.Channel != policy.Channel || state.Platform != policy.Platform || state.Sequence <= 0
            || !Hash(state.CatalogSha256)) throw Invalid();
        AddRevocations(state.RevokedArtifactSha256, new(StringComparer.Ordinal));
    }

    private static void ValidatePolicy(ModelCatalogTrustPolicy policy)
    {
        if (policy is null || !Identifier(policy.RepositoryId) || policy.Channel is not ("Stable" or "Beta" or "Development" or "Staging")
            || policy.Platform != "win-x64" || policy.PublicKeys is null || policy.PublicKeys.Count is < 1 or > 16)
            throw new ModelDistributionException(ModelDistributionErrors.TrustNotConfigured,
                "Chưa cấu hình gốc tin cậy cho kho model.");
        foreach (var key in policy.PublicKeys)
            if (!Identifier(key.Key) || string.IsNullOrEmpty(key.Value) || key.Value.Length > 4096
                || !key.Value.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
                throw new ModelDistributionException(ModelDistributionErrors.TrustNotConfigured,
                    "Cấu hình khóa công khai của kho model không hợp lệ.");
    }

    private static void VerifySignature(byte[] payload, byte[] signature, string pem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7"
                || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw SignatureInvalid();
        }
        catch (CryptographicException) { throw SignatureInvalid(); }
        catch (ArgumentException) { throw SignatureInvalid(); }
    }

    private static void ValidateCatalog(ModelDistributionCatalog catalog, ModelCatalogTrustPolicy policy,
        DateTimeOffset now)
    {
        if (catalog.SchemaVersion != 1 || catalog.RepositoryId != policy.RepositoryId
            || catalog.Channel != policy.Channel || catalog.Platform != policy.Platform || catalog.Sequence <= 0
            || catalog.PublishedAtUtc == default || catalog.ExpiresAtUtc == default
            || catalog.PublishedAtUtc.Offset != TimeSpan.Zero || catalog.ExpiresAtUtc.Offset != TimeSpan.Zero
            || catalog.ExpiresAtUtc <= catalog.PublishedAtUtc
            || catalog.ExpiresAtUtc - catalog.PublishedAtUtc > MaximumCatalogValidity
            || catalog.PublishedAtUtc - now > AllowedClockSkew || catalog.Artifacts is null
            || catalog.Artifacts.Count > ApprovedModelArtifactPins.All.Count) throw Invalid();
        if (catalog.ExpiresAtUtc <= now)
            throw new ModelDistributionException(ModelDistributionErrors.CatalogExpired,
                "Danh mục model đã hết hiệu lực; cần lấy danh mục hợp lệ mới.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in catalog.Artifacts)
        {
            ApprovedModelArtifactPins.Validate(artifact);
            if (!ids.Add(artifact.ArtifactId) || !paths.Add(artifact.RelativePath)
                || string.IsNullOrEmpty(artifact.MinimumDesktopVersion)
                || !Regex.IsMatch(artifact.MinimumDesktopVersion,
                    @"^\d{1,5}\.\d{1,5}\.\d{1,5}(?:\.\d{1,5})?(?:-[a-zA-Z0-9.-]{1,40})?$", RegexOptions.CultureInvariant)
                || artifact.MinimumDesktopBuildNumber <= 0 || artifact.MinimumFreeDiskBytes > 64L * 1024 * 1024 * 1024)
                throw Invalid();
        }
        if (catalog.RevokedArtifactSha256 is { } revoked)
        {
            if (revoked.Count > ApprovedModelArtifactPins.All.Count) throw Invalid();
            var known = ApprovedModelArtifactPins.All.Select(item => item.Sha256).ToHashSet(StringComparer.Ordinal);
            var active = catalog.Artifacts.Select(item => item.Sha256).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hash in revoked)
                if (!Hash(hash) || !known.Contains(hash) || !seen.Add(hash) || active.Contains(hash)) throw Invalid();
        }
    }

    private static T Parse<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 12 });
            RejectDuplicateMembers(document.RootElement);
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                RejectDuplicateMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateMembers(item);
    }

    private static void AddRevocations(IReadOnlyList<string>? entries, HashSet<string> target)
    {
        if (entries is null) return;
        if (entries.Count > ApprovedModelArtifactPins.All.Count) throw Invalid();
        var known = ApprovedModelArtifactPins.All.Select(item => item.Sha256).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hash in entries)
        {
            if (!Hash(hash) || !known.Contains(hash) || !seen.Add(hash)) throw Invalid();
            target.Add(hash);
        }
    }

    private static bool Identifier(string? value) => value is { Length: > 0 and <= 100 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static bool Hash(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static ModelDistributionException Invalid() => new(ModelDistributionErrors.CatalogInvalid,
        "Danh mục model không hợp lệ.");
    private static ModelDistributionException SignatureInvalid() => new(ModelDistributionErrors.SignatureInvalid,
        "Không xác thực được chữ ký danh mục model.");
}

/// <summary>Signing requires a caller-owned key; no private key is generated, loaded or persisted here.</summary>
public static class ModelCatalogSigner
{
    public const string Algorithm = "ECDSA-P256-SHA256-P1363";

    public static byte[] CreateEnvelope(ReadOnlySpan<byte> payloadBytes, string keyId, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (payloadBytes.Length is <= 0 or > ModelCatalogVerifier.MaximumPayloadBytes
            || string.IsNullOrEmpty(keyId) || keyId.Length > 100
            || !keyId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new ArgumentException("Đầu vào ký danh mục model không hợp lệ.");
        var signature = key.SignData(payloadBytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.SerializeToUtf8Bytes(new ModelDistributionEnvelope(1, keyId, Algorithm,
            Convert.ToBase64String(payloadBytes), Convert.ToBase64String(signature)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
