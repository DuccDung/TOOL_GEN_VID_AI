using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class ShortVideoOutfitServiceTests
{
    [Fact]
    public async Task Gateway_SubmitsOutfitToVeoWithApprovedFirstFrame_AndReplaysWithoutAnotherCharge()
    {
        await using var f = new Fixture(); var image = await f.Compose(await f.ImageQuote()); await f.Approve(image);
        var quote = await f.VideoQuote(); var frame = await f.Db.SceneFirstFrames.SingleAsync();
        var video = new FakeVeo();
        var firstFrames = new SceneFirstFrameService(f.Db, f.Access, f.Policy, f.Providers, f.Images, f.Cost, f.Budget,
            Options.Create(new OpenAiImageOptions()), NullLogger<SceneFirstFrameService>.Instance, TimeProvider.System);
        var gateway = new GenerationService(f.Db, f.Providers, null!, f.Images, null!, null!, f.Access, f.Budget, f.Cost,
            NullLogger<GenerationService>.Instance, TimeProvider.System, Options.Create(new OpenAiImageOptions()), Options.Create(new OpenAiSpeechOptions()),
            f.Policy, new VideoProviderRouter([video]), new UnusedOutputStore(), firstFrames, shortVideoOutfitService: f.Service);
        var request = new SubmitVideoRequest(f.Project.ProjectId, f.Scene.SceneId, $"outfit-video:{quote.QuoteId:N}", f.Org,
            ScenePlanVersion: 1, ScenePromptVersion: 1,
            FirstFrame: new(frame.SceneFirstFrameId, image.MimeType, Convert.ToBase64String(f.Images.Output), image.Sha256),
            ShortVideoComposition: new(image.CompositionId, quote.QuoteId, 1, image.MimeType, Convert.ToBase64String(f.Images.Output), image.Sha256));
        await Assert.ThrowsAsync<AccountApiException>(() => gateway.SubmitVideoAsync(request with { FirstFrame = null }, "owner", f.Device, default));
        Assert.Equal(0, video.Calls); Assert.Equal(1, f.Budget.Reserves);
        var first = await gateway.SubmitVideoAsync(request, "owner", f.Device, default);
        var replay = await gateway.SubmitVideoAsync(request, "owner", f.Device, default);
        Assert.Equal(first.ProviderRequestId, replay.ProviderRequestId); Assert.Equal("fal", first.ProviderCode);
        Assert.Equal(1, video.Calls); Assert.Equal(2, f.Budget.Reserves);
        Assert.Equal(image.Sha256, video.Image!.Sha256); Assert.Equal(8, video.Duration);
        var saved = await f.Db.ProviderRequests.SingleAsync(x => x.RequestKind == "Video");
        Assert.Equal(frame.SceneFirstFrameId, saved.InputSceneFirstFrameId);
        Assert.DoesNotContain(Convert.ToBase64String(f.Images.Output), saved.RequestJson);
    }

    private sealed class FakeVeo : IVideoProviderClient
    {
        public string ProviderCode => "fal"; public int Calls, Duration; public VideoProviderReferenceImage? Image;
        public Task<VideoProviderTaskResult> SubmitAsync(ProviderRuntimeConfiguration provider, string prompt, string ratio, int duration, string resolution, bool audio, string safety, VideoProviderReferenceImage? image, CancellationToken ct)
        { Calls++; Duration = duration; Image = image; return Task.FromResult(new VideoProviderTaskResult("veo-unit-test", "Submitted", 5, null, null, null, null, null, duration, "{}")); }
        public Task<VideoProviderTaskResult> GetStatusAsync(ProviderRuntimeConfiguration provider, string id, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class UnusedOutputStore : IVideoOutputStore
    {
        public Task CacheAsync(Guid id, string url, CancellationToken ct) => throw new NotSupportedException();
        public Task CopyToResponseAsync(Microsoft.AspNetCore.Http.HttpContext context, Guid id, string user, Guid device, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CleanupExpiredAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    [Theory]
    [InlineData(4, "9:16")]
    [InlineData(6, "16:9")]
    [InlineData(8, "9:16")]
    public async Task VeoQuote_UsesSupportedDurationAndModel(int seconds, string ratio)
    {
        await using var f = new Fixture(); f.Project.TargetDurationSeconds = seconds; f.Project.AspectRatio = ratio;
        await f.Db.SaveChangesAsync();
        var quote = await f.ImageQuote();
        Assert.Equal("gpt-image-2", quote.ModelCode); Assert.Equal(0, f.Images.Calls);
        Assert.Equal("fal", f.Project.VideoProviderCode);
    }

    [Theory]
    [InlineData(5, "9:16")]
    [InlineData(15, "9:16")]
    [InlineData(8, "1:1")]
    public async Task Veo_RejectsLegacyVariantsBeforeImageSpend(int seconds, string ratio)
    {
        await using var f = new Fixture(); f.Project.TargetDurationSeconds = seconds; f.Project.AspectRatio = ratio;
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.ImageQuote());
        Assert.Equal("short_video_veo_variant_invalid", error.Code); Assert.Equal(0, f.Images.Calls); Assert.Equal(0, f.Budget.Reserves);
    }

    [Fact]
    public async Task Approval_BindsRealVeoFirstFrame_AndInputChangeMakesItStale()
    {
        await using var f = new Fixture(); var image = await f.Compose(await f.ImageQuote()); await f.Approve(image);
        var frame = await f.Db.SceneFirstFrames.Include(x => x.MediaAsset).SingleAsync();
        var source = await f.Db.ProviderRequests.SingleAsync();
        Assert.Equal("Approved", frame.Status); Assert.Equal(image.CompositionId, frame.GeneratedByProviderRequestId);
        Assert.Equal(image.Sha256, frame.MediaAsset.Sha256); Assert.NotNull(frame.ApprovedByUserId);
        Assert.True(await ShortVideoVeoFirstFrame.IsCurrentAsync(f.Db, frame, source, default));
        await f.Service.SaveAsync(new(f.Project.ProjectId, f.Org, 1, f.A.Info, f.B.Info, "changed", "walk"), "owner", f.Device, default);
        Assert.False(await ShortVideoVeoFirstFrame.IsCurrentAsync(f.Db, frame, source, default));
    }

    [Theory]
    [InlineData("9:16", true)]
    [InlineData("16:9", false)]
    public async Task Migration_PreservesMatchingApprovedImage_ExpiresQuotes_AndNeverCallsAI(string ratio, bool preserved)
    {
        await using var f = new Fixture(); var image = await f.Compose(await f.ImageQuote());
        var unused = await f.ImageQuote();
        f.Project.VideoProviderCode = "kling"; f.Project.VideoModelCode = "kling-3.0"; f.Project.TargetDurationSeconds = 15;
        await f.Db.SaveChangesAsync(); await f.Approve(image);
        Assert.Empty(f.Db.SceneFirstFrames);
        var result = await f.Service.MigrateToVeoAsync(new(f.Project.ProjectId, f.Org, "kling", 8, ratio), "owner", f.Device, default);
        Assert.Equal(preserved, result.PreservedComposition); Assert.Equal("fal", f.Project.VideoProviderCode);
        Assert.Equal(8000, f.Scene.GenerationDurationMs); Assert.Equal(1, f.Images.Calls); Assert.Equal(1, f.Budget.Reserves);
        Assert.True((await f.Db.ShortVideoOperations.SingleAsync(x => x.OperationId == unused.QuoteId)).ExpiresAtUtc <= DateTime.UtcNow);
        var state = await f.State();
        if (preserved) { Assert.Equal(image.CompositionId, state.Composition!.CompositionId); Assert.Single(f.Db.SceneFirstFrames); }
        else { Assert.Null(state.Composition); Assert.Equal(2, state.Revision); }
        var replay = await f.Service.MigrateToVeoAsync(new(f.Project.ProjectId, f.Org, "kling", 8, ratio), "owner", f.Device, default);
        Assert.Equal(result, replay); Assert.Equal(1, f.Images.Calls);
    }

    [Fact]
    public async Task Migration_RejectsUnknownOperationWithoutChangingProvider()
    {
        await using var f = new Fixture(); f.Images.Fail = true;
        var quote = await f.ImageQuote(); await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(quote));
        f.Project.VideoProviderCode = "kling"; await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.MigrateToVeoAsync(new(f.Project.ProjectId, f.Org, "kling", 8, "9:16"), "owner", f.Device, default));
        Assert.Equal("short_video_operation_pending", error.Code); Assert.Equal("kling", f.Project.VideoProviderCode); Assert.Equal(1, f.Images.Calls);
    }

    [Fact]
    public async Task TextQuote_BindsFramePromptPriceAndScope_AndClaimIsSingleUse()
    {
        await using var f = new Fixture(); var image = await f.Compose(await f.ImageQuote()); await f.Approve(image);
        f.Scene.RequiredCapabilitiesJson = "{\"shortVideoMode\":\"TextOnly\",\"requiresFirstFrame\":true}"; await f.Db.SaveChangesAsync();
        var frame = await f.Db.SceneFirstFrames.SingleAsync();
        var quote = await f.Service.QuoteTextVideoAsync(new(f.Project.ProjectId, f.Org, 1, "Video"), "owner", f.Device, default);
        var request = new SubmitVideoRequest(f.Project.ProjectId, f.Scene.SceneId, $"short-video:{quote.QuoteId:N}", f.Org,
            ScenePlanVersion: 1, ScenePromptVersion: 1,
            FirstFrame: new(frame.SceneFirstFrameId, image.MimeType, Convert.ToBase64String(f.Images.Output), image.Sha256), ShortVideoQuoteId: quote.QuoteId);
        f.Cost.Rate = 99;
        var validated = await f.Service.ValidateTextQuoteAsync(request, f.Project, f.Policy.Snapshot, "owner", default);
        Assert.Equal(quote.EstimatedCost, validated.Quote.EstimatedCost);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ValidateTextQuoteAsync(request with { FirstFrame = request.FirstFrame! with { Sha256 = new string('f', 64) } }, f.Project, f.Policy.Snapshot, "owner", default));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ValidateTextQuoteAsync(request, f.Project, f.Policy.Snapshot, "another-user", default));
        await f.Service.ClaimTextQuoteAsync(quote.QuoteId, default);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ClaimTextQuoteAsync(quote.QuoteId, default));
        Assert.Equal(1, f.Budget.Reserves); // only the fixture image; quote/claim never submit AI
    }
    [Fact]
    public async Task Compose_UsesTwoInputs_ReplayDoesNotSpendAgain_AndPersistsOnlySafeRequestMetadata()
    {
        await using var f = new Fixture();
        var quote = await f.ImageQuote();
        var first = await f.Compose(quote);
        var replay = await f.Compose(quote);
        Assert.Equal(first, replay);
        Assert.Equal(1, f.Images.Calls); Assert.Equal(1, f.Budget.Reserves); Assert.Equal(1, f.Budget.Settles);
        Assert.Equal(2, f.Images.Inputs!.Count);
        Assert.NotEqual(f.Images.Inputs[0].Bytes, f.Images.Inputs[1].Bytes);
        Assert.Equal("PendingReview", (await f.State()).Composition!.Status);
        var request = await f.Db.ProviderRequests.SingleAsync();
        Assert.DoesNotContain("private-background", request.RequestJson);
        Assert.DoesNotContain(f.A.Base64Data, request.RequestJson);
        Assert.DoesNotContain("Base64", request.ResponseJson!, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("/api/generation/short-video/images/", first.ContentUrl);
    }

    [Fact]
    public async Task Timeout_RetainsReservation_BlocksReplayAndNewQuote()
    {
        await using var f = new Fixture(); f.Images.Fail = true;
        var quote = await f.ImageQuote();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(quote));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(quote));
        await Assert.ThrowsAsync<AccountApiException>(() => f.ImageQuote());
        Assert.Equal(1, f.Images.Calls); Assert.Equal(0, f.Budget.Releases);
        Assert.Equal("Unknown", (await f.Db.ProviderRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task DefiniteProviderRejection_ReleasesReservationAndAllowsANewQuote()
    {
        await using var f = new Fixture(); f.Images.Reject = true;
        var quote = await f.ImageQuote();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(quote));
        Assert.Equal(1, f.Budget.Releases); Assert.Equal("Failed", (await f.Db.ProviderRequests.SingleAsync()).Status);
        await f.ImageQuote(); Assert.Equal(1, f.Images.Calls);
    }

    [Fact]
    public async Task IncompatibleVideoPolicy_BlocksImageCostBeforeCallingImageProvider()
    {
        await using var f = new Fixture(); f.Policy.Snapshot = f.Policy.Snapshot with { ModelCode = "kling-omni" };
        await Assert.ThrowsAsync<AccountApiException>(() => f.ImageQuote());
        Assert.Equal(0, f.Budget.Reserves); Assert.Equal(0, f.Images.Calls);
    }

    [Fact]
    public async Task TwoPreviouslyIssuedQuotes_CannotBypassUncertainOperation()
    {
        await using var f = new Fixture();
        var first = await f.ImageQuote(); var second = await f.ImageQuote();
        f.Images.Fail = true;
        await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(first));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(second));
        Assert.Equal(1, f.Images.Calls); Assert.Equal(1, f.Budget.Reserves);
    }

    [Theory]
    [InlineData(0, 100, "pricing_not_configured")]
    [InlineData(1, 0, "organization_budget_exceeded")]
    public async Task Quote_RejectsMissingRateOrZeroBudgetBeforeOutbound(decimal rate, decimal budget, string code)
    {
        await using var f = new Fixture(); f.Cost.Rate = rate; f.Budget.Limit = budget;
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.ImageQuote());
        Assert.Equal(code, error.Code); Assert.Equal(0, f.Images.Calls); Assert.Equal(0, f.Budget.Reserves);
    }

    [Theory]
    [InlineData("organization_generation_denied")]
    [InlineData("project_not_found")]
    [InlineData("organization_membership_required")]
    public async Task AccessDenied_StopsBeforeQuoteOrOutbound(string code)
    {
        await using var f = new Fixture(); f.Access.Denied = code;
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.ImageQuote());
        Assert.Equal(code, error.Code); Assert.Equal(0, f.Providers.Calls); Assert.Equal(0, f.Images.Calls);
    }

    [Fact]
    public async Task ChangedInput_RejectsOldApprovalAndQuote_AndClearsVideoApproval()
    {
        await using var f = new Fixture();
        var quote = await f.ImageQuote(); var image = await f.Compose(quote); await f.Approve(image);
        f.Scene.ApprovedGenerationId = Guid.NewGuid(); f.Scene.ApprovedRenderMediaAssetId = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        var state = await f.Service.SaveAsync(new(f.Project.ProjectId, f.Org, 1, f.A.Info, f.B.Info, "changed", "walk"), "owner", f.Device, default);
        Assert.Equal(2, state.Revision); Assert.Null(state.Composition); Assert.Null(f.Scene.ApprovedGenerationId); Assert.Null(f.Scene.ApprovedRenderMediaAssetId);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Approve(image));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(quote));
        Assert.Equal(1, f.Images.Calls);
    }

    [Fact]
    public async Task RepeatedApproval_DoesNotInvalidateAnAlreadyApprovedVideo()
    {
        await using var f = new Fixture(); var image = await f.Compose(await f.ImageQuote()); await f.Approve(image);
        var generation = Guid.NewGuid(); f.Scene.ApprovedGenerationId = generation; await f.Db.SaveChangesAsync();
        await f.Approve(image); Assert.Equal(generation, f.Scene.ApprovedGenerationId);
    }

    [Fact]
    public async Task Video_RequiresApprovedCurrentImageAndBindsHashQuoteAndFirstFrame()
    {
        await using var f = new Fixture(); var image = await f.Compose(await f.ImageQuote());
        await Assert.ThrowsAsync<AccountApiException>(() => f.VideoQuote());
        await f.Approve(image); var quote = await f.VideoQuote();
        var input = new ShortVideoCompositionInput(image.CompositionId, quote.QuoteId, 1, image.MimeType, Convert.ToBase64String(f.Images.Output), image.Sha256);
        var result = await f.Service.ValidateVideoAsync(f.Project, f.Scene.SceneId, input, f.Policy.Snapshot, "owner", default);
        Assert.True(result.Image.IsFirstFrame); Assert.Equal(image.Sha256, result.Image.Sha256); Assert.Equal(quote.EstimatedCost, result.Quote.EstimatedCost);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ValidateVideoAsync(f.Project, f.Scene.SceneId, null, f.Policy.Snapshot, "owner", default));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ValidateVideoAsync(f.Project, f.Scene.SceneId, input with { Sha256 = new string('f', 64) }, f.Policy.Snapshot, "owner", default));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ValidateVideoAsync(f.Project, f.Scene.SceneId, input, f.Policy.Snapshot, "another-user", default));
        await f.Service.ClaimVideoAsync(input, default);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ClaimVideoAsync(input, default));
    }

    [Theory]
    [InlineData("kling", "kling-omni")]
    [InlineData("byteplus", "seedance")]
    [InlineData("fal", "veo")]
    [InlineData("kling", "unknown-model")]
    public async Task Video_RejectsUnverifiedModelInsteadOfTextFallback(string provider, string model)
    {
        await using var f = new Fixture(); var image = await f.Compose(await f.ImageQuote()); await f.Approve(image);
        f.Policy.Snapshot = f.Policy.Snapshot with { ProviderCode = provider, ModelCode = model };
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.VideoQuote());
        Assert.Equal("short_video_veo_required", error.Code);
    }

    [Fact]
    public async Task ChangedBytesOrExpiredQuote_DoNotReserveOrCallProvider()
    {
        await using var f = new Fixture(); var quote = await f.ImageQuote();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ComposeAsync(new(f.Project.ProjectId, f.Org, quote.QuoteId, f.A with { Base64Data = f.B.Base64Data }, f.B), "owner", f.Device, default));
        var op = await f.Db.ShortVideoOperations.SingleAsync(); op.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Compose(quote));
        Assert.Equal(0, f.Budget.Reserves); Assert.Equal(0, f.Images.Calls);
    }

    [Fact]
    public async Task DisabledFeature_ReadsSafeStateAndRejectsMutation()
    {
        await using var f = new Fixture(); f.Options.Enabled = false;
        Assert.False((await f.State()).Enabled);
        await Assert.ThrowsAsync<AccountApiException>(() => f.ImageQuote()); Assert.Equal(0, f.Images.Calls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public VideoFactoryDbContext Db { get; } = new(new DbContextOptionsBuilder<VideoFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        public Guid Org { get; } = Guid.NewGuid(); public Guid Device { get; } = Guid.NewGuid();
        public Project Project { get; } public Scene Scene { get; } public ShortVideoOutfitService Service { get; }
        public Access Access { get; } public Policy Policy { get; } = new(); public Providers Providers { get; } = new();
        public Images Images { get; } = new(); public Budget Budget { get; } = new(); public Cost Cost { get; } = new(); public ShortVideoOutfitOptions Options { get; } = new() { Enabled = true };
        public ShortVideoImageInput A { get; } = Input(200, 300); public ShortVideoImageInput B { get; } = Input(300, 400);
        public Fixture()
        {
            Project = new() { ProjectId = Guid.NewGuid(), OrganizationId = Org, RemoteUserId = "owner", CreatedByUserId = "owner", Name = "short", Topic = "short", LanguageCode = "vi-VN", Platform = "YouTubeShorts", AspectRatio = "9:16", TargetDurationSeconds = 8, Status = "ScenePlanning", CurrentScenePlanVersion = 1, CurrencyCode = "USD", WorkspaceRelativePath = "test", RowVersion = new byte[8] };
            var script = new Script { ScriptId = Guid.NewGuid(), ProjectId = Project.ProjectId, Project = Project, StructureType = "DirectShortVideo", Version = 1, FullText = "short", StoryBeatsJson = "[]", Status = "Approved", RowVersion = new byte[8] };
            Scene = new() { SceneId = Guid.NewGuid(), ProjectId = Project.ProjectId, Project = Project, ScriptId = script.ScriptId, Script = script, StyleProfileId = Guid.NewGuid(), ScenePlanVersion = 1, SequenceNumber = 1, ContentDurationMs = 8000, GenerationDurationMs = 8000, StoryPurpose = "short", VisualDescription = "short", CharacterIdsJson = "[]", EntryStateJson = "{}", ExitStateJson = "{}", Status = "PromptReady", RequiredCapabilitiesJson = "{\"shortVideoMode\":\"CharacterOutfit\"}", RowVersion = new byte[8] };
            Scene.ScenePrompts.Add(new ScenePrompt { ScenePromptId = Guid.NewGuid(), SceneId = Scene.SceneId, Version = 1, Status = "Approved", PromptTemplateName = "manual-short-video", PromptTemplateVersion = "1", CanonicalInputJson = "{}", FinalPrompt = "walk", NegativePrompt = "", PromptHash = "test", RowVersion = new byte[8] });
            Db.AddRange(Project, script, Scene, new ShortVideoOutfit { SceneId = Scene.SceneId, ProjectId = Project.ProjectId, Revision = 1, CharacterJson = JsonSerializer.Serialize(A.Info, Json), OutfitJson = JsonSerializer.Serialize(B.Info, Json), Background = "private-background", Motion = "walk" }); Db.SaveChanges();
            Access = new(Project);
            Service = new(Db, Access, Policy, Providers, Cost, Budget, Images, Microsoft.Extensions.Options.Options.Create(new OpenAiImageOptions()), Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        }
        public Task<ShortVideoQuote> ImageQuote() => Service.QuoteAsync(new(Project.ProjectId, Org, 1, "Image"), "owner", Device, default);
        public Task<ShortVideoQuote> VideoQuote() => Service.QuoteAsync(new(Project.ProjectId, Org, 1, "Video"), "owner", Device, default);
        public Task<ShortVideoComposition> Compose(ShortVideoQuote q) => Service.ComposeAsync(new(Project.ProjectId, Org, q.QuoteId, A, B), "owner", Device, default);
        public Task<ShortVideoState> State() => Service.GetAsync(Project.ProjectId, Org, "owner", Device, default);
        public Task<ShortVideoState> Approve(ShortVideoComposition c) => Service.ApproveAsync(new(Project.ProjectId, Org, c.CompositionId, c.Revision, true), "owner", Device, default);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static byte[] Png(int w, int h) { var b = new byte[24]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(b, 0); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h); return b; }
    private static ShortVideoImageInput Input(int w, int h) { var b = Png(w, h); return new(new(Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant(), "image/png", b.Length, w, h), Convert.ToBase64String(b)); }
    private sealed class Access(Project project) : IGenerationAccessService
    {
        public string? Denied;
        public Task<GenerationAccessContext> RequireAsync(string userId, Guid deviceId, Guid? requestedOrganizationId, Guid? projectId, CancellationToken cancellationToken) => Denied is null ? Task.FromResult(new GenerationAccessContext(project.OrganizationId!.Value, "org", "Owner", project)) : throw new AccountApiException(403, Denied, "denied");
    }
    private sealed class Policy : IProjectVideoPolicyResolver
    {
        public ProjectVideoSnapshot Snapshot = new("fal", "Fal", FalVeoPolicy.StandardEndpointId, "Veo 3.1", 1, "720p", true, VideoModelCapabilities.KlingDefault);
        public Task<ProjectVideoSnapshot> ResolveAsync(Project project, Guid organizationId, string policyScope, CancellationToken cancellationToken) => Task.FromResult(SnapshotProject(project));
        private ProjectVideoSnapshot SnapshotProject(Project p) { p.VideoProviderCode = Snapshot.ProviderCode; p.VideoModelCode = Snapshot.ModelCode; p.VideoPolicyVersion = Snapshot.PolicyVersion; p.VideoResolution = Snapshot.Resolution; p.VideoNativeAudio = Snapshot.NativeAudio; return Snapshot; }
    }
    private sealed class Providers : IProviderRuntimeResolver
    {
        private readonly Guid provider = Guid.NewGuid(), model = Guid.NewGuid(), credential = Guid.NewGuid(); public int Calls;
        public Task<ProviderRuntimeConfiguration> ResolveAsync(Guid organizationId, string providerCode, string modality, Guid? credentialId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new ProviderRuntimeConfiguration(provider, model, credential, providerCode, modality == "Image" ? "gpt-image-2" : FalVeoPolicy.StandardEndpointId, new("https://api.openai.com/v1/"), "Bearer", null, "fake-unit-test-credential")); }
        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Images : IOpenAiImageClient
    {
        public int Calls; public bool Fail, Reject; public IReadOnlyList<OpenAiImageEditInput>? Inputs; public byte[] Output = Png(720, 1280);
        public Task<OpenAiImageResult> GenerateAsync(ProviderRuntimeConfiguration provider, string prompt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OpenAiImageResult> GenerateOutfitAsync(ProviderRuntimeConfiguration provider, string prompt, string aspectRatio, IReadOnlyList<OpenAiImageEditInput> sources, CancellationToken cancellationToken)
        { Calls++; Inputs = sources; if (Fail) throw new TimeoutException(); if (Reject) throw new ProviderHttpException("openai", "openai_image_moderation_blocked", "rejected", statusCode: System.Net.HttpStatusCode.BadRequest); return Task.FromResult(new OpenAiImageResult(new(Output, "image/png", Convert.ToHexString(SHA256.HashData(Output)).ToLowerInvariant(), 720, 1280), 100, 200, "fake-request")); }
    }
    private sealed class Cost : IAiCostEstimator
    {
        public decimal Rate = 1;
        public Task<AiCostQuote> QuoteVideoAsync(string providerCode, Guid modelId, int seconds, string resolution, bool audio, int fps, CancellationToken ct)
        { Assert.Equal("fal", providerCode); return Task.FromResult(new AiCostQuote(Rate, "USD", "{}")); }
        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) => Task.FromResult(new AiCostQuote(Rate, "USD", "{}"));
        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) => Task.FromResult(.2m);
        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) => Task.FromResult(new AiCostQuote(Rate, "USD", "{}"));
        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Budget : IAiBudgetService
    {
        public decimal Limit = 100; public int Reserves, Settles, Releases;
        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(new BudgetSnapshot(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(1), Limit, 0, 0, Limit, "USD"));
        public Task<BudgetReservationResult> ReserveAsync(Guid organizationId, string userId, Guid projectId, Guid providerRequestId, string operationKey, string providerCode, string modelCode, decimal amount, CancellationToken cancellationToken) { Reserves++; return Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD")); }
        public Task SettleAsync(Guid reservationId, decimal actualAmount, Guid? organizationProviderCredentialId, object? usage, object? rateSnapshot, CancellationToken cancellationToken) { Settles++; return Task.CompletedTask; }
        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) { Releases++; return Task.CompletedTask; }
    }
}
