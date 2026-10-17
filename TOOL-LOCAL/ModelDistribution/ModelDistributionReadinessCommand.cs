using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Configuration;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_LOCAL.ModelDistribution;

internal static class ModelDistributionReadinessCommand
{
    internal static int Run()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var options = DesktopOptions.Load(AppContext.BaseDirectory, preferencesDirectory: null, requireDatabase: false);
            if (!options.ModelDistribution.Enabled) throw new InvalidDataException("disabled");
            var origin = new Uri(options.ModelDistribution.BaseUrl ?? options.Server.BaseUrl);
            if (origin.Scheme != "https" || origin.Port != 443 || !string.IsNullOrEmpty(origin.UserInfo) ||
                !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment)) throw new InvalidDataException("origin");
            var root = EmbeddedModelTrustRoot.Load(options.Update.Channel);
            foreach (var key in root.PublicKeys)
            {
                if (!key.Value.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) || key.Value.Contains("PRIVATE KEY"))
                    throw new InvalidDataException("key");
                using var verifier = ECDsa.Create();
                verifier.ImportFromPem(key.Value);
                if (verifier.KeySize != 256 || verifier.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
                    throw new InvalidDataException("curve");
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ModelDistributionConfigured = true, root.RepositoryId, PublicKeyCount = root.PublicKeys.Count,
                TrustRootSha256 = Convert.ToHexString(SHA256.HashData(EmbeddedModelTrustRoot.ReadBytes())).ToLowerInvariant(),
                NetworkRequests = 0
            }));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or CryptographicException or
            JsonException or ModelDistributionException or InvalidOperationException)
        {
            Console.WriteLine("{\"ModelDistributionConfigured\":false,\"NetworkRequests\":0}");
            return 1;
        }
    }
}
