using System.Runtime.CompilerServices;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

[assembly: InternalsVisibleTo("TOOL-TESTS")]

namespace TOOL_SHARED.Distribution.StaticModels;

/// <summary>Release-bound metadata. There is intentionally no public constructor or JSON loader.</summary>
public sealed class StaticModelBundleDescriptor
{
    internal StaticModelBundleDescriptor(string bundleId, Uri archiveUrl, long archiveSizeBytes,
        string archiveSha256, string inventorySha256, long maximumExpandedBytes,
        IReadOnlyList<ModelDistributionArtifact> artifacts, int maximumEntryCount = 64)
    {
        if (string.IsNullOrWhiteSpace(bundleId) || bundleId.Length > 100 ||
            bundleId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.') ||
            !archiveUrl.IsAbsoluteUri || archiveUrl.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(archiveUrl.UserInfo) || !string.IsNullOrEmpty(archiveUrl.Query) ||
            !string.IsNullOrEmpty(archiveUrl.Fragment) || archiveSizeBytes is <= 0 or > 16_000_000_000 ||
            maximumExpandedBytes is <= 0 or > 16_000_000_000 || maximumEntryCount is < 3 or > 128 ||
            !StaticModelFileSafety.IsHash(archiveSha256) || !StaticModelFileSafety.IsHash(inventorySha256) ||
            artifacts.Count != 3 || artifacts.Select(a => a.ArtifactId).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("Định nghĩa gói model không hợp lệ.");
        BundleId = bundleId;
        ArchiveUrl = archiveUrl;
        ArchiveSizeBytes = archiveSizeBytes;
        ArchiveSha256 = archiveSha256;
        InventorySha256 = inventorySha256;
        MaximumExpandedBytes = maximumExpandedBytes;
        MaximumEntryCount = maximumEntryCount;
        Artifacts = Array.AsReadOnly(artifacts.ToArray());
        // This deliberately requires all three artifacts, even for an internally injected test descriptor.
        _ = GetArtifact(ApprovedModelArtifactPins.QwenArtifactId);
        _ = GetArtifact(ApprovedModelArtifactPins.PiperArchiveArtifactId);
        _ = GetArtifact(ApprovedModelArtifactPins.PiperManifestArtifactId);
    }

    public string BundleId { get; }
    public Uri ArchiveUrl { get; }
    public long ArchiveSizeBytes { get; }
    public string ArchiveSha256 { get; }
    public string InventorySha256 { get; }
    public long MaximumExpandedBytes { get; }
    public int MaximumEntryCount { get; }
    public string RequiredWorkerVersion => "1.2.1";
    public int RequiredWorkerProtocolVersion => 3;
    public IReadOnlyList<ModelDistributionArtifact> Artifacts { get; }

    public void ValidateDesktopCompatibility(string desktopVersion, int desktopBuildNumber,
        string workerVersion, int workerProtocolVersion)
    {
        if (!Version.TryParse(desktopVersion, out var desktop) || desktopBuildNumber <= 0 ||
            workerVersion != RequiredWorkerVersion || workerProtocolVersion != RequiredWorkerProtocolVersion)
            throw new ModelDistributionException(ModelDistributionErrors.Incompatible, "Ứng dụng hoặc worker không tương thích bộ model đã chọn.");
        foreach (var artifact in Artifacts)
        {
            if (!Version.TryParse(artifact.MinimumDesktopVersion, out var minimum) || desktop < minimum ||
                (desktop == minimum && desktopBuildNumber < artifact.MinimumDesktopBuildNumber))
                throw new ModelDistributionException(ModelDistributionErrors.Incompatible, "Phiên bản ứng dụng chưa hỗ trợ bộ model đã chọn.");
        }
    }

    internal ModelDistributionArtifact GetArtifact(string id) => Artifacts.Single(a => a.ArtifactId == id);

    internal VerifiedStaticModelPayload Describe(string cacheRoot)
    {
        var root = Path.Combine(Path.GetFullPath(cacheRoot), ArchiveSha256, "payload");
        return new(Path.Combine(root, "qwen", GetArtifact(ApprovedModelArtifactPins.QwenArtifactId).FileName),
            Path.Combine(root, "piper"), BundleId) { PayloadDirectory = root };
    }
}

public static class ApprovedStaticModelBundle
{
    public static StaticModelBundleDescriptor Current { get; } = new(
        "models-20261016-03",
        new Uri("https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip"),
        2_847_980_994,
        "e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471",
        "2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843",
        3_100_000_000, ApprovedModelArtifactPins.All);

    /// <summary>Resolves native cache paths only. This never attests installation or readiness.</summary>
    public static VerifiedStaticModelPayload DescribeCachedPayload(string cacheRoot) => Current.Describe(cacheRoot);
}

/// <summary>Native-only result. Do not serialize local paths into a WebView or server response.</summary>
public sealed record VerifiedStaticModelPayload(string QwenModelPath, string PiperBundleDirectory, string BundleId)
{
    public required string PayloadDirectory { get; init; }
}
