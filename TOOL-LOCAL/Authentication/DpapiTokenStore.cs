using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_LOCAL.Authentication;

public sealed class DpapiTokenStore : ITokenStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("TOOL_GEN_POST_VIDEO/AuthToken/v1");
    private readonly string _tokenPath;

    public DpapiTokenStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo");
        _tokenPath = Path.Combine(folder, "auth-token.bin");
    }

    public async Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_tokenPath))
        {
            return null;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_tokenPath, cancellationToken);
            var plain = ProtectedData.Unprotect(encrypted, OptionalEntropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredRefreshToken>(plain);
        }
        catch (CryptographicException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
        catch (JsonException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
    }

    public async Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(_tokenPath)!;
        Directory.CreateDirectory(folder);

        var plain = JsonSerializer.SerializeToUtf8Bytes(token);
        var encrypted = ProtectedData.Protect(plain, OptionalEntropy, DataProtectionScope.CurrentUser);
        var temporaryPath = _tokenPath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken);
        File.Move(temporaryPath, _tokenPath, true);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_tokenPath))
        {
            File.Delete(_tokenPath);
        }

        return Task.CompletedTask;
    }
}
