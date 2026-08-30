using System.Security.Cryptography;
using System.Text;

namespace TOOL_LOCAL.Authentication;

public sealed class DeviceIdentityService
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("TOOL_GEN_POST_VIDEO/Device/v1");
    private readonly string _identityPath;

    public DeviceIdentityService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo");
        _identityPath = Path.Combine(folder, "device-id.bin");
    }

    public string GetOrCreateFingerprint()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_identityPath)!);

        if (File.Exists(_identityPath))
        {
            try
            {
                var encrypted = File.ReadAllBytes(_identityPath);
                var plain = ProtectedData.Unprotect(encrypted, OptionalEntropy, DataProtectionScope.CurrentUser);
                var existing = Encoding.UTF8.GetString(plain);
                if (Guid.TryParse(existing, out _))
                {
                    return existing;
                }
            }
            catch (CryptographicException)
            {
                // Replace an unreadable identity owned by another Windows profile.
            }
        }

        var fingerprint = Guid.NewGuid().ToString("D");
        var protectedValue = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(fingerprint),
            OptionalEntropy,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_identityPath, protectedValue);
        return fingerprint;
    }
}
