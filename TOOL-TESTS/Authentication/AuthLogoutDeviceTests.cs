using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class AuthLogoutDeviceTests
{
    private static readonly DateTime Now = new(2026, 9, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly ClientRequestContext Client = new(null, "logout-test", null);

    [Theory]
    [InlineData(SessionStatuses.Revoked)]
    [InlineData(SessionStatuses.Expired)]
    [InlineData(SessionStatuses.Active)]
    public async Task Activate_RecoversOldSlotWithoutValidSession_AndLeavesOtherUserUntouched(string oldStatus)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        await fixture.AddUserAsync("other");
        var oldDevice = await fixture.LoginAndActivateAsync("owner", "old");
        var otherDevice = await fixture.LoginAndActivateAsync("other", "other");
        // Simulate logout on an older server, or an expired session whose status was not swept.
        await fixture.Db.UserSessions.Where(x => x.SessionId == oldDevice.SessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, oldStatus)
                .SetProperty(x => x.AbsoluteExpiresAtUtc, oldStatus == SessionStatuses.Active
                    ? Now.AddMinutes(-1) : Now.AddDays(1)));
        var newDevice = await fixture.LoginAsync("owner", "new");

        var activated = await fixture.ActivateAsync(newDevice);

        Assert.True(activated.CurrentDeviceActivated);
        Assert.Equal(1, activated.ActiveDeviceCount);
        var oldActivation = await fixture.Db.LicenseActivations.AsNoTracking()
            .SingleAsync(x => x.DeviceId == oldDevice.DeviceId);
        Assert.Equal("Revoked", oldActivation.Status);
        Assert.Equal(Now, oldActivation.RevokedAtUtc);
        await AssertStillActiveAsync(fixture, otherDevice);
        Assert.Single(await fixture.Db.AccountAuditLogs.Where(x => x.EventType == "DeviceActivationsReleased")
            .ToListAsync());
        Assert.False((await fixture.Db.RegisteredDevices.AsNoTracking()
            .SingleAsync(x => x.DeviceId == oldDevice.DeviceId)).IsRevoked);
    }

    [Fact]
    public async Task Activate_DoesNotReclaimQuietDeviceWithUnexpiredActiveSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        var oldDevice = await fixture.LoginAndActivateAsync("owner", "old");
        await fixture.Db.UserSessions.Where(x => x.SessionId == oldDevice.SessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastSeenAtUtc, Now.AddDays(-1)));
        var newDevice = await fixture.LoginAsync("owner", "new");

        var denied = await Assert.ThrowsAsync<AccountApiException>(() => fixture.ActivateAsync(newDevice));

        Assert.Equal("device_limit_reached", denied.Code);
        await AssertStillActiveAsync(fixture, oldDevice);
        Assert.False(await fixture.Db.AccountAuditLogs.AnyAsync(x => x.EventType == "DeviceActivationsReleased"));
    }

    [Fact]
    public async Task Activate_WhenRecoveryAuditFails_RollsBackOldAndNewActivations()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        var oldDevice = await fixture.LoginAndActivateAsync("owner", "old");
        await fixture.Db.UserSessions.Where(x => x.SessionId == oldDevice.SessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, SessionStatuses.Revoked));
        var newDevice = await fixture.LoginAsync("owner", "new");
        await fixture.Db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER FailActivationRecoveryAudit BEFORE INSERT ON AccountAuditLogs
            WHEN NEW.EventType = 'DeviceActivationsReleased'
            BEGIN SELECT RAISE(ABORT, 'forced activation recovery audit failure'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.ActivateAsync(newDevice));
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal("Active", (await fixture.Db.LicenseActivations.AsNoTracking()
            .SingleAsync(x => x.DeviceId == oldDevice.DeviceId)).Status);
        Assert.False(await fixture.Db.LicenseActivations.AnyAsync(x => x.DeviceId == newDevice.DeviceId));
    }

    [Fact]
    public async Task Activate_ExpiredCallerSession_CannotReclaimSlots()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        var oldDevice = await fixture.LoginAndActivateAsync("owner", "old");
        await fixture.Db.UserSessions.Where(x => x.SessionId == oldDevice.SessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, SessionStatuses.Revoked));
        var newDevice = await fixture.LoginAsync("owner", "new");
        await fixture.Db.UserSessions.Where(x => x.SessionId == newDevice.SessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AbsoluteExpiresAtUtc, Now.AddMinutes(-1)));

        var denied = await Assert.ThrowsAsync<AccountApiException>(() => fixture.ActivateAsync(newDevice));

        Assert.Equal("session_unavailable", denied.Code);
        Assert.Equal("Active", (await fixture.Db.LicenseActivations.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_ReleasesSingleDeviceSlot_AndAllowsSwitchingBack(bool includeRefreshToken)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        var oldDevice = await fixture.LoginAsync("owner", "old-device");
        await fixture.ActivateAsync(oldDevice);
        var newDevice = await fixture.LoginAsync("owner", "new-device");
        var denied = await Assert.ThrowsAsync<AccountApiException>(() => fixture.ActivateAsync(newDevice));
        Assert.Equal("device_limit_reached", denied.Code);

        await fixture.LogoutAsync(oldDevice, includeRefreshToken: includeRefreshToken);

        var released = await fixture.LicenseAsync(oldDevice);
        Assert.Equal(0, released.ActiveDeviceCount);
        Assert.False(released.CurrentDeviceActivated);
        Assert.False((await fixture.Db.RegisteredDevices.AsNoTracking()
            .SingleAsync(x => x.DeviceId == oldDevice.DeviceId)).IsRevoked);
        await AssertSessionRevokedAsync(fixture, oldDevice);
        var invalidRefresh = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Auth.RefreshAsync(
            new RefreshTokenRequest(oldDevice.RefreshToken), Client, default));
        Assert.Equal("invalid_refresh_token", invalidRefresh.Code);
        fixture.Db.ChangeTracker.Clear();

        Assert.True((await fixture.ActivateAsync(newDevice)).CurrentDeviceActivated);
        var returningDevice = await fixture.LoginAsync("owner", "old-device");
        Assert.Equal(oldDevice.DeviceId, returningDevice.DeviceId);
        denied = await Assert.ThrowsAsync<AccountApiException>(() => fixture.ActivateAsync(returningDevice));
        Assert.Equal("device_limit_reached", denied.Code);
        await fixture.LogoutAsync(newDevice);

        var switchedBack = await fixture.ActivateAsync(returningDevice);
        Assert.True(switchedBack.CurrentDeviceActivated);
        Assert.Equal(1, switchedBack.ActiveDeviceCount);
    }

    [Fact]
    public async Task Logout_LeavesOtherDevicesAndOtherUsersUntouched()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner", maxDevices: 2);
        await fixture.AddUserAsync("other");
        var current = await fixture.LoginAndActivateAsync("owner", "current");
        var second = await fixture.LoginAndActivateAsync("owner", "second");
        var other = await fixture.LoginAndActivateAsync("other", "other");

        await fixture.LogoutAsync(current);

        await AssertSessionRevokedAsync(fixture, current);
        await AssertStillActiveAsync(fixture, second);
        await AssertStillActiveAsync(fixture, other);
        Assert.Equal(1, (await fixture.LicenseAsync(second)).ActiveDeviceCount);
    }

    [Fact]
    public async Task LogoutAll_ReleasesAllOwnedSlotsIncludingPreviouslyLoggedOutDevices()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner", maxDevices: 2);
        await fixture.AddUserAsync("other");
        var oldDevice = await fixture.LoginAndActivateAsync("owner", "old");
        var current = await fixture.LoginAndActivateAsync("owner", "current");
        var other = await fixture.LoginAndActivateAsync("other", "other");
        // Reproduce a slot left behind by the server's previous logout behavior.
        await fixture.Db.UserSessions.Where(x => x.SessionId == oldDevice.SessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, SessionStatuses.Revoked));

        await fixture.LogoutAsync(current, allDevices: true);
        await fixture.LogoutAsync(current, allDevices: true);

        Assert.Equal(0, (await fixture.LicenseAsync(current)).ActiveDeviceCount);
        await AssertSessionRevokedAsync(fixture, current);
        await AssertSessionRevokedAsync(fixture, oldDevice);
        await AssertStillActiveAsync(fixture, other);
        Assert.All(await fixture.Db.RegisteredDevices.AsNoTracking()
            .Where(x => x.UserId == "owner").ToListAsync(), device => Assert.False(device.IsRevoked));
        Assert.Equal(2, await fixture.Db.AccountAuditLogs.CountAsync(x => x.EventType == "LogoutAll"));
    }

    [Fact]
    public async Task Logout_WithOldRefreshToken_DoesNotReleaseNewSessionOnSameDevice()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        var oldSession = await fixture.LoginAndActivateAsync("owner", "same-device");
        var newSession = await fixture.LoginAndActivateAsync("owner", "same-device");

        await fixture.Auth.LogoutAsync("owner", newSession.SessionId,
            new LogoutRequest(oldSession.RefreshToken), Client, default);
        fixture.Db.ChangeTracker.Clear();

        await AssertSessionRevokedAsync(fixture, oldSession);
        await AssertStillActiveAsync(fixture, newSession);
        Assert.True((await fixture.LicenseAsync(newSession)).CurrentDeviceActivated);
    }

    [Fact]
    public async Task Logout_ExpiredSessionOnSameDevice_DoesNotKeepSlotOccupied()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        var current = await fixture.LoginAndActivateAsync("owner", "current");
        fixture.Db.UserSessions.Add(new UserSession
        {
            SessionId = Guid.NewGuid(), UserId = "owner", DeviceId = current.DeviceId,
            Status = SessionStatuses.Active, StartedAtUtc = Now.AddDays(-2),
            LastSeenAtUtc = Now.AddDays(-1), AbsoluteExpiresAtUtc = Now.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.LogoutAsync(current);

        Assert.Equal(0, (await fixture.LicenseAsync(current)).ActiveDeviceCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_ForeignSessionOrRefreshToken_DoesNotAffectOtherUser(bool foreignSessionId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        await fixture.AddUserAsync("other");
        var current = await fixture.LoginAndActivateAsync("owner", "current");
        var other = await fixture.LoginAndActivateAsync("other", "other");

        await fixture.Auth.LogoutAsync("owner",
            foreignSessionId ? other.SessionId : current.SessionId,
            new LogoutRequest(foreignSessionId ? null : other.RefreshToken), Client, default);
        fixture.Db.ChangeTracker.Clear();

        await AssertStillActiveAsync(fixture, other);
        if (foreignSessionId)
            await AssertStillActiveAsync(fixture, current);
        else
            await AssertSessionRevokedAsync(fixture, current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_WhenAuditFails_RollsBackSessionTokenAndActivation(bool allDevices)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner");
        var current = await fixture.LoginAndActivateAsync("owner", "current");
        await fixture.Db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER FailLogoutAudit BEFORE INSERT ON AccountAuditLogs
            WHEN NEW.EventType IN ('Logout', 'LogoutAll')
            BEGIN SELECT RAISE(ABORT, 'forced logout audit failure'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.LogoutAsync(current, allDevices));
        fixture.Db.ChangeTracker.Clear();

        await AssertStillActiveAsync(fixture, current);
        Assert.False(await fixture.Db.AccountAuditLogs.AnyAsync(x =>
            x.EventType == "Logout" || x.EventType == "LogoutAll"));
    }

    [Fact]
    public async Task LogoutAll_DoesNotUnbanAnAdministrativelyRevokedDevice()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddUserAsync("owner", maxDevices: 2);
        var banned = await fixture.LoginAndActivateAsync("owner", "banned");
        var current = await fixture.LoginAndActivateAsync("owner", "current");
        await fixture.Db.RegisteredDevices.Where(x => x.DeviceId == banned.DeviceId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRevoked, true));

        await fixture.LogoutAsync(current, allDevices: true);

        var denied = await Assert.ThrowsAsync<AccountApiException>(() => fixture.LoginAsync("owner", "banned"));
        Assert.Equal("device_revoked", denied.Code);
    }

    private static async Task AssertSessionRevokedAsync(Fixture fixture, AuthTokenResponse login)
    {
        Assert.Equal(SessionStatuses.Revoked, (await fixture.Db.UserSessions.AsNoTracking()
            .SingleAsync(x => x.SessionId == login.SessionId)).Status);
        var tokens = await fixture.Db.RefreshTokens.AsNoTracking()
            .Where(x => x.SessionId == login.SessionId).ToListAsync();
        Assert.NotEmpty(tokens);
        Assert.All(tokens, token => Assert.NotNull(token.RevokedAtUtc));
    }

    private static async Task AssertStillActiveAsync(Fixture fixture, AuthTokenResponse login)
    {
        Assert.Equal(SessionStatuses.Active, (await fixture.Db.UserSessions.AsNoTracking()
            .SingleAsync(x => x.SessionId == login.SessionId)).Status);
        Assert.Null((await fixture.Db.RefreshTokens.AsNoTracking()
            .SingleAsync(x => x.SessionId == login.SessionId)).RevokedAtUtc);
        Assert.True((await fixture.LicenseAsync(login)).CurrentDeviceActivated);
    }

    private sealed class Fixture(SqliteConnection connection, ServiceProvider services, AsyncServiceScope scope)
        : IAsyncDisposable
    {
        private const string Password = "Logout-test-password-123!";
        public AccountDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        public AuthService Auth { get; } = scope.ServiceProvider.GetRequiredService<AuthService>();
        private AccountManagementService Accounts { get; } = scope.ServiceProvider.GetRequiredService<AccountManagementService>();

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddDbContext<AccountDbContext>(options => options.UseSqlite(connection));
            collection.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<AccountDbContext>();
            collection.AddSingleton<TimeProvider>(new FixedTimeProvider());
            collection.AddSingleton<ITokenFactory, TestTokenFactory>();
            collection.AddScoped<AuthService>();
            collection.AddScoped<AccountManagementService>();
            var services = collection.BuildServiceProvider();
            var fixture = new Fixture(connection, services, services.CreateAsyncScope());
            // Use the production relational model; adapt SQL Server defaults/types for SQLite.
            await fixture.Db.Database.ExecuteSqlRawAsync(fixture.Db.Database.GenerateCreateScript()
                .Replace("DEFAULT (NEWSEQUENTIALID())", "")
                .Replace("DEFAULT (SYSUTCDATETIME())", "DEFAULT CURRENT_TIMESTAMP")
                .Replace("nvarchar(max)", "TEXT")
                .Replace("\"RowVersion\" BLOB NOT NULL", "\"RowVersion\" BLOB NOT NULL DEFAULT X''"));
            return fixture;
        }

        public async Task AddUserAsync(string userId, int maxDevices = 1)
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await manager.CreateAsync(new ApplicationUser
            {
                Id = userId, UserName = userId + "@example.invalid", Email = userId + "@example.invalid",
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            }, Password);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
            var plan = new LicensePlan
            {
                LicensePlanId = Guid.NewGuid(), PlanCode = userId, Name = "Test plan",
                MaxActivatedDevices = maxDevices, IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now
            };
            Db.UserLicenses.Add(new UserLicense
            {
                UserLicenseId = Guid.NewGuid(), UserId = userId, LicensePlan = plan,
                Status = "Active", StartsAtUtc = Now.AddDays(-1), ExpiresAtUtc = Now.AddDays(30),
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<AuthTokenResponse> LoginAsync(string userId, string deviceName)
        {
            Db.ChangeTracker.Clear();
            var login = await Auth.LoginAsync(new LoginRequest(userId + "@example.invalid", Password,
                new DeviceRegistrationRequest(deviceName, deviceName, "Test OS", "1.0.0")), Client, default);
            Assert.Null(login.Failure);
            return Assert.IsType<AuthTokenResponse>(login.Response);
        }

        public async Task<AuthTokenResponse> LoginAndActivateAsync(string userId, string deviceName)
        {
            var login = await LoginAsync(userId, deviceName);
            await ActivateAsync(login);
            return login;
        }

        public Task<CurrentLicenseResponse> ActivateAsync(AuthTokenResponse login)
        {
            Db.ChangeTracker.Clear();
            return Accounts.ActivateCurrentDeviceAsync(login.User.UserId, login.DeviceId, login.SessionId, default);
        }

        public Task<CurrentLicenseResponse> LicenseAsync(AuthTokenResponse login)
        {
            Db.ChangeTracker.Clear();
            return Accounts.GetCurrentLicenseAsync(login.User.UserId, login.DeviceId, default);
        }

        public async Task LogoutAsync(AuthTokenResponse login, bool allDevices = false, bool includeRefreshToken = true)
        {
            Db.ChangeTracker.Clear();
            await Auth.LogoutAsync(login.User.UserId, login.SessionId,
                new LogoutRequest(includeRefreshToken ? login.RefreshToken : null, allDevices), Client, default);
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class TestTokenFactory : ITokenFactory
    {
        public AccessTokenMaterial CreateAccessToken(ApplicationUser user, IEnumerable<string> roles,
            Guid sessionId, Guid deviceId) => new("test-access", Guid.NewGuid().ToString("N"), Now.AddMinutes(15));

        public RefreshTokenMaterial CreateRefreshToken()
        {
            var token = Guid.NewGuid().ToString("N");
            return new RefreshTokenMaterial(token, HashRefreshToken(token), token[..8], Now.AddDays(7));
        }

        public byte[] HashRefreshToken(string refreshToken) => SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
    }
}
