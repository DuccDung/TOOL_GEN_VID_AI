using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Publishing;
using TOOL_SERVER.TikTok;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Publishing;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_TESTS.Publishing;

public sealed class PublishingTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 5, 0, 0, DateTimeKind.Utc);
    internal static PublishingScheduleInput Input() => new("Sản phẩm mới", "Nhân vật giới thiệu sản phẩm trong không gian sáng.", Guid.NewGuid(), Guid.NewGuid(),
        "2026-09-12", "2026-09-18", "19:00", "Asia/Ho_Chi_Minh", 127, 120, "SameDay", 8, "9:16", 5,
        [new("YouTube", Guid.NewGuid(), "private")]);

    [Fact]
    public void Calendar_UsesLocalClockAndInclusiveEndDate()
    {
        var input = Input();
        Assert.Equal(new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc), PublishingCalendar.Next(input, Now));
        Assert.Equal(new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc), PublishingCalendar.Next(input, new(2026, 9, 18, 11, 59, 0, DateTimeKind.Utc)));
        Assert.Null(PublishingCalendar.Next(input, new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Calendar_WeekdayMaskUsesMondayThroughSunday()
    {
        var input = Input() with { Weekdays = 1 };
        Assert.Equal(new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc), PublishingCalendar.Next(input, Now));
        Assert.Equal(new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc), PublishingCalendar.Next(input with { Weekdays = 64 }, Now));
    }

    [Fact]
    public void Calendar_DstGapIsSkipped_OverlapOccursOnlyOnce()
    {
        var spring = Input() with { StartDate = "2026-03-08", EndDate = "2026-03-09", PublishTime = "02:30", TimeZoneId = "America/New_York" };
        Assert.Equal(new DateTime(2026, 3, 9, 6, 30, 0, DateTimeKind.Utc), PublishingCalendar.Next(spring, new(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc)));
        var autumn = spring with { StartDate = "2026-11-01", EndDate = "2026-11-02", PublishTime = "01:30" };
        var first = PublishingCalendar.Next(autumn, new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), first);
        Assert.Equal(new DateTime(2026, 11, 2, 6, 30, 0, DateTimeKind.Utc), PublishingCalendar.Next(autumn, first!.Value));
    }

    [Fact]
    public void Calendar_LatePolicyRespectsLocalMidnight()
    {
        var publish = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 12, 17, 0, 0, DateTimeKind.Utc), PublishingCalendar.Deadline(Input(), publish));
        Assert.Equal(publish.AddMinutes(5), PublishingCalendar.Deadline(Input() with { LatePolicy = "Skip" }, publish));
    }

    [Theory]
    [InlineData(0, 120, 8)] [InlineData(128, 120, 8)] [InlineData(127, 0, 8)] [InlineData(127, 1441, 8)] [InlineData(127, 120, 30)]
    public void InvalidFrequencyAndDuration_AreRejected(int days, int lead, int duration) => Assert.Throws<AccountApiException>(() =>
        PublishingCalendar.Validate(Input() with { Weekdays = days, LeadMinutes = lead, DurationSeconds = duration }, Now));

    [Theory]
    [InlineData("2026-09-12", "2027-01-01", "19:00", "UTC")]
    [InlineData("2026-09-12", "2026-09-11", "19:00", "UTC")]
    [InlineData("2026-09-12", "2026-09-13", "25:00", "UTC")]
    [InlineData("2026-09-12", "2026-09-13", "19:00", "invalid/timezone")]
    public void InvalidDates_AreRejected(string start, string end, string clock, string zone) => Assert.Throws<AccountApiException>(() =>
        PublishingCalendar.Validate(Input() with { StartDate = start, EndDate = end, PublishTime = clock, TimeZoneId = zone }, Now));

    [Fact]
    public void Targets_RejectDuplicatesAndUnreviewedTikTokPrivacy()
    {
        var target = new PublishingTarget("TikTok", Guid.NewGuid(), "PUBLIC_TO_EVERYONE");
        Assert.Throws<AccountApiException>(() => PublishingCalendar.Validate(Input() with { Targets = [target] }, Now));
        target = target with { Privacy = "review" };
        PublishingCalendar.Validate(Input() with { Targets = [target] }, Now);
        Assert.Throws<AccountApiException>(() => PublishingCalendar.Validate(Input() with { Targets = [target, target] }, Now));
    }

    [Theory]
    [InlineData(0, 1, "USD")] [InlineData(5, 1, "USD")] [InlineData(0, 0, "USD")] [InlineData(0, 1, "VND")]
    public void QuoteLimit_FailsClosed(decimal used, decimal quote, string currency)
    {
        var limit = used == 0 && quote == 1 && currency == "USD" ? .5m : 5;
        Assert.Throws<AccountApiException>(() => PublishingProduction.RequireQuoteWithinLimit(new(Guid.NewGuid(), "Image", quote, currency, "model", "720p", true, Now.AddMinutes(5), 1), used, limit));
    }

    [Theory]
    [InlineData("http://www.googleapis.com/upload")]
    [InlineData("https://www.googleapis.com.evil.test/upload")]
    [InlineData("https://user@www.googleapis.com/upload")]
    [InlineData("https://www.googleapis.com:8443/upload")]
    [InlineData("https://127.0.0.1/upload")]
    public void PlatformUrls_RejectUntrustedDestinations(string uri) => Assert.Throws<AccountApiException>(() => PublishingSocialService.ValidateUri(new(uri)));

    [Fact]
    public void MediaValidation_RequiresVideoAudioDurationAndRatio()
    {
        const string valid = """{"streams":[{"codec_type":"video","codec_name":"h264","width":720,"height":1280},{"codec_type":"audio","codec_name":"aac"}],"format":{"duration":"8.05"}}""";
        PublishingMedia.ValidateProbe(valid, Input());
        Assert.Throws<AccountApiException>(() => PublishingMedia.ValidateProbe(valid.Replace("8.05", "30.0"), Input()));
        Assert.Throws<AccountApiException>(() => PublishingMedia.ValidateProbe(valid.Replace("h264", "vp9"), Input()));
        Assert.Throws<AccountApiException>(() => PublishingMedia.ValidateProbe(valid, Input() with { AspectRatio = "16:9" }));
    }

    [Fact]
    public void Facebook_RejectsUnsupportedVideoBeforeSchedulingOrPublishing()
    {
        var input = Input() with { Targets = [new("Facebook", Guid.NewGuid(), "public")] };
        Assert.Throws<AccountApiException>(() => PublishingCalendar.Validate(input with { AspectRatio = "16:9" }, Now));
        const string valid = """{"streams":[{"codec_type":"video","codec_name":"h264","width":720,"height":1280,"r_frame_rate":"24/1"},{"codec_type":"audio","codec_name":"aac"}],"format":{"duration":"8.05"}}""";
        PublishingMedia.ValidateProbe(valid, input);
        Assert.Throws<AccountApiException>(() => PublishingMedia.ValidateProbe(valid.Replace("24/1", "12/1"), input));
        Assert.Throws<AccountApiException>(() => PublishingMedia.ValidateProbe(valid.Replace("720", "360").Replace("1280", "640"), input));
    }

    [Fact]
    public async Task DisabledState_DoesNotTouchSchemaOrProvider()
    {
        await using var f = new Fixture(); f.Options.Enabled = false;
        var state = await f.Service.StateAsync(f.Org, "owner", f.Device, default);
        Assert.False(state.Enabled); Assert.Empty(state.Runs); Assert.Equal(0, f.Access.Calls); Assert.Equal(0, f.Http.Calls);
    }

    [Fact]
    public async Task Images_AreEncryptedAndBoundToUserOrganizationAndRole()
    {
        await using var f = new Fixture(); await f.SeedSession();
        var image = await f.Service.UploadImageAsync(new(f.Org, "Character", Image()), "owner", f.Device, default);
        var stored = await f.Db.Images.SingleAsync();
        Assert.NotEqual(Convert.FromBase64String(Image().Base64Data), stored.ProtectedPayload);
        Assert.Equal(Image(), await f.Service.ImageAsync(image.ImageId, f.Org, "owner", "Character", default));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ImageAsync(image.ImageId, f.Org, "other", "Character", default));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ImageAsync(image.ImageId, Guid.NewGuid(), "owner", "Character", default));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ImageAsync(image.ImageId, f.Org, "owner", "Product", default));
    }

    [Fact]
    public async Task Save_IsIdempotent_RejectsStaleAndCrossTenantMutations()
    {
        await using var f = new Fixture(); var request = await f.DraftRequest();
        var first = await f.Service.SaveAsync(request, "owner", f.Device, f.Session, default);
        var again = await f.Service.SaveAsync(request, "owner", f.Device, f.Session, default);
        Assert.Equal(PublishingService.Write(first), PublishingService.Write(again)); Assert.Equal(1, await f.Db.Schedules.CountAsync()); Assert.Equal(0, f.Http.Calls);
        var stale = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SaveAsync(request with { Input = request.Input with { Title = "Changed" } }, "owner", f.Device, f.Session, default));
        Assert.Equal("publishing_revision_changed", stale.Code);
        var other = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ChangeAsync(new(Guid.NewGuid(), first.ScheduleId, 1, "Pause"), "owner", f.Device, f.Session, default));
        Assert.Equal("publishing_not_found", other.Code);
    }

    [Fact]
    public async Task ViewerCannotUploadOrSave_AndNoOutboundOccurs()
    {
        await using var f = new Fixture(); await f.SeedSession(); f.Access.Denied = true;
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.UploadImageAsync(new(f.Org, "Character", Image()), "owner", f.Device, default));
        Assert.Empty(f.Db.Images); Assert.Equal(0, f.Http.Calls);
    }

    [Fact]
    public async Task ActivationRequiresSeparateCostConsent_AndWorkerReadiness()
    {
        await using var f = new Fixture(); var request = await f.DraftRequest();
        var saved = await f.Service.SaveAsync(request, "owner", f.Device, f.Session, default);
        var denied = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ChangeAsync(new(f.Org, saved.ScheduleId, saved.Revision, "Activate"), "owner", f.Device, f.Session, default));
        Assert.Equal("publishing_consent_required", denied.Code);
        f.Options.WorkerEnabled = false;
        denied = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ChangeAsync(new(f.Org, saved.ScheduleId, saved.Revision, "Activate", true), "owner", f.Device, f.Session, default));
        Assert.Equal("publishing_worker_disabled", denied.Code); Assert.Equal("Draft", (await f.Db.Schedules.SingleAsync()).Status);
    }

    [Fact]
    public async Task Dispatcher_CreatesOccurrenceOnce_AndSnapshotsConsentAndTargets()
    {
        await using var f = new Fixture(); await f.SeedSession();
        var schedule = f.Schedule(Now.AddMinutes(90)); f.Db.Schedules.Add(schedule); await f.Db.SaveChangesAsync();
        await f.Dispatcher.MaterializeAsync(default); await f.Dispatcher.MaterializeAsync(default);
        var run = await f.Db.Runs.SingleAsync(); Assert.Equal(schedule.InputJson, run.InputJson);
        Assert.Equal(schedule.ConsentAtUtc, run.ProductionConsentAtUtc); Assert.Single(f.Db.Deliveries);
        schedule.InputJson = PublishingService.Write(Input() with { Title = "Edited later" }); await f.Db.SaveChangesAsync();
        Assert.NotEqual(schedule.InputJson, (await f.Db.Runs.SingleAsync()).InputJson);
    }

    [Fact]
    public async Task MissedOccurrence_IsSkippedWithoutCharging()
    {
        await using var f = new Fixture(); var schedule = f.Schedule(Now.AddDays(-2));
        f.Db.Schedules.Add(schedule); await f.Db.SaveChangesAsync();
        await f.Dispatcher.MaterializeAsync(default);
        Assert.Equal("Skipped", (await f.Db.Runs.SingleAsync()).Status); Assert.Equal(0, f.Production.Calls); Assert.Equal(0, f.Publisher.Calls);
    }

    [Fact]
    public async Task RevokedSession_StopsBeforeProductionOrPublishing()
    {
        await using var f = new Fixture(); await f.SeedSession();
        var schedule = f.Schedule(Now.AddMinutes(90)); f.Db.Schedules.Add(schedule); await f.Db.SaveChangesAsync();
        var session = await f.Accounts.UserSessions.SingleAsync(); session.Status = "Revoked"; await f.Accounts.SaveChangesAsync();
        await f.Dispatcher.TickAsync(default);
        var run = await f.Db.Runs.SingleAsync(); Assert.Equal("NeedsAttention", run.Status); Assert.Equal("publishing_session_expired", run.ErrorCode);
        Assert.Equal(0, f.Production.Calls); Assert.Equal(0, f.Publisher.Calls);
    }

    [Fact]
    public async Task AccessLostAfterActivation_StopsBeforeOutbound()
    {
        await using var f = new Fixture(); await f.SeedSession();
        f.Db.Schedules.Add(f.Schedule(Now.AddMinutes(90))); await f.Db.SaveChangesAsync(); f.Access.Denied = true;
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("organization_generation_denied", (await f.Db.Runs.SingleAsync()).ErrorCode); Assert.Equal(0, f.Production.Calls);
    }

    [Fact]
    public async Task PausedSchedule_DoesNotAdvanceAlreadyCreatedRun()
    {
        await using var f = new Fixture(); await f.SeedSession();
        f.Db.Schedules.Add(f.Schedule(Now.AddMinutes(90))); await f.Db.SaveChangesAsync(); await f.Dispatcher.MaterializeAsync(default);
        var schedule = await f.Db.Schedules.SingleAsync(); schedule.Status = "Paused"; await f.Db.SaveChangesAsync();
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("publishing_schedule_paused", (await f.Db.Runs.SingleAsync()).ErrorCode); Assert.Equal(0, f.Production.Calls);
    }

    [Fact]
    public async Task Worker_RecoversExpiredLease_AndRespectsLiveLease()
    {
        await using var f = new Fixture(); await f.SeedSession();
        f.Db.Schedules.Add(f.Schedule(Now.AddMinutes(90))); await f.Db.SaveChangesAsync(); await f.Dispatcher.MaterializeAsync(default);
        var run = await f.Db.Runs.SingleAsync(); run.LeaseId = Guid.NewGuid(); run.LeaseUntilUtc = Now.AddMinutes(5); await f.Db.SaveChangesAsync();
        await f.Dispatcher.TickAsync(default); Assert.Equal(0, f.Production.Calls);
        run = await f.Db.Runs.SingleAsync(); run.LeaseUntilUtc = Now.AddMinutes(-1); await f.Db.SaveChangesAsync();
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Production.Calls); Assert.Null((await f.Db.Runs.SingleAsync()).LeaseId);
    }

    [Fact]
    public async Task Worker_RecoversQueuedRunWithReservedButNotYetCreatedProjectId()
    {
        await using var f = new Fixture(); await f.SeedSession();
        f.Db.Schedules.Add(f.Schedule(Now.AddMinutes(90))); await f.Db.SaveChangesAsync(); await f.Dispatcher.MaterializeAsync(default);
        var run = await f.Db.Runs.SingleAsync(); run.ProjectId = run.RunId; await f.Db.SaveChangesAsync();
        f.Access.ProjectDoesNotExist = true;
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(1, f.Production.Calls); Assert.Equal("Generating", (await f.Db.Runs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Worker_WaitsUntilPublishTime_ThenDispatches()
    {
        await using var f = new Fixture(); await f.SeedSession();
        f.Db.Schedules.Add(f.Schedule(Now.AddMinutes(90))); await f.Db.SaveChangesAsync(); await f.Dispatcher.MaterializeAsync(default);
        var run = await f.Db.Runs.SingleAsync(); run.Status = "ReadyToPublish"; await f.Db.SaveChangesAsync();
        await f.Dispatcher.TickAsync(default); Assert.Equal(0, f.Publisher.Calls);
        f.Clock.Utc = Now.AddMinutes(91); await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Publisher.Calls);
    }

    [Fact]
    public async Task RealRelationalIndex_RejectsDuplicateOccurrence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new PublishingDbContext(new DbContextOptionsBuilder<PublishingDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var id = Guid.NewGuid();
        db.Schedules.Add(new() { ScheduleId = id, OrganizationId = Guid.NewGuid(), UserId = "owner", InputJson = "{}", InputHash = new('a', 64) });
        db.Runs.Add(new() { RunId = Guid.NewGuid(), ScheduleId = id, PublishAtUtc = Now }); await db.SaveChangesAsync();
        db.Runs.Add(new() { RunId = Guid.NewGuid(), ScheduleId = id, PublishAtUtc = Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static ShortVideoImageInput Image()
    {
        var bytes = new byte[32]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), 200); BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), 300);
        return new(new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "image/png", bytes.Length, 200, 300), Convert.ToBase64String(bytes));
    }

    private sealed class Clock : TimeProvider { public DateTime Utc = Now; public override DateTimeOffset GetUtcNow() => new(Utc); }
    private sealed class Access : IGenerationAccessService
    {
        public bool Denied; public bool ProjectDoesNotExist; public int Calls;
        public Task<GenerationAccessContext> RequireAsync(string user, Guid device, Guid? org, Guid? project, CancellationToken ct)
        { Calls++; if (Denied) throw new AccountApiException(403, "organization_generation_denied", "Viewer cannot generate");
            if (ProjectDoesNotExist && project is not null) throw new AccountApiException(404, "project_not_found", "Project does not exist yet");
            return Task.FromResult(new GenerationAccessContext(org!.Value, "org", "Owner", null)); }
    }
    private sealed class Production : IPublishingProduction { public int Calls; public Task StepAsync(PublishingRun run, CancellationToken ct) { Calls++; run.Status = "Generating"; return Task.CompletedTask; } }
    private sealed class Publisher : IPublishingPublisher { public int Calls; public Task StepAsync(PublishingRun run, CancellationToken ct) { Calls++; return Task.CompletedTask; } }
    private sealed class Http : IHttpClientFactory { public int Calls; public HttpClient CreateClient(string name) { Calls++; throw new InvalidOperationException("Unexpected network request"); } }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Guid Org = Guid.NewGuid(), Device = Guid.NewGuid(), Session = Guid.NewGuid();
        public readonly PublishingDbContext Db = new(new DbContextOptionsBuilder<PublishingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly AccountDbContext Accounts = new(new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly PublishingOptions Options = new() { Enabled = true, WorkerEnabled = true };
        public readonly Clock Clock = new(); public readonly Access Access = new(); public readonly Http Http = new();
        public readonly Production Production = new(); public readonly Publisher Publisher = new();
        public readonly PublishingService Service; public readonly PublishingDispatcher Dispatcher;
        public Fixture()
        {
            var protection = new EphemeralDataProtectionProvider(); var configured = Microsoft.Extensions.Options.Options.Create(Options);
            var social = new PublishingSocialService(Db, Accounts, Access, protection, Http, configured, Clock);
            Service = new(Db, Accounts, Access, new TikTokStub(), social, protection, configured, Clock);
            Dispatcher = new(Db, Service, Production, Publisher, Access, configured, Clock);
        }
        public async Task SeedSession()
        {
            Accounts.UserSessions.Add(new() { SessionId = Session, UserId = "owner", DeviceId = Device, Status = "Active", AbsoluteExpiresAtUtc = Now.AddDays(30),
                User = new() { Id = "owner", AccountStatus = "Active" }, Device = new() { DeviceId = Device, UserId = "owner", DeviceName = "Test device" } });
            await Accounts.SaveChangesAsync();
        }
        public PublishingSchedule Schedule(DateTime publish) => new() { ScheduleId = Guid.NewGuid(), OrganizationId = Org, UserId = "owner", DeviceId = Device,
            SessionId = Session, Status = "Active", ConsentAtUtc = Now.AddHours(-1), NextPublishAtUtc = publish, InputJson = PublishingService.Write(Input()), InputHash = new('a', 64) };
        public async Task<SavePublishingScheduleRequest> DraftRequest()
        {
            await SeedSession();
            var a = await Service.UploadImageAsync(new(Org, "Character", Image()), "owner", Device, default);
            var b = await Service.UploadImageAsync(new(Org, "Product", Image()), "owner", Device, default);
            var connection = new PublishingConnection { ConnectionId = Guid.NewGuid(), UserId = "owner", Platform = "YouTube", ExternalId = "channel", DisplayName = "Channel" };
            Db.Connections.Add(connection); await Db.SaveChangesAsync();
            return new(Org, Guid.NewGuid(), 0, Input() with { CharacterImageId = a.ImageId, ProductImageId = b.ImageId, Targets = [new("YouTube", connection.ConnectionId, "private")] });
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Accounts.DisposeAsync(); }
    }

    private sealed class TikTokStub : ITikTokService
    {
        public Task<TikTokFeatureStateResponse> GetConnectionsStateAsync(string user, CancellationToken ct) => Task.FromResult(new TikTokFeatureStateResponse(false, false, null, Connections: []));
        public Task<TikTokFeatureStateResponse> GetStateAsync(string user, CancellationToken ct) => GetConnectionsStateAsync(user, ct);
        public Task<StartTikTokOAuthResponse> StartOAuthAsync(string user, Guid device, StartTikTokOAuthRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<TikTokFeatureStateResponse> CompleteOAuthAsync(string user, Guid device, CompleteTikTokOAuthRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DisconnectAsync(string user, CancellationToken ct, Guid? connection = null) => throw new NotSupportedException();
        public Task<TikTokCreatorInfoResponse> GetCreatorInfoAsync(string user, CancellationToken ct, Guid? connection = null) => throw new NotSupportedException();
        public Task<InitializeTikTokPublishResponse> InitializePublishAsync(string user, InitializeTikTokPublishRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<TikTokPublishStatusResponse> GetPublishStatusAsync(string user, Guid job, CancellationToken ct) => throw new NotSupportedException();
        public Task<TikTokPublishHistoryResponse> GetPublishHistoryAsync(string user, Guid? connection, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<TikTokPublishStatusResponse> ReadPublishStatusAsync(string user, Guid job, CancellationToken ct) => throw new NotSupportedException();
    }
}
