using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.TikTok;
using TOOL_SERVER.TikTok.Data;
using TOOL_SERVER.TikTok.Domain;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed partial class TikTokServiceTests
{
    private static TikTokConnection Account(string openId = "open-a", string userId = "user-a") => new()
    {
        TikTokConnectionId = Guid.NewGuid(), UserId = userId, OpenId = openId, CreatorUsername = openId,
        Scopes = "video.publish", ProtectedAccessToken = "access-" + openId, ProtectedRefreshToken = "refresh-" + openId,
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1), RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private static InitializeTikTokPublishRequest PublishRequest(Guid connectionId) => new(
        Guid.NewGuid(), "Caption", "SELF_ONLY", true, true, true, false, false, false,
        4 * 1024 * 1024, 30, "video/mp4", connectionId);

    private static async Task<TikTokFeatureStateResponse> ConnectAccount(TikTokService service, Guid? target = null)
    {
        var device = Guid.NewGuid();
        var verifier = new string('a', 64);
        var start = await service.StartOAuthAsync("user-a", device,
            new("http://127.0.0.1:49152/callback/", TikTokService.CreateCodeChallenge(verifier), target, true), default);
        return await service.CompleteOAuthAsync("user-a", device,
            new(start.OAuthSessionId, "test-code", start.State, verifier), default);
    }

    [Fact]
    public async Task MultiAccount_AddReconnectAndMismatch_PreserveIdentityAndExistingJobs()
    {
        await using var db = CreateDb();
        var original = Account();
        db.Connections.Add(original);
        await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient { OpenId = "open-b" };
        var service = CreateService(db, api, multiAccount: true);
        var oldJob = await service.InitializePublishAsync("user-a", PublishRequest(original.TikTokConnectionId), default);
        var added = await ConnectAccount(service);
        Assert.Equal(2, added.Connections!.Count);
        Assert.NotEqual(original.TikTokConnectionId, added.ConnectedConnectionId);
        Assert.Null(added.Connection);
        Assert.Equal("open-a", original.OpenId);
        Assert.Equal("access-open-a", original.ProtectedAccessToken);
        var reconnected = await ConnectAccount(service, added.ConnectedConnectionId);
        Assert.Equal(added.ConnectedConnectionId, reconnected.ConnectedConnectionId);
        Assert.Equal(2, await db.Connections.CountAsync());
        var mismatch = await Assert.ThrowsAsync<AccountApiException>(() => ConnectAccount(service, original.TikTokConnectionId));
        Assert.Equal("tiktok_oauth_account_mismatch", mismatch.Code);
        Assert.Equal(original.TikTokConnectionId, (await db.PublishJobs.SingleAsync()).TikTokConnectionId);
        Assert.Equal(oldJob.PublishJobId, (await db.PublishJobs.SingleAsync()).TikTokPublishJobId);
    }

    [Fact]
    public async Task MultiAccount_FlagOff_BlocksNewAccountButAllowsExistingReconnect()
    {
        await using var db = CreateDb();
        var account = Account(); db.Connections.Add(account); await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient { OpenId = "open-b" };
        var service = CreateService(db, api);
        await Assert.ThrowsAsync<AccountApiException>(() => ConnectAccount(service));
        Assert.Single(await db.Connections.ToListAsync());
        api.OpenId = "open-a";
        Assert.Equal(account.TikTokConnectionId, (await ConnectAccount(service, account.TikTokConnectionId)).ConnectedConnectionId);
    }

    [Fact]
    public async Task MultiAccount_OwnershipAndLegacyChecks_RejectBeforeOutbound()
    {
        await using var db = CreateDb();
        var a = Account(); var b = Account("open-b"); var foreign = Account("open-c", "user-b");
        db.Connections.AddRange(a, b, foreign); await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient(); var service = CreateService(db, api, multiAccount: true);
        var state = await service.GetConnectionsStateAsync("user-a", default);
        Assert.Equal(2, state.Connections!.Count);
        Assert.DoesNotContain(state.Connections, c => c.ConnectionId == foreign.TikTokConnectionId);
        var actions = new Func<Task>[] {
            () => service.GetCreatorInfoAsync("user-a", default, foreign.TikTokConnectionId),
            () => service.DisconnectAsync("user-a", default, foreign.TikTokConnectionId),
            () => service.InitializePublishAsync("user-a", PublishRequest(foreign.TikTokConnectionId), default),
            () => service.GetPublishHistoryAsync("user-a", foreign.TikTokConnectionId, 1, 20, default),
            () => ConnectAccount(service, foreign.TikTokConnectionId)
        };
        foreach (var action in actions) Assert.Equal("tiktok_connection_not_found", (await Assert.ThrowsAsync<AccountApiException>(action)).Code);
        Assert.Equal("tiktok_client_update_required", (await Assert.ThrowsAsync<AccountApiException>(() => service.GetStateAsync("user-a", default))).Code);
        Assert.Equal("tiktok_client_update_required", (await Assert.ThrowsAsync<AccountApiException>(() => service.DisconnectAsync("user-a", default))).Code);
        Assert.Equal(0, api.InitializeCalls); Assert.Equal(0, api.ExchangeCalls); Assert.Equal(0, api.RefreshCalls);
    }

    [Fact]
    public async Task MultiAccount_DisconnectA_LeavesBAndItsWorkerJobUsable()
    {
        await using var db = CreateDb();
        var a = Account(); var b = Account("open-b"); db.Connections.AddRange(a, b); await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient(); var service = CreateService(db, api, multiAccount: true);
        var jobA = await service.InitializePublishAsync("user-a", PublishRequest(a.TikTokConnectionId), default);
        var jobB = await service.InitializePublishAsync("user-a", PublishRequest(b.TikTokConnectionId), default);
        await service.DisconnectAsync("user-a", default, a.TikTokConnectionId);
        Assert.Empty(a.ProtectedRefreshToken); Assert.NotNull(a.DisconnectedAtUtc);
        Assert.Null(b.RevokedAtUtc); Assert.Equal("access-open-b", b.ProtectedAccessToken);
        var failed = await service.ReadPublishStatusAsync("user-a", jobA.PublishJobId, default);
        Assert.Equal("connection_revoked", failed.FailureReason);
        var completed = await service.GetPublishStatusAsync("user-a", jobB.PublishJobId, default);
        Assert.Equal("access-open-b", api.StatusAccessToken); Assert.True(completed.IsTerminal);
        var history = await service.GetPublishHistoryAsync("user-a", a.TikTokConnectionId, 1, 20, default);
        Assert.Equal(jobA.PublishJobId, Assert.Single(history.Items).PublishJobId);
        Assert.Equal("creator-a", history.Items[0].CreatorUsername);
        var state = await service.GetConnectionsStateAsync("user-a", default);
        Assert.Equal("Disconnected", state.Connections!.Single(c => c.ConnectionId == a.TikTokConnectionId).Status);
        Assert.Equal(2, state.Connections!.Count);
    }

    [Fact]
    public async Task MultiAccount_Idempotency_BindsPayloadAndAccount_AndNewIntentAllowsSameVideo()
    {
        await using var db = CreateDb();
        var a = Account(); var b = Account("open-b"); db.Connections.AddRange(a, b); await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient(); var service = CreateService(db, api, multiAccount: true);
        var request = PublishRequest(a.TikTokConnectionId);
        var first = await service.InitializePublishAsync("user-a", request, default);
        Assert.Equal(first.PublishJobId, (await service.InitializePublishAsync("user-a", request, default)).PublishJobId);
        foreach (var changed in new[] { request with { ConnectionId = b.TikTokConnectionId }, request with { Title = "Different caption" } })
            Assert.Equal("tiktok_idempotency_conflict", (await Assert.ThrowsAsync<AccountApiException>(() => service.InitializePublishAsync("user-a", changed, default))).Code);
        Assert.Equal(1, api.InitializeCalls);
        var next = await service.InitializePublishAsync("user-a", request with { ClientRequestId = Guid.NewGuid() }, default);
        Assert.NotEqual(first.PublishJobId, next.PublishJobId); Assert.Equal(2, api.InitializeCalls);
    }

    [Fact]
    public async Task MultiAccount_AmbiguousInit_IsPersistedBeforeOutbound_AndNeverReplayedAfterRestart()
    {
        var options = new DbContextOptionsBuilder<TikTokDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new TikTokDbContext(options);
        var a = Account(); db.Connections.Add(a); await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient { InitializeError = new HttpRequestException("synthetic timeout") };
        api.BeforeInitialize = async () => {
            await using var observer = new TikTokDbContext(options);
            Assert.Equal("Initializing", (await observer.PublishAttempts.SingleAsync()).Status);
        };
        var request = PublishRequest(a.TikTokConnectionId);
        await Assert.ThrowsAsync<AccountApiException>(() => CreateService(db, api).InitializePublishAsync("user-a", request, default));
        await using var restarted = new TikTokDbContext(options);
        var service = CreateService(restarted, api);
        Assert.Equal("tiktok_publish_initialization_unknown", (await Assert.ThrowsAsync<AccountApiException>(() => service.InitializePublishAsync("user-a", request, default))).Code);
        Assert.Equal(1, api.InitializeCalls); Assert.Empty(await restarted.PublishJobs.ToListAsync());
        var unresolved = Assert.Single((await service.GetConnectionsStateAsync("user-a", default)).UnresolvedAttempts!);
        Assert.Equal(request.ClientRequestId, unresolved.ClientRequestId); Assert.Equal("Unknown", unresolved.Status);
    }

    [Fact]
    public async Task MultiAccount_ConcurrentInitAcrossContexts_CallsProviderOnce()
    {
        var options = new DbContextOptionsBuilder<TikTokDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new TikTokDbContext(options); await using var other = new TikTokDbContext(options);
        var a = Account(); db.Connections.Add(a); await db.SaveChangesAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeTikTokApiClient { BeforeInitialize = async () => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(5)); } };
        var request = PublishRequest(a.TikTokConnectionId);
        var first = CreateService(db, api).InitializePublishAsync("user-a", request, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = CreateService(other, api).InitializePublishAsync("user-a", request, default);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Equal(results[0].PublishJobId, results[1].PublishJobId); Assert.Equal(1, api.InitializeCalls);
    }

    [Fact]
    public async Task MultiAccount_RefreshCannotChangeIdentityOrUseAnotherAccountsToken()
    {
        await using var db = CreateDb();
        var a = Account(); a.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var b = Account("open-b"); db.Connections.AddRange(a, b); await db.SaveChangesAsync();
        var api = new FakeTikTokApiClient { OpenId = "open-b" };
        await Assert.ThrowsAsync<AccountApiException>(() => CreateService(db, api).GetCreatorInfoAsync("user-a", default, a.TikTokConnectionId));
        Assert.Equal("open-a", a.OpenId); Assert.Equal("access-open-a", a.ProtectedAccessToken);
        Assert.Equal("access-open-b", b.ProtectedAccessToken); Assert.Equal(1, api.RefreshCalls);
    }
}
