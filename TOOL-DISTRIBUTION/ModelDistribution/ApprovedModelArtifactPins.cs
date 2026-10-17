using TOOL_SHARED.Contracts.ModelDistribution;

namespace TOOL_SHARED.Distribution.ModelDistribution;

/// <summary>Release approved bytes. A signed catalog cannot introduce new code or models.</summary>
public static class ApprovedModelArtifactPins
{
    public const string QwenArtifactId = "qwen3-4b-q4-k-m-cpu";
    public const string QwenComponentVersion = "bc64014-llamasharp-0.27.0-adapter-1";
    public const string PiperComponentId = "piper-vi-vais1000-medium";
    public const string PiperComponentVersion = "piper-1.6.0-python-3.11.15-offline-v3";
    public const string PiperArchiveArtifactId = "piper-offline-archive";
    public const string PiperManifestArtifactId = "piper-offline-manifest";

    public static IReadOnlyList<ModelDistributionArtifact> All { get; } = Array.AsReadOnly(new[]
    {
        Create(QwenArtifactId, QwenArtifactId, QwenComponentVersion, "Qwen3-4B-Q4_K_M.gguf",
            2_497_280_256, "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5",
            2_497_280_256 + 768L * 1024 * 1024, "1.2.1", 3),
        Create(PiperArchiveArtifactId, PiperComponentId, PiperComponentVersion, "piper-offline.zip",
            159_970_075, "e2d1b5f212d2cc6a3b547f9280e380416c57c5c151d639595009624caf4f6e1d", 727_344_098),
        Create(PiperManifestArtifactId, PiperComponentId, PiperComponentVersion, "manifest.json",
            977_507, "69bfe70e9ef038f2c157db035cf6f59ae8310028359d93b1dc16de73c691f1b5", 727_344_098)
    });

    public static ModelDistributionArtifact Get(string artifactId) => All.FirstOrDefault(
        item => string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal))
        ?? throw Rejected();

    public static void Validate(ModelDistributionArtifact artifact)
    {
        if (artifact is null) throw Rejected();
        var approved = Get(artifact.ArtifactId);
        if (artifact.ComponentId != approved.ComponentId || artifact.ComponentVersion != approved.ComponentVersion
            || artifact.FileName != approved.FileName || artifact.RelativePath != approved.RelativePath
            || artifact.SizeBytes != approved.SizeBytes || artifact.Sha256 != approved.Sha256
            || artifact.MinimumFreeDiskBytes < approved.MinimumFreeDiskBytes
            || artifact.RequiredWorkerVersion != approved.RequiredWorkerVersion
            || artifact.RequiredWorkerProtocolVersion != approved.RequiredWorkerProtocolVersion)
            throw Rejected();
    }

    private static ModelDistributionArtifact Create(string artifactId, string componentId, string version,
        string fileName, long size, string hash, long disk, string? worker = null, int? protocol = null) =>
        new(artifactId, componentId, version, fileName, $"artifacts/{componentId}/{version}/{hash}/{fileName}",
            size, hash, disk, "1.0.0", 1, worker, protocol);

    private static ModelDistributionException Rejected() => new(ModelDistributionErrors.ArtifactNotApproved,
        "Thành phần không khớp bộ model đã được ứng dụng phê duyệt.");
}
