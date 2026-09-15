using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.TikTok;
using TOOL_SERVER.TikTok.Data;
using TOOL_SERVER.TikTok.Domain;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed class TikTokAdminCredentialTests
{
    private const string ClientKey = "test-client-key-123456";
    private const string ClientSecret = "test-client-secret-very-private";

    [Fact]
    public void CredentialProtector_EncryptsAndBindsPayloadToCredentialId()
    {
        var protector = new TikTokAppCredentialProtector(new EphemeralDataProtectionProvider());
        var credentialId = Guid.NewGuid();

        var protectedPayload = protector.Protect(credentialId, ClientKey, ClientSecret);
        var material = protector.Unprotect(credentialId, protectedPayload, true);

        Assert.DoesNotContain(ClientKey, protectedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, protectedPayload, StringComparison.Ordinal);
        Assert.Equal(ClientKey, material.ClientKey);
        Assert.Equal(ClientSecret, material.ClientSecret);
        Assert.True(material.PendingVerification);
        Assert.Throws<CryptographicException>(() =>
            protector.Unprotect(Guid.NewGuid(), protectedPayload, true));
    }

    [Fact]
    public async Task SaveCredential_ReturnsOnlyHintsAndPersistsEncryptedPendingPayload()
    {
        await using var fixture = CreateFixture();

        var state = await fixture.Admin.SaveCredentialAsync(
            new SaveTikTokAdminCredentialRequest(ClientKey, ClientSecret),
            Context("admin-a"),
            CancellationToken.None);

        var summary = Assert.Single(state.Credentials);
        var stored = await fixture.Db.AppCredentials.SingleAsync();
        var serializedResponse = JsonSerializer.Serialize(state);
        Assert.Equal(TikTokAppCredentialStatuses.Pending, summary.Status);
        Assert.DoesNotContain(ClientKey, serializedResponse, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, serializedResponse, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientKey, stored.ProtectedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, stored.ProtectedPayload, StringComparison.Ordinal);
        Assert.Contains(ClientKey[^4..], summary.ClientKeyHint, StringComparison.Ordinal);
        Assert.Contains(ClientSecret[^4..], summary.SecretHint, StringComparison.Ordinal);
        Assert.Contains(
            await fixture.Db.AccountAuditLogs.Select(x => x.EventType).ToListAsync(),
            x => x == "TikTokAppCredentialSaved");
    }

    [Fact]
    public async Task Verification_ReadFromSqlStyleDates_PreservesUtcDeadlineAndAdminAccess()
    {
        var clock = new VerificationClock();
        await using var fixture = CreateFixture(clock);
        var saved = await fixture.Admin.SaveCredentialAsync(
            new SaveTikTokAdminCredentialRequest(ClientKey, ClientSecret), Context("admin-a"), CancellationToken.None);
        var credentialId = Assert.Single(saved.Credentials).CredentialId;
        await fixture.Admin.RequestVerificationAsync(credentialId, Context("admin-a"), CancellationToken.None);

        // SQL datetime2 preserves ticks but does not preserve DateTime.Kind.
        var stored = await fixture.Db.AppCredentials.SingleAsync();
        stored.CreatedAtUtc = DateTime.SpecifyKind(stored.CreatedAtUtc, DateTimeKind.Unspecified);
        stored.UpdatedAtUtc = DateTime.SpecifyKind(stored.UpdatedAtUtc, DateTimeKind.Unspecified);
        stored.VerificationExpiresAtUtc = DateTime.SpecifyKind(stored.VerificationExpiresAtUtc!.Value, DateTimeKind.Unspecified);
        fixture.Db.AppCredentials.Update(stored);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var state = await fixture.Admin.GetStateAsync("admin-a", CancellationToken.None);
        var summary = Assert.Single(state.Credentials);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var credential = json.RootElement.GetProperty("credentials")[0];
        Assert.Equal("2026-09-09T05:15:00Z", credential.GetProperty("verificationExpiresAtUtc").GetString());
        Assert.EndsWith("Z", credential.GetProperty("createdAtUtc").GetString());
        Assert.EndsWith("Z", credential.GetProperty("updatedAtUtc").GetString());
        Assert.True(summary.VerificationRequestedByCurrentAdmin);
        Assert.False(state.IntegrationEnabled);
        var verifier = await fixture.Runtime.GetUserAccessAsync("admin-a", CancellationToken.None);
        Assert.True(verifier.Enabled);
        Assert.True(verifier.Configured);
        Assert.True(verifier.IsCredentialVerification);
        Assert.False((await fixture.Runtime.GetUserAccessAsync("admin-b", CancellationToken.None)).Enabled);

        clock.Now = clock.Now.AddMinutes(15);
        Assert.False((await fixture.Runtime.GetUserAccessAsync("admin-a", CancellationToken.None)).Enabled);
        Assert.False(Assert.Single((await fixture.Admin.GetStateAsync("admin-a", CancellationToken.None)).Credentials)
            .VerificationRequestedByCurrentAdmin);
    }

    [Fact]
    public async Task RealOAuthVerification_ActivatesCredentialOnlyForRequestingAdmin()
    {
        await using var fixture = CreateFixture();
        var saved = await fixture.Admin.SaveCredentialAsync(
            new SaveTikTokAdminCredentialRequest(ClientKey, ClientSecret),
            Context("admin-a"),
            CancellationToken.None);
        var credentialId = Assert.Single(saved.Credentials).CredentialId;

        await fixture.Admin.RequestVerificationAsync(
            credentialId,
            Context("admin-a"),
            CancellationToken.None);

        var otherUserAccess = await fixture.Runtime.GetUserAccessAsync("admin-b", CancellationToken.None);
        var verifierAccess = await fixture.Runtime.GetUserAccessAsync("admin-a", CancellationToken.None);
        Assert.False(otherUserAccess.Enabled);
        Assert.Equal(TikTokUnavailableReasons.VerificationOtherAccount, otherUserAccess.UnavailableReason);
        Assert.True(verifierAccess.Enabled);
        Assert.Null(verifierAccess.UnavailableReason);
        Assert.True(verifierAccess.IsCredentialVerification);
        Assert.Equal(ClientKey, verifierAccess.Credential?.ClientKey);

        await fixture.Runtime.ActivateVerifiedCredentialAsync(
            credentialId,
            "admin-a",
            CancellationToken.None);

        var state = await fixture.Admin.GetStateAsync("admin-a", CancellationToken.None);
        var active = Assert.Single(state.Credentials);
        Assert.Equal(TikTokAppCredentialStatuses.Active, active.Status);
        Assert.True(state.IntegrationEnabled);
        Assert.False(state.AuditedForPublicPosting);
        Assert.Null(state.AuditEvidence);
    }

    [Fact]
    public async Task Readiness_ExplainsMissingSetupExpiredVerificationAndDisabledIntegration()
    {
        var clock = new VerificationClock();
        await using var fixture = CreateFixture(clock);
        Assert.Equal(TikTokUnavailableReasons.SetupRequired,
            (await fixture.Runtime.GetUserAccessAsync("admin-a", CancellationToken.None)).UnavailableReason);
        var saved = await fixture.Admin.SaveCredentialAsync(
            new SaveTikTokAdminCredentialRequest(ClientKey, ClientSecret), Context("admin-a"), CancellationToken.None);
        var id = Assert.Single(saved.Credentials).CredentialId;
        await fixture.Admin.RequestVerificationAsync(id, Context("admin-a"), CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(15);
        Assert.Equal(TikTokUnavailableReasons.VerificationExpired,
            (await fixture.Runtime.GetUserAccessAsync("admin-a", CancellationToken.None)).UnavailableReason);
        Assert.Equal(TikTokUnavailableReasons.SetupRequired,
            (await fixture.Runtime.GetUserAccessAsync("user-b", CancellationToken.None)).UnavailableReason);
        await fixture.Admin.RequestVerificationAsync(id, Context("admin-a"), CancellationToken.None);
        await fixture.Runtime.ActivateVerifiedCredentialAsync(id, "admin-a", CancellationToken.None);
        await fixture.Admin.UpdateSettingsAsync(new UpdateTikTokAdminSettingsRequest(false, false, false, null),
            Context("admin-a"), CancellationToken.None);
        Assert.Equal(TikTokUnavailableReasons.IntegrationDisabled,
            (await fixture.Runtime.GetUserAccessAsync("admin-a", CancellationToken.None)).UnavailableReason);
    }

    [Fact]
    public async Task Rotation_IsBlockedWhileAnyTikTokConnectionExists()
    {
        await using var fixture = CreateFixture();
        var saved = await fixture.Admin.SaveCredentialAsync(
            new SaveTikTokAdminCredentialRequest(ClientKey, ClientSecret),
            Context("admin-a"),
            CancellationToken.None);
        var credentialId = Assert.Single(saved.Credentials).CredentialId;
        await fixture.Admin.RequestVerificationAsync(credentialId, Context("admin-a"), CancellationToken.None);
        fixture.Db.Connections.Add(new TikTokConnection
        {
            TikTokConnectionId = Guid.NewGuid(),
            UserId = "user-a",
            OpenId = "open-a",
            Scopes = "video.publish",
            ProtectedAccessToken = "protected-access",
            ProtectedRefreshToken = "protected-refresh",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Runtime.ActivateVerifiedCredentialAsync(
                credentialId,
                "admin-a",
                CancellationToken.None));

        Assert.Equal("tiktok_rotation_in_use", exception.Code);
        Assert.Equal(TikTokAppCredentialStatuses.Pending,
            (await fixture.Db.AppCredentials.SingleAsync()).Status);
    }

    [Fact]
    public async Task PublicPosting_RequiresExplicitAuditConfirmationAndEvidence()
    {
        await using var fixture = CreateFixture();
        var saved = await fixture.Admin.SaveCredentialAsync(
            new SaveTikTokAdminCredentialRequest(ClientKey, ClientSecret),
            Context("admin-a"),
            CancellationToken.None);
        var credentialId = Assert.Single(saved.Credentials).CredentialId;
        await fixture.Admin.RequestVerificationAsync(credentialId, Context("admin-a"), CancellationToken.None);
        await fixture.Runtime.ActivateVerifiedCredentialAsync(credentialId, "admin-a", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Admin.UpdateSettingsAsync(
                new UpdateTikTokAdminSettingsRequest(true, true, false, "review-123456"),
                Context("admin-a"),
                CancellationToken.None));
        Assert.Equal("tiktok_audit_confirmation_required", exception.Code);

        var state = await fixture.Admin.UpdateSettingsAsync(
            new UpdateTikTokAdminSettingsRequest(true, true, true, "review-123456"),
            Context("admin-a"),
            CancellationToken.None);
        Assert.True(state.AuditedForPublicPosting);
        Assert.Equal("review-123456", state.AuditEvidence);
    }

    private static Fixture CreateFixture(TimeProvider? clock = null)
    {
        var dbOptions = new DbContextOptionsBuilder<TikTokDbContext>()
            .UseInMemoryDatabase($"tiktok-admin-{Guid.NewGuid():N}")
            .Options;
        var db = new TikTokDbContext(dbOptions);
        var options = Options.Create(new TikTokOptions
        {
            Enabled = false,
            AdminManagedCredentialsEnabled = true,
            Scopes = ["video.publish"]
        });
        var protector = new TikTokAppCredentialProtector(new EphemeralDataProtectionProvider());
        return new Fixture(
            db,
            new TikTokAdminService(db, protector, options, clock ?? TimeProvider.System),
            new TikTokCredentialRuntime(db, protector, options, clock ?? TimeProvider.System));
    }

    private sealed class VerificationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 9, 5, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static TikTokAdminRequestContext Context(string userId) =>
        new(userId, "127.0.0.1", "unit-test", Guid.NewGuid().ToString("N"));

    private sealed class Fixture(
        TikTokDbContext db,
        TikTokAdminService admin,
        TikTokCredentialRuntime runtime) : IAsyncDisposable
    {
        public TikTokDbContext Db { get; } = db;
        public TikTokAdminService Admin { get; } = admin;
        public TikTokCredentialRuntime Runtime { get; } = runtime;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
