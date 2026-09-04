using TOOL_SERVER.Authentication;
using TOOL_SERVER.Domain.Accounts;

namespace TOOL_TESTS.Authentication;

public sealed class AuthRefreshPolicyTests
{
    [Fact]
    public void ExpiredLicense_DoesNotInvalidateOtherwiseActiveRefreshContext()
    {
        var now = DateTime.UtcNow;
        var token = ValidRefreshToken(now);
        var expiredLicense = new UserLicense
        {
            Status = "Active",
            StartsAtUtc = now.AddDays(-31),
            ExpiresAtUtc = now.AddDays(-1)
        };

        Assert.True(expiredLicense.ExpiresAtUtc <= now);
        Assert.True(AuthService.HasValidRefreshContext(token, now));
    }

    [Theory]
    [InlineData(true, SessionStatuses.Active)]
    [InlineData(false, SessionStatuses.Revoked)]
    public void RevokedDeviceOrSession_InvalidatesRefreshContext(
        bool deviceRevoked,
        string sessionStatus)
    {
        var now = DateTime.UtcNow;
        var token = ValidRefreshToken(now);
        token.Session.Device!.IsRevoked = deviceRevoked;
        token.Session.Status = sessionStatus;

        Assert.False(AuthService.HasValidRefreshContext(token, now));
    }

    private static RefreshToken ValidRefreshToken(DateTime now)
    {
        var device = new RegisteredDevice
        {
            DeviceId = Guid.NewGuid(),
            UserId = "user-1",
            DeviceName = "Test device",
            DeviceFingerprintHash = new byte[32],
            FirstSeenAtUtc = now.AddDays(-1),
            LastSeenAtUtc = now,
            IsRevoked = false
        };
        var session = new UserSession
        {
            SessionId = Guid.NewGuid(),
            UserId = "user-1",
            DeviceId = device.DeviceId,
            Device = device,
            Status = SessionStatuses.Active,
            StartedAtUtc = now.AddHours(-1),
            LastSeenAtUtc = now,
            AbsoluteExpiresAtUtc = now.AddDays(1)
        };
        return new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            UserId = "user-1",
            SessionId = session.SessionId,
            Session = session,
            TokenHash = new byte[32],
            CreatedAtUtc = now.AddMinutes(-5),
            ExpiresAtUtc = now.AddDays(1)
        };
    }
}
