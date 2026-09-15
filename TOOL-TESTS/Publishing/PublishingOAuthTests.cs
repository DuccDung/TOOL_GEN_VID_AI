using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Publishing;

namespace TOOL_TESTS.Publishing;

public sealed class PublishingOAuthTests
{
    [Fact]
    public async Task YouTube_UsesPkceSingleUseStateAndEncryptedAccountBoundTokens()
    {
        await using var f = new Fixture(); var state = await f.Start();
        f.Http.Responses.Enqueue(Token()); f.Http.Responses.Enqueue("""{"items":[{"id":"channel_1","snippet":{"title":"Test channel"}}]}""");
        await f.Service.CompleteAsync(state, "test-authorization-code", default);
        var connection = await f.Db.Connections.SingleAsync();
        Assert.DoesNotContain("test-access-token", connection.ProtectedTokens);
        Assert.DoesNotContain("test-refresh-token", connection.ProtectedTokens);
        var tokens = await f.Service.TokenAsync(connection.ConnectionId, "owner", "YouTube", default);
        Assert.Equal("test-access-token", tokens.Token);
        Assert.Equal("publishing_connection_missing", (await Assert.ThrowsAsync<AccountApiException>(() => f.Service.TokenAsync(connection.ConnectionId, "another-user", "YouTube", default))).Code);
        Assert.Equal("publishing_oauth_expired", (await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CompleteAsync(state, "another-code", default))).Code);
        Assert.Equal(2, f.Http.Calls);
        connection.UserId = "another-user"; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CryptographicException>(() => f.Service.TokenAsync(connection.ConnectionId, "another-user", "YouTube", default));
        Assert.Equal(2, f.Http.Calls);
    }

    [Theory]
    [InlineData("https://www.googleapis.com/auth/youtube.readonly")]
    [InlineData("https://www.googleapis.com/auth/youtube.upload")]
    public async Task MissingGrantedScope_StopsBeforeChannelDiscovery(string scope)
    {
        await using var f = new Fixture(); var state = await f.Start();
        f.Http.Responses.Enqueue(Token(scope));
        Assert.Equal("publishing_scope_missing", (await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CompleteAsync(state, "test-code", default))).Code);
        Assert.Empty(f.Db.Connections); Assert.Equal(1, f.Http.Calls);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("role")]
    [InlineData("expired")]
    public async Task RevokedAuthorization_BlocksCallbackBeforeTokenExchange(string kind)
    {
        await using var f = new Fixture(); var state = await f.Start();
        if (kind == "session") { (await f.Accounts.UserSessions.SingleAsync()).RevokedAtUtc = DateTime.UtcNow; await f.Accounts.SaveChangesAsync(); }
        if (kind == "role") f.Access.Denied = true;
        if (kind == "expired") { (await f.Db.OAuthSessions.SingleAsync()).ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CompleteAsync(state, "test-code", default));
        Assert.Empty(f.Db.Connections); Assert.Equal(0, f.Http.Calls);
    }

    [Fact]
    public async Task Facebook_OnlyConnectsGrantedPagesWithContentPermission()
    {
        await using var f = new Fixture(); var state = await f.Start("Facebook");
        f.Http.Responses.Enqueue("""{"access_token":"test-short-token","expires_in":3600}""");
        f.Http.Responses.Enqueue("""{"access_token":"test-long-token","expires_in":5184000}""");
        f.Http.Responses.Enqueue("""{"data":[{"permission":"pages_show_list","status":"granted"},{"permission":"pages_read_engagement","status":"granted"},{"permission":"pages_manage_posts","status":"granted"}]}""");
        f.Http.Responses.Enqueue("""{"data":[{"id":"101","name":"Writable page","access_token":"test-page-token","tasks":["CREATE_CONTENT"]},{"id":"102","name":"Read only page","access_token":"read-token","tasks":["ANALYZE"]}]}""");
        await f.Service.CompleteAsync(state, "test-code", default);
        var connection = await f.Db.Connections.SingleAsync(); Assert.Equal("101", connection.ExternalId);
        Assert.Equal("test-page-token", (await f.Service.TokenAsync(connection.ConnectionId, "owner", "Facebook", default)).Token);
        Assert.DoesNotContain("test-page-token", connection.ProtectedTokens); Assert.Equal(4, f.Http.Calls);
    }

    private static string Token(string scope = "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly") =>
        PublishingService.Write(new { access_token = "test-access-token", refresh_token = "test-refresh-token", expires_in = 3600, scope });
    private sealed class Access : IGenerationAccessService
    {
        public bool Denied;
        public Task<GenerationAccessContext> RequireAsync(string user, Guid device, Guid? org, Guid? project, CancellationToken ct) => Denied
            ? throw new AccountApiException(403, "denied", "Permission removed") : Task.FromResult(new GenerationAccessContext(org!.Value, "test", "Owner", null));
    }
    private sealed class Http : HttpMessageHandler, IHttpClientFactory
    {
        public readonly Queue<string> Responses = new(); public int Calls;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Responses.Dequeue()) }); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly PublishingDbContext Db = new(new DbContextOptionsBuilder<PublishingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly AccountDbContext Accounts = new(new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Access Access = new(); public readonly Http Http = new();
        public readonly PublishingSocialService Service;
        private readonly Guid device = Guid.NewGuid(), session = Guid.NewGuid(), org = Guid.NewGuid();
        public Fixture()
        {
            var settings = new PublishingOptions { Enabled = true,
                YouTube = new() { Enabled = true, ClientId = "test-client", ClientSecret = "test-secret", RedirectUri = "https://app.example.test/api/publishing/oauth/callback" },
                Facebook = new() { Enabled = true, ClientId = "test-meta-client", ClientSecret = "test-secret", RedirectUri = "https://app.example.test/api/publishing/oauth/callback", ApiVersion = "v23.0" } };
            Service = new(Db, Accounts, Access, new EphemeralDataProtectionProvider(), Http, Options.Create(settings), TimeProvider.System);
        }
        public async Task<string> Start(string platform = "YouTube")
        {
            Accounts.UserSessions.Add(new() { SessionId = session, UserId = "owner", DeviceId = device, Status = "Active", AbsoluteExpiresAtUtc = DateTime.UtcNow.AddDays(1),
                User = new() { Id = "owner", AccountStatus = "Active" }, Device = new() { DeviceId = device, UserId = "owner", DeviceName = "Test" } });
            await Accounts.SaveChangesAsync();
            var result = await Service.StartAsync(new(org, platform), "owner", device, session, default);
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.AuthorizationUrl).Query);
            if (platform == "YouTube") { Assert.Equal("S256", query["code_challenge_method"]); Assert.NotEmpty(query["code_challenge"]!); }
            Assert.DoesNotContain("test-secret", result.AuthorizationUrl);
            var state = query["state"]!; Assert.NotEqual(state, (await Db.OAuthSessions.SingleAsync()).StateHash); return state;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Accounts.DisposeAsync(); Http.Dispose(); }
    }
}
