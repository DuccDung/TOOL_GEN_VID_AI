using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.LocalVoice;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.LocalVoice;

[Collection(NativeWindowsCollection.Name)]
public sealed class LocalVoiceServiceTests
{
    [Fact]
    public async Task Enable_IsExplicitAndIdempotent_OnlyClearsFinalSelectionOnce()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.EnableAsync(f.ProjectId, f.User, f.Org, false, default));
        await f.Service.EnableAsync(f.ProjectId, f.User, f.Org, true, default);
        await using var db = f.Factory.CreateDbContext();
        var project = await db.Projects.SingleAsync(); var scene = await db.Scenes.SingleAsync();
        Assert.Equal(LocalVoicePolicies.VeoLocalVoiceConsistency, project.LocalVoicePolicyVersion);
        Assert.Null(scene.ApprovedRenderMediaAssetId);
        Assert.NotNull(scene.ApprovedGenerationId);
        scene.ApprovedRenderMediaAssetId = f.NativeId; await db.SaveChangesAsync();
        await f.Service.EnableAsync(f.ProjectId, f.User, f.Org, true, default);
        await db.Entry(scene).ReloadAsync();
        Assert.Equal(f.NativeId, scene.ApprovedRenderMediaAssetId);
    }

    [Fact]
    public async Task AuthorizationFailure_DoesNotChangePolicyOrCreateJob()
    {
        await using var f = await Fixture.CreateAsync();
        f.Access.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.EnableAsync(f.ProjectId, f.User, f.Org, true, default));
        await using var db = f.Factory.CreateDbContext();
        Assert.Null((await db.Projects.SingleAsync()).LocalVoicePolicyVersion);
        Assert.Empty(f.Store.List(f.ProjectId));
        Assert.Equal(0, f.Runtime.Calls);
    }

    [Fact]
    public async Task WrongOrganization_CannotReadOrMutateProject()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.GetAsync(f.ProjectId, f.User, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.EnableAsync(f.ProjectId, "another-user", f.Org, true, default));
    }

    [Theory]
    [InlineData("kling", "OpenAiStructuredPlan", "ProviderNativeVerified")]
    [InlineData("fal", "DirectShortVideo", "ProviderNativeVerified")]
    [InlineData("fal", "OpenAiStructuredPlan", "CanonicalVoice")]
    public async Task UnsupportedProject_IsRejected(string provider, string structure, string policy)
    {
        await using var f = await Fixture.CreateAsync();
        await using var db = f.Factory.CreateDbContext();
        var project = await db.Projects.SingleAsync(); project.VideoProviderCode = provider; project.SpeechProductionPolicy = policy;
        (await db.Scripts.SingleAsync()).StructureType = structure; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.EnableAsync(f.ProjectId, f.User, f.Org, true, default));
    }

    [Theory]
    [InlineData("dialogue")]
    [InlineData("character")]
    [InlineData("prompt")]
    [InlineData("trim")]
    [InlineData("model")]
    [InlineData("approval")]
    public async Task ChangedSnapshot_InvalidatesApprovedAnchorAndConversion(string change)
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        var anchor = await f.ReviewableAsync(true); await f.ApproveAsync(anchor);
        var job = await f.ReviewableAsync(false, f.Store.Read(f.ProjectId, anchor.Id)); await f.ApproveAsync(job);
        Assert.Equal(LocalVoiceStatuses.Approved, (await f.StateAsync()).Jobs.Single().Status);
        await using var db = f.Factory.CreateDbContext();
        switch (change)
        {
            case "dialogue": (await db.Scenes.SingleAsync()).Dialogue = "Lời mới"; break;
            case "character": (await db.Characters.SingleAsync()).ProfileJson = "{\"voice\":\"new\"}"; break;
            case "prompt": (await db.ScenePrompts.SingleAsync()).PromptHash = "changed"; break;
            case "trim": (await db.Scenes.SingleAsync()).HeadTrimMs = 100; break;
            case "model": (await db.Projects.SingleAsync()).VideoModelCode = "other-model"; break;
            case "approval": (await db.Scenes.SingleAsync()).UpdatedAtUtc = DateTime.UtcNow; break;
        }
        await db.SaveChangesAsync();
        var state = await f.StateAsync();
        Assert.Equal(LocalVoiceStatuses.Stale, state.Anchors.Single().Status);
        Assert.Equal(LocalVoiceStatuses.Stale, state.Jobs.Single().Status);
        Assert.Null(await f.Service.ResolveRenderAsync(db, await db.Projects.SingleAsync(), await db.Scenes.SingleAsync(), default));
    }

    [Fact]
    public async Task NewAnchor_InvalidatesOldConversionAndClearsRenderPointer()
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        var anchor = await f.ReviewableAsync(true); await f.ApproveAsync(anchor);
        var job = await f.ReviewableAsync(false, f.Store.Read(f.ProjectId, anchor.Id)); await f.ApproveAsync(job);
        var replacement = await f.ReviewableAsync(true); await f.ApproveAsync(replacement);
        Assert.Equal(LocalVoiceStatuses.Stale, (await f.StateAsync()).Jobs.Single().Status);
        await using var db = f.Factory.CreateDbContext(); Assert.Null((await db.Scenes.SingleAsync()).ApprovedRenderMediaAssetId);
        Assert.True(File.Exists(f.Store.Resolve(job.OutputRelativePath!)));
    }

    [Fact]
    public async Task NativeException_NeedsConfirmationAndReason_AndNeverCallsModel()
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.UseNativeAsync(f.ProjectId, f.User, f.Org, f.SceneId, false, "accepted", default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.UseNativeAsync(f.ProjectId, f.User, f.Org, f.SceneId, true, " ", default));
        await f.Service.UseNativeAsync(f.ProjectId, f.User, f.Org, f.SceneId, true, "Đã nghe và chấp nhận", default);
        await using var db = f.Factory.CreateDbContext();
        var resolved = await f.Service.ResolveRenderAsync(db, await db.Projects.SingleAsync(), await db.Scenes.SingleAsync(), default);
        Assert.Equal(f.NativeId, resolved?.MediaAssetId);
        Assert.True((await f.StateAsync()).Jobs.Single().NativeException);
        Assert.Equal(0, f.Runtime.Calls);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("anchor")]
    [InlineData("output")]
    public async Task TamperedMedia_CannotBeApprovedOrRendered(string target)
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        var anchor = await f.ReviewableAsync(true); await f.ApproveAsync(anchor);
        var job = await f.ReviewableAsync(false, f.Store.Read(f.ProjectId, anchor.Id));
        var path = target == "source" ? job.Source.RelativePath : target == "anchor" ? anchor.OutputRelativePath! : job.OutputRelativePath!;
        await File.WriteAllTextAsync(f.Store.Resolve(path), "changed bytes");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.ApproveAsync(job));
        Assert.Equal(LocalVoiceStatuses.ReviewRequired, f.Store.Read(f.ProjectId, job.Id).Status);
    }

    [Fact]
    public async Task MissingRuntime_AndMissingAnchor_BlockBeforeModelOrMediaProcessing()
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        f.Runtime.Status = "NOT_INSTALLED";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], true, null, default));
        f.Runtime.Status = "READY";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RunAsync(f.ProjectId, f.User, f.Org, [f.SceneId], false, null, default));
        Assert.Equal(0, f.Runtime.Calls); Assert.Empty(f.Store.List(f.ProjectId));
    }

    [Fact]
    public async Task InterruptedCheckpoint_IsVisibleAndCleanupPreservesSourceAnchorAndOutput()
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        var anchor = await f.ReviewableAsync(true); await f.ApproveAsync(anchor);
        var job = await f.ReviewableAsync(false, f.Store.Read(f.ProjectId, anchor.Id)); await f.ApproveAsync(job);
        var temporary = f.Store.JobRelative(f.ProjectId, job.Id) + "/speech.wav";
        await File.WriteAllTextAsync(f.Store.Resolve(temporary), "intermediate");
        var interrupted = await f.ReviewableAsync(true); interrupted.Status = LocalVoiceStatuses.ConvertingVoice; f.Store.Save(interrupted);
        Assert.Contains((await f.StateAsync()).Anchors, x => x.Id == interrupted.Id && x.Status == "Interrupted");
        Assert.Equal(1, await f.Service.CleanupAsync(f.ProjectId, f.User, f.Org, true, default));
        Assert.False(File.Exists(f.Store.Resolve(temporary)));
        Assert.True(File.Exists(f.Store.Resolve(anchor.OutputRelativePath!)));
        Assert.True(File.Exists(f.Store.Resolve(job.OutputRelativePath!)));
        Assert.True(File.Exists(f.Store.Resolve(job.Source.RelativePath)));
    }

    [Fact]
    public async Task Store_IsAtomicScopedAndExclusivelyLocked()
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        var record = await f.ReviewableAsync(true);
        Assert.Equal(record.Source.Fingerprint, f.Store.Read(f.ProjectId, record.Id).Source.Fingerprint);
        Assert.DoesNotContain("\"fingerprint\":", File.ReadAllText(f.Store.Resolve($"{f.Store.DirectoryRelative(f.ProjectId)}/{record.Id:N}.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => f.Store.Resolve("../escape.wav"));
        using var first = f.Store.AcquireProjectLock(f.ProjectId);
        Assert.Throws<IOException>(() => f.Store.AcquireProjectLock(f.ProjectId));
        Assert.Empty(Directory.EnumerateFiles(f.Root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Checkpoint_CannotRedirectOutputToAnotherProject()
    {
        await using var f = await Fixture.CreateAsync(); await f.EnableAsync();
        var record = await f.ReviewableAsync(true);
        record.OutputRelativePath = $"projects/{Guid.NewGuid():N}/private.wav";
        f.Store.Save(record);
        Assert.Throws<InvalidDataException>(() => f.Store.Read(f.ProjectId, record.Id));
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vm-local-voice-test-" + Guid.NewGuid().ToString("N"));
        public Guid ProjectId { get; } = Guid.NewGuid(); public Guid SceneId { get; } = Guid.NewGuid();
        public Guid Org { get; } = Guid.NewGuid(); public Guid NativeId { get; } = Guid.NewGuid(); public string User => "voice-test-user";
        public Factory Factory { get; } = new(); public FakeRuntime Runtime { get; } = new();
        public FakeAccess Access { get; private set; } = null!;
        public LocalVoiceStore Store { get; private set; } = null!;
        public LocalVoiceService Service { get; private set; } = null!;
        public async Task<LocalVoiceProjectSummary> StateAsync() => await Service.GetAsync(ProjectId, User, Org, default);
        public Task EnableAsync() => Service.EnableAsync(ProjectId, User, Org, true, default);
        public Task ApproveAsync(LocalVoiceRecord record) => Service.ReviewAsync(ProjectId, User, Org, record.Id, true, true, null, default);
        public async Task<LocalVoiceRecord> ReviewableAsync(bool anchor, LocalVoiceRecord? reference = null)
        {
            await using var db = Factory.CreateDbContext();
            var record = new LocalVoiceRecord { IsAnchor = anchor, Source = await LocalVoiceService.SourceAsync(db, await db.Projects.SingleAsync(), SceneId, default),
                Status = LocalVoiceStatuses.ReviewRequired, RuntimeFingerprint = "runtime-v1", AnchorId = reference?.Id, AnchorFingerprint = reference?.Fingerprint };
            record.OutputRelativePath = Store.JobRelative(ProjectId, record.Id) + (anchor ? "/anchor.wav" : "/converted.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(Store.Resolve(record.OutputRelativePath))!);
            await File.WriteAllTextAsync(Store.Resolve(record.OutputRelativePath), "fixture-result-" + record.Id);
            record.OutputSha256 = await LocalVoiceStore.FileHashAsync(Store.Resolve(record.OutputRelativePath), default);
            Store.Save(record); return record;
        }
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); var workspace = new ProjectWorkspaceService(f.Root); var relative = workspace.Create(f.ProjectId);
            f.Store = new(workspace); f.Access = new(f.Org);
            var runner = new ExternalProcessRunner(); var probe = new FfprobeService("not-used", runner);
            f.Service = new(f.Factory, f.Store, f.Runtime, new LocalVoiceMedia("not-used", runner, probe, new("not-used", runner, probe)), f.Access);
            var source = workspace.Resolve(relative + "/scenes/native.mp4"); await File.WriteAllTextAsync(source, "native fixture");
            var charId = Guid.NewGuid(); var scriptId = Guid.NewGuid(); var generationId = Guid.NewGuid(); var promptId = Guid.NewGuid();
            await using var db = f.Factory.CreateDbContext();
            db.Projects.Add(new Project { ProjectId = f.ProjectId, OrganizationId = f.Org, RemoteUserId = f.User, Name = "Voice", Topic = "Test", LanguageCode = "vi-VN",
                Platform = "YouTube", AspectRatio = "16:9", TargetDurationSeconds = 4, OutputWidth = 320, OutputHeight = 180, OutputFrameRate = 25,
                Status = "ReadyToRender", CurrentScriptVersion = 1, CurrentCharacterVersion = 1, CurrentScenePlanVersion = 1, CurrencyCode = "USD",
                VideoProviderCode = "fal", VideoModelCode = "veo-3.1", WorkspaceRelativePath = relative, RowVersion = new byte[8] });
            db.Scripts.Add(new Script { ScriptId = scriptId, ProjectId = f.ProjectId, Version = 1, StructureType = "OpenAiStructuredPlan", FullText = "Test", StoryBeatsJson = "[]", Status = "Approved", RowVersion = new byte[8] });
            db.Characters.Add(new Character { CharacterId = charId, CharacterKey = "speaker", ProjectId = f.ProjectId, Version = 1, Name = "Nhân vật", ProfileJson = "{}", Status = "Approved", RowVersion = new byte[8] });
            db.MediaAssets.Add(new MediaAsset { MediaAssetId = f.NativeId, ProjectId = f.ProjectId, SceneId = f.SceneId, AssetType = "SceneVideo", RelativePath = "scenes/native.mp4",
                MimeType = "video/mp4", Sha256 = await LocalVoiceStore.FileHashAsync(source, default), SizeBytes = new FileInfo(source).Length,
                DurationMs = 4000, Status = "Ready", SourceType = "Generated", MetadataJson = "{\"nativeAudioAudible\":true}", RowVersion = new byte[8] });
            db.ScenePrompts.Add(new ScenePrompt { ScenePromptId = promptId, SceneId = f.SceneId, Version = 1, PromptTemplateName = "veo", PromptTemplateVersion = "v1",
                CanonicalInputJson = "{}", FinalPrompt = "Test", PromptHash = "hash", Status = "Approved", RowVersion = new byte[8] });
            db.VideoGenerations.Add(new VideoGeneration { VideoGenerationId = generationId, SceneId = f.SceneId, ScenePromptId = promptId,
                ProviderRequestId = Guid.NewGuid(), AttemptNumber = 1, Status = "Approved", RequestedDurationMs = 4000, ActualDurationMs = 4000,
                OutputMediaAssetId = f.NativeId, RowVersion = new byte[8] });
            db.Scenes.Add(new Scene { SceneId = f.SceneId, ProjectId = f.ProjectId, ScriptId = scriptId, StyleProfileId = Guid.NewGuid(), ScenePlanVersion = 1,
                SequenceNumber = 1, StoryPurpose = "Test", VisualDescription = "Test", Dialogue = "Xin chào các bạn.", CharacterIdsJson = JsonSerializer.Serialize(new[] { charId }),
                ContentDurationMs = 4000, GenerationDurationMs = 4000, TimelineEndMs = 4000, EntryStateJson = "{}", ExitStateJson = "{}",
                Status = "Approved", ApprovedGenerationId = generationId, ApprovedRenderMediaAssetId = f.NativeId, RowVersion = new byte[8] });
            await db.SaveChangesAsync(); return f;
        }
        public ValueTask DisposeAsync()
        {
            Service.Dispose();
            var absolute = Path.GetFullPath(Root); var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            if (absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("vm-local-voice-test-")) Directory.Delete(absolute, true);
            return ValueTask.CompletedTask;
        }
    }
    internal sealed class Factory : IDbContextFactory<VideoFactoryDbContext>
    {
        private readonly DbContextOptions<VideoFactoryDbContext> _options = new DbContextOptionsBuilder<VideoFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public VideoFactoryDbContext CreateDbContext() => new(_options);
    }
    internal sealed class FakeAccess(Guid org) : ILocalVoiceAccessClient
    {
        public bool Denied { get; set; }
        public Task<LocalVoiceAccessResponse> AuthorizeLocalVoiceAsync(Guid project, CancellationToken token) => Denied
            ? throw new UnauthorizedAccessException() : Task.FromResult(new LocalVoiceAccessResponse(org, project));
    }
    internal sealed class FakeRuntime : ILocalVoiceRuntime
    {
        public string Status { get; set; } = "READY"; public int Calls { get; private set; }
        public LocalVoiceRuntimeSummary GetStatus() => new(Status, "test", Status == "READY" ? "runtime-v1" : null);
        public Task InstallAsync(CancellationToken token) => Task.CompletedTask;
        public Task RunAsync(string action, string work, Action<string>? progress, CancellationToken token) { Calls++; throw new InvalidOperationException("Not a real model."); }
    }
}
