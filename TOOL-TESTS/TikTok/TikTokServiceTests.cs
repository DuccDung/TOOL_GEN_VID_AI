using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.TikTok;
using TOOL_SERVER.TikTok.Data;
using TOOL_SERVER.TikTok.Domain;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed partial class TikTokServiceTests
{
    [Theory]
    [InlineData(false, TikTokUnavailableReasons.SetupRequired)]
    [InlineData(true, TikTokUnavailableReasons.EmergencyDisabled)]
    public async Task FeatureState_ReportsUnavailableReasonWithoutCallingTikTok(bool emergencyDisabled, string reason)
    {
        await using var db = CreateDb();
        var options = Options.Create(new TikTokOptions { EmergencyDisabled = emergencyDisabled });
        var runtime = new TikTokCredentialRuntime(db,
            new TikTokAppCredentialProtector(new EphemeralDataProtectionProvider()), options, TimeProvider.System);
        // A readiness query must not need an outbound API client.
        var service = new TikTokService(db, null!, new IdentityProtector(), runtime, options, TimeProvider.System);
        var state = await service.GetStateAsync("user-a", CancellationToken.None);
        Assert.False(state.Enabled);
        Assert.Equal(reason, state.UnavailableReason);
        Assert.Null(state.Connection);
        Assert.False(state.IsCredentialVerification);
        var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<TikTokFeatureStateResponse>(
            System.Text.Json.JsonSerializer.Serialize(state, jsonOptions), jsonOptions);
        Assert.Equal(reason, roundTrip!.UnavailableReason);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<TikTokFeatureStateResponse>(
            """{"enabled":false,"configured":false,"connection":null}""", jsonOptions);
        Assert.Null(legacy!.UnavailableReason);
        Assert.False(legacy.IsCredentialVerification);
    }

    [Theory]
    [InlineData(4 * 1024 * 1024, 4 * 1024 * 1024, 1)]
    [InlineData(64 * 1024 * 1024, 64 * 1024 * 1024, 1)]
    [InlineData(65 * 1024 * 1024, 32 * 1024 * 1024, 2)]
    [InlineData(4L * 1024 * 1024 * 1024, 32 * 1024 * 1024, 128)]
    public void ChunkPlan_UsesTikTokSequentialUploadRules(long size, long expectedChunk, int expectedCount)
    {
        var plan = TikTokService.CreateChunkPlan(size);

        Assert.Equal(expectedChunk, plan.ChunkSizeBytes);
        Assert.Equal(expectedCount, plan.TotalChunkCount);
        var finalChunk = size - plan.ChunkSizeBytes * (plan.TotalChunkCount - 1L);
        Assert.InRange(finalChunk, 1, 128L * 1024 * 1024);
    }

    [Fact]
    public void TokenProtector_EncryptsAndBindsCiphertextToUser()
    {
        var protector = new TikTokTokenProtector(new EphemeralDataProtectionProvider());
        const string token = "act.secret-user-token";

        var protectedToken = protector.ProtectToken("user-a", token);

        Assert.NotEqual(token, protectedToken);
        Assert.DoesNotContain(token, protectedToken, StringComparison.Ordinal);
        Assert.Equal(token, protector.UnprotectToken("user-a", protectedToken));
        Assert.Throws<CryptographicException>(() => protector.UnprotectToken("user-b", protectedToken));
    }

    [Fact]
    public async Task StartOAuth_StoresOnlyStateHashAndRequiresLoopbackRedirect()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var verifier = new string('a', 64);
        var challenge = TikTokService.CreateCodeChallenge(verifier);

        var started = await service.StartOAuthAsync(
            "user-a",
            Guid.NewGuid(),
            new StartTikTokOAuthRequest("http://127.0.0.1:49152/callback/", challenge),
            CancellationToken.None);

        var stored = await db.OAuthSessions.SingleAsync();
        Assert.Equal(32, stored.StateHash.Length);
        Assert.DoesNotContain(started.State, Convert.ToHexString(stored.StateHash), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://www.tiktok.com/v2/auth/authorize/", started.AuthorizationUrl, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(challenge), started.AuthorizationUrl, StringComparison.Ordinal);
        await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(() =>
            service.StartOAuthAsync(
                "user-a",
                Guid.NewGuid(),
                new StartTikTokOAuthRequest("https://example.com/callback", challenge),
                CancellationToken.None));
    }

    [Theory]
    [InlineData("open-upload.tiktokapis.com")]
    [InlineData("open-upload-sg.tiktokapis.com")]
    [InlineData("upload.us.tiktokapis.com")]
    public async Task StateAndPublishJobs_AreIsolatedByUser(string uploadHost)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var connection = new TikTokConnection
        {
            TikTokConnectionId = Guid.NewGuid(),
            UserId = "user-a",
            OpenId = "open-a",
            CreatorUsername = "creator-a",
            CreatorNickname = "Creator A",
            Scopes = "video.publish",
            ProtectedAccessToken = "access-a",
            ProtectedRefreshToken = "refresh-a",
            AccessTokenExpiresAtUtc = now.AddHours(1),
            RefreshTokenExpiresAtUtc = now.AddDays(100),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync();
        var uploadUrl = $"https://{uploadHost}/video/?upload_id=test&upload_token=fake%2Bvalue%3D";
        var api = new FakeTikTokApiClient { UploadUrl = uploadUrl };
        var service = CreateService(db, api);

        var ownerState = await service.GetStateAsync("user-a", CancellationToken.None);
        var otherState = await service.GetStateAsync("user-b", CancellationToken.None);
        var initialized = await service.InitializePublishAsync(
            "user-a",
            new InitializeTikTokPublishRequest(
                Guid.NewGuid(), "Caption", "SELF_ONLY", true, true, true,
                false, false, false, 70L * 1024 * 1024, 30, "video/mp4"),
            CancellationToken.None);

        Assert.NotNull(ownerState.Connection);
        Assert.Null(otherState.Connection);
        Assert.Equal(32L * 1024 * 1024, initialized.ChunkSizeBytes);
        Assert.Equal(2, initialized.TotalChunkCount);
        Assert.Equal("Caption", api.LastPublishPayload?.Title);
        Assert.Equal(uploadUrl, initialized.UploadUrl);
        Assert.Equal(uploadUrl, (await db.PublishJobs.SingleAsync()).ProtectedUploadUrl);
        var activeState = await service.GetStateAsync("user-a", CancellationToken.None);
        Assert.Equal(initialized.PublishJobId, activeState.ActivePublish?.PublishJobId);
        var exception = await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(() =>
            service.GetPublishStatusAsync("user-b", initialized.PublishJobId, CancellationToken.None));
        Assert.Equal("tiktok_publish_not_found", exception.Code);
    }

    [Theory]
    [MemberData(nameof(TikTokUploadUrlCases.RejectedUrls), MemberType = typeof(TikTokUploadUrlCases))]
    public async Task Publish_RejectsUnsafeUploadUrlWithoutSavingJob(string uploadUrl)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        db.Connections.Add(new TikTokConnection
        {
            TikTokConnectionId = Guid.NewGuid(), UserId = "user-a", OpenId = "open-a",
            Scopes = "video.publish", ProtectedAccessToken = "access-a", ProtectedRefreshToken = "refresh-a",
            AccessTokenExpiresAtUtc = now.AddHours(1), RefreshTokenExpiresAtUtc = now.AddDays(100),
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeTikTokApiClient { UploadUrl = uploadUrl });

        var exception = await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(() =>
            service.InitializePublishAsync("user-a",
                new InitializeTikTokPublishRequest(Guid.NewGuid(), "Caption", "SELF_ONLY", true, true, true,
                    false, false, false, 4 * 1024 * 1024, 30, "video/mp4"), CancellationToken.None));

        Assert.Equal("tiktok_upload_url_invalid", exception.Code);
        Assert.DoesNotContain(uploadUrl, exception.Message, StringComparison.Ordinal);
        Assert.Empty(await db.PublishJobs.ToListAsync());
    }

    [Fact]
    public async Task BrandedContent_CannotUsePrivateVisibility()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        db.Connections.Add(new TikTokConnection
        {
            TikTokConnectionId = Guid.NewGuid(),
            UserId = "user-a",
            OpenId = "open-a",
            Scopes = "video.publish",
            ProtectedAccessToken = "access-a",
            ProtectedRefreshToken = "refresh-a",
            AccessTokenExpiresAtUtc = now.AddHours(1),
            RefreshTokenExpiresAtUtc = now.AddDays(100),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var exception = await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(() =>
            service.InitializePublishAsync(
                "user-a",
                new InitializeTikTokPublishRequest(
                    Guid.NewGuid(), "Caption", "SELF_ONLY", true, true, true,
                    true, false, false, 4 * 1024 * 1024, 30, "video/mp4"),
                CancellationToken.None));

        Assert.Equal("tiktok_branded_content_privacy_invalid", exception.Code);
    }

    [Fact]
    public async Task CompleteOAuth_IsBoundToStartingUserDeviceAndPkceVerifier()
    {
        await using var db = CreateDb();
        var api = new FakeTikTokApiClient();
        var service = CreateService(db, api);
        var deviceA = Guid.NewGuid();
        var verifier = new string('z', 64);
        var started = await service.StartOAuthAsync(
            "user-a",
            deviceA,
            new StartTikTokOAuthRequest(
                "http://127.0.0.1:49153/callback/",
                TikTokService.CreateCodeChallenge(verifier)),
            CancellationToken.None);

        await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(() =>
            service.CompleteOAuthAsync(
                "user-a",
                Guid.NewGuid(),
                new CompleteTikTokOAuthRequest(started.OAuthSessionId, "code", started.State, verifier),
                CancellationToken.None));
        Assert.Equal(0, api.ExchangeCalls);

        var completed = await service.CompleteOAuthAsync(
            "user-a",
            deviceA,
            new CompleteTikTokOAuthRequest(started.OAuthSessionId, "code", started.State, verifier),
            CancellationToken.None);

        Assert.NotNull(completed.Connection);
        Assert.Equal(1, api.ExchangeCalls);
        Assert.NotNull((await db.OAuthSessions.SingleAsync()).ConsumedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauditedApp_RequiresPrivateCreatorAccount(bool privateAccount)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        db.Connections.Add(new TikTokConnection
        {
            TikTokConnectionId = Guid.NewGuid(),
            UserId = "user-a",
            OpenId = "open-a",
            Scopes = "video.publish",
            ProtectedAccessToken = "access-a",
            ProtectedRefreshToken = "refresh-a",
            AccessTokenExpiresAtUtc = now.AddHours(1),
            RefreshTokenExpiresAtUtc = now.AddDays(100),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient { PrivateAccount = privateAccount };
        var service = CreateService(db, api, auditedForPublicPosting: false);

        var creator = await service.GetCreatorInfoAsync("user-a", CancellationToken.None);
        if (privateAccount)
        {
            Assert.Equal("SELF_ONLY", Assert.Single(creator.PrivacyLevelOptions));
            Assert.Null(creator.PublishingIssue);
            return;
        }

        Assert.Equal("creator-a", creator.CreatorUsername);
        Assert.Equal("tiktok_private_test_account_required", creator.PublishingIssue?.Code);
        var blocked = await service.InitializePublishAsync("user-a",
            new InitializeTikTokPublishRequest(Guid.NewGuid(), "Caption", "SELF_ONLY", true, true, true,
                false, false, false, 4 * 1024 * 1024, 30, "video/mp4"), CancellationToken.None);
        Assert.Equal(creator.PublishingIssue, blocked.BlockedCreator?.PublishingIssue);
        Assert.Equal(Guid.Empty, blocked.PublishJobId);
        Assert.Empty(blocked.UploadUrl);
        Assert.Null(api.LastPublishPayload);
        Assert.Empty(await db.PublishJobs.ToListAsync());
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<InitializeTikTokPublishResponse>(
            System.Text.Json.JsonSerializer.Serialize(blocked, options), options);
        Assert.Equal(creator.PublishingIssue, roundTrip?.BlockedCreator?.PublishingIssue);
        api.PrivateAccount = true;
        var refreshed = await service.GetCreatorInfoAsync("user-a", CancellationToken.None);
        Assert.Null(refreshed.PublishingIssue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatorRefresh_DoesNotOverwriteConcurrentConnectionChanges(bool expiredAccessToken)
    {
        var options = new DbContextOptionsBuilder<TikTokDbContext>()
            .UseInMemoryDatabase($"tiktok-concurrent-{Guid.NewGuid():N}").Options;
        await using var db = new TikTokDbContext(options);
        await using var otherRequest = new TikTokDbContext(options);
        var now = DateTime.UtcNow;
        db.Connections.Add(new TikTokConnection
        {
            TikTokConnectionId = Guid.NewGuid(), UserId = "user-a", OpenId = "open-a",
            CreatorUsername = "old-name", CreatorNickname = "Old Name", Scopes = "video.publish",
            ProtectedAccessToken = "access-a", ProtectedRefreshToken = "refresh-a",
            AccessTokenExpiresAtUtc = expiredAccessToken ? now.AddMinutes(-1) : now.AddHours(1),
            RefreshTokenExpiresAtUtc = now.AddDays(100), CreatedAtUtc = now, UpdatedAtUtc = now,
            RowVersion = [1]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var api = new FakeTikTokApiClient
        {
            BeforeCreatorResponse = async () =>
            {
                // Another request changes the SQL row version while the provider call is in flight.
                var current = await otherRequest.Connections.SingleAsync();
                Assert.Equal(expiredAccessToken ? "access-b" : "access-a", current.ProtectedAccessToken);
                Assert.Equal(expiredAccessToken ? "refresh-b" : "refresh-a", current.ProtectedRefreshToken);
                current.ProtectedAccessToken = "concurrent-access";
                current.ProtectedRefreshToken = "concurrent-refresh";
                current.UpdatedAtUtc = now.AddSeconds(1);
                current.RowVersion = [2];
                await otherRequest.SaveChangesAsync();
            }
        };

        var creator = await CreateService(db, api).GetCreatorInfoAsync("user-a", CancellationToken.None);

        Assert.Equal("creator-a", creator.CreatorUsername);
        Assert.Equal("Creator A", creator.CreatorNickname);
        Assert.False(db.ChangeTracker.HasChanges());
        var persisted = await db.Connections.AsNoTracking().SingleAsync();
        Assert.Equal("concurrent-access", persisted.ProtectedAccessToken);
        Assert.Equal("concurrent-refresh", persisted.ProtectedRefreshToken);
        Assert.Equal(now.AddSeconds(1), persisted.UpdatedAtUtc);
        Assert.Equal(new byte[] { 2 }, persisted.RowVersion);
        Assert.Equal("old-name", persisted.CreatorUsername);
    }

    [Fact]
    public async Task Disconnect_RevokesTokensAndTerminatesPendingJobs()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var connection = new TikTokConnection
        {
            TikTokConnectionId = Guid.NewGuid(),
            UserId = "user-a",
            OpenId = "open-a",
            Scopes = "video.publish",
            ProtectedAccessToken = "access-a",
            ProtectedRefreshToken = "refresh-a",
            AccessTokenExpiresAtUtc = now.AddHours(1),
            RefreshTokenExpiresAtUtc = now.AddDays(100),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Connections.Add(connection);
        db.PublishJobs.Add(new TikTokPublishJob
        {
            TikTokPublishJobId = Guid.NewGuid(),
            TikTokConnectionId = connection.TikTokConnectionId,
            UserId = "user-a",
            ClientRequestId = Guid.NewGuid(),
            TikTokPublishId = "v_pub_file~pending",
            ProtectedUploadUrl = "protected-url",
            UploadUrlExpiresAtUtc = now.AddMinutes(30),
            VideoSizeBytes = 1024,
            ChunkSizeBytes = 1024,
            TotalChunkCount = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.DisconnectAsync("user-a", CancellationToken.None);

        Assert.NotNull(connection.RevokedAtUtc);
        Assert.Empty(connection.ProtectedAccessToken);
        var job = await db.PublishJobs.SingleAsync();
        Assert.Equal(TikTokPublishStatuses.Failed, job.Status);
        Assert.Equal("connection_revoked", job.FailureReason);
        Assert.Null(job.ProtectedUploadUrl);
    }

    private static TikTokDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TikTokDbContext>()
            .UseInMemoryDatabase($"tiktok-{Guid.NewGuid():N}")
            .Options;
        return new TikTokDbContext(options);
    }

    private static TikTokService CreateService(
        TikTokDbContext db,
        FakeTikTokApiClient? api = null,
        bool auditedForPublicPosting = true, bool multiAccount = false)
    {
        var options = Options.Create(new TikTokOptions
        {
            Enabled = true,
            MultiAccountEnabled = multiAccount,
            ClientKey = "test-client-key",
            ClientSecret = "test-client-secret",
            Scopes = ["video.publish"],
            AuditedForPublicPosting = auditedForPublicPosting
        });
        var credentialProtector = new TikTokAppCredentialProtector(new EphemeralDataProtectionProvider());
        var runtime = new TikTokCredentialRuntime(db, credentialProtector, options, TimeProvider.System);
        return new TikTokService(
            db,
            api ?? new FakeTikTokApiClient(),
            new IdentityProtector(),
            runtime,
            options,
            TimeProvider.System);
    }

    private sealed class IdentityProtector : ITikTokTokenProtector
    {
        public string ProtectToken(string userId, string token) => token;
        public string UnprotectToken(string userId, string protectedToken) => protectedToken;
        public string ProtectUploadUrl(string userId, string uploadUrl) => uploadUrl;
        public string UnprotectUploadUrl(string userId, string protectedUploadUrl) => protectedUploadUrl;
    }

    private sealed class FakeTikTokApiClient : ITikTokApiClient
    {
        public bool PrivateAccount { get; set; }
        public string UploadUrl { get; init; } = "https://open-upload.tiktokapis.com/video/?upload_id=test&upload_token=secret";
        public Func<Task>? BeforeCreatorResponse { get; init; }
        public TikTokPublishInitPayload? LastPublishPayload { get; private set; }
        public int ExchangeCalls { get; private set; }
        public int InitializeCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public string OpenId { get; set; } = "open-a";
        public string? StatusAccessToken { get; private set; }
        public Func<Task>? BeforeInitialize { get; set; }
        public Exception? InitializeError { get; set; }

        public Task<TikTokTokenResult> ExchangeCodeAsync(
            TikTokAppCredentialMaterial credential,
            string code,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken)
        {
            ExchangeCalls++;
            return Task.FromResult(new TikTokTokenResult(OpenId, "video.publish", "access-a", 3600, "refresh-a", 86400));
        }

        public Task<TikTokTokenResult> RefreshTokenAsync(
            TikTokAppCredentialMaterial credential,
            string refreshToken,
            CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return Task.FromResult(new TikTokTokenResult(OpenId, "video.publish", "access-b", 3600, "refresh-b", 86400));
        }

        public Task RevokeAsync(
            TikTokAppCredentialMaterial credential,
            string accessToken,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<TikTokCreatorResult> GetCreatorInfoAsync(string accessToken, CancellationToken cancellationToken)
        {
            if (BeforeCreatorResponse is not null) await BeforeCreatorResponse();
            return new TikTokCreatorResult(
                "creator-a", "Creator A", PrivateAccount ? ["SELF_ONLY", "FOLLOWER_OF_CREATOR"] : ["SELF_ONLY", "PUBLIC_TO_EVERYONE"], false, false, false, 600);
        }

        public async Task<TikTokPublishInitResult> InitializePublishAsync(string accessToken, TikTokPublishInitPayload payload, CancellationToken cancellationToken)
        {
            InitializeCalls++;
            if (BeforeInitialize is not null) await BeforeInitialize();
            if (InitializeError is not null) throw InitializeError;
            LastPublishPayload = payload;
            return new TikTokPublishInitResult("v_pub_file~test", UploadUrl);
        }

        public Task<TikTokStatusResult> GetPublishStatusAsync(string accessToken, string publishId, CancellationToken cancellationToken)
        {
            StatusAccessToken = accessToken;
            return Task.FromResult(new TikTokStatusResult("PUBLISH_COMPLETE", null, 1, ["123"]));
        }
    }
}
