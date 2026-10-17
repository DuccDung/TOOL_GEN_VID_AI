using System.Reflection;
using System.Text.Json;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_LOCAL.ModelDistribution;

public sealed class DesktopModelDistributionOptions
{
    public string Mode { get; init; } = "Server";
    public bool Enabled { get; init; }
    public string? BaseUrl { get; init; }
    public string? CacheRoot { get; init; }
}

internal sealed record EmbeddedModelTrustRoot(int SchemaVersion, string RepositoryId,
    Dictionary<string, string> PublicKeys)
{
    internal static byte[] ReadBytes()
    {
        using var source = typeof(EmbeddedModelTrustRoot).Assembly.GetManifestResourceStream("ModelDistribution.TrustRoot")
            ?? throw new InvalidDataException("Bản ứng dụng thiếu gốc tin cậy model.");
        if (source.Length > 64 * 1024) throw new InvalidDataException("Gốc tin cậy model không hợp lệ.");
        var bytes = new byte[checked((int)source.Length)];
        source.ReadExactly(bytes);
        return bytes;
    }

    internal static ModelCatalogTrustPolicy Load(string channel, bool requirePublicKeys = true)
    {
        using var stream = typeof(EmbeddedModelTrustRoot).Assembly
            .GetManifestResourceStream("ModelDistribution.TrustRoot")
            ?? throw new ModelDistributionException(ModelDistributionErrors.TrustNotConfigured,
                "Bản ứng dụng chưa có gốc tin cậy cho kho model.");
        if (stream.Length > 64 * 1024) throw new InvalidDataException("Gốc tin cậy model không hợp lệ.");
        var root = JsonSerializer.Deserialize<EmbeddedModelTrustRoot>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (root is not { SchemaVersion: 1 } || root.PublicKeys is null
            || root.RepositoryId is not { Length: > 0 and <= 100 }
            || !root.RepositoryId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            || channel is not ("Stable" or "Beta" or "Development" or "Staging")
            || (requirePublicKeys && root.PublicKeys.Count == 0))
            throw new ModelDistributionException(ModelDistributionErrors.TrustNotConfigured,
                "Bản ứng dụng chưa cấu hình khóa công khai của kho model.");
        return new(root.RepositoryId, channel, "win-x64", root.PublicKeys);
    }
}
