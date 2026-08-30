namespace TOOL_LOCAL.Authentication;

public interface ITokenStore
{
    Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record StoredRefreshToken(string RefreshToken, DateTime ExpiresAtUtc);
