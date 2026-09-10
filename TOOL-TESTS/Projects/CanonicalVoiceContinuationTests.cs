using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed partial class CanonicalVoiceApprovalTests
{
    // InMemory has no SQL Server rowversion generator. Keep required-property validation enabled
    // and supply only the store-generated concurrency value when the fixture inserts a new row.
    private sealed class SqlRowVersionFixtureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries().Where(x => x.State == EntityState.Added))
                foreach (var property in entry.Properties.Where(x => x.Metadata.Name == "RowVersion" &&
                    x.Metadata.IsConcurrencyToken && x.CurrentValue is null))
                    property.CurrentValue = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("approved", "SpeechApproved", "PromptReady")]
    [InlineData("unapproved", "SpeechReviewRequired", "AudioReviewRequired")]
    [InlineData("changed", "SpeechMissing", "PromptReady")]
    public async Task LegacyWaitingScene_UsesCurrentVoiceEvidenceWithoutWritingOnRead(
        string evidence, string expectedSpeech, string expectedStatus)
    {
        using var f = await CreateFixtureAsync(true, false,
            speechStatus: SceneSpeechStatuses.LegacySpeechReadyForLipSync);
        await using (var db = f.Factory.CreateDbContext())
        {
            var scene = await db.Scenes.SingleAsync();
            var voice = await db.VoiceGenerations.SingleAsync();
            if (evidence != "unapproved")
            {
                voice.Status = "Approved";
                voice.ApprovedAtUtc = DateTime.UtcNow;
                scene.ApprovedVoiceGenerationId = voice.VoiceGenerationId;
            }
            if (evidence == "changed") scene.Dialogue = "Lời thoại đã đổi.";
            await db.SaveChangesAsync();
        }
        var dashboard = await f.Service.GetDashboardAsync(f.ProjectId, f.UserId, default);
        var item = Assert.Single(dashboard!.Scenes);
        Assert.Equal(expectedSpeech, item.SpeechStatus);
        Assert.Equal(expectedStatus, item.Status);
        Assert.Equal(evidence == "unapproved", item.RequiresAudioReview);
        if (evidence == "unapproved") Assert.True(item.CanApproveNativeAudio);
        await using var verify = f.Factory.CreateDbContext();
        Assert.Equal(SceneSpeechStatuses.LegacySpeechReadyForLipSync, (await verify.Scenes.SingleAsync()).SpeechStatus);
        Assert.Equal(evidence == "unapproved" ? "Completed" : "Approved", (await verify.VoiceGenerations.SingleAsync()).Status);
        Assert.Empty(verify.VideoGenerations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalWav_ContinuesThroughDownloadMixReviewAndLocalRetry(bool onCamera)
    {
        using var f = await CreateFixtureAsync(onCamera, true,
            speechStatus: onCamera ? SceneSpeechStatuses.LegacySpeechReadyForLipSync : SceneSpeechStatuses.SpeechApproved);
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        var ffprobe = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffprobe.exe");
        Assert.True(File.Exists(ffmpeg) && File.Exists(ffprobe), "Licensed FFmpeg bundle is required.");
        var runner = new ExternalProcessRunner();
        var nativePath = Path.Combine(f.WorkspaceRoot, "cached-provider.mp4");
        var workspace = new ProjectWorkspaceService(f.WorkspaceRoot);
        var voicePath = workspace.Resolve($"projects/{f.ProjectId:N}/voice/scene-001.wav");
        foreach (var args in new string[][] {
            ["-y", "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=25:d=5", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", nativePath],
            ["-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=24000:duration=4.5", "-c:a", "pcm_s16le", voicePath]
        })
        {
            var result = await runner.RunAsync(ffmpeg, args, TimeSpan.FromMinutes(1), default);
            Assert.True(result.ExitCode == 0, result.StandardError);
        }
        Guid requestId;
        await using (var db = f.Factory.CreateDbContext())
        {
            db.MediaAssets.RemoveRange(await db.MediaAssets.Where(x => x.AssetType == "SceneVideoNarrated").ToListAsync());
            var voice = await db.VoiceGenerations.SingleAsync();
            var voiceAsset = await db.MediaAssets.SingleAsync(x => x.MediaAssetId == voice.OutputMediaAssetId);
            voiceAsset.Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(voicePath))).ToLowerInvariant();
            (await db.VoiceProfileVersions.SingleAsync()).PreviewProviderRequestId = voice.ProviderRequestId;
            requestId = (await db.VideoGenerations.SingleAsync()).ProviderRequestId;
            if (onCamera)
            {
                // The cached provider request avoids a new submit; only reference metadata is needed here.
                var character = await db.Characters.SingleAsync();
                var referenceAsset = new MediaAsset { MediaAssetId = Guid.NewGuid(), ProjectId = f.ProjectId,
                    AssetType = "CharacterReference", RelativePath = "reference.png", MimeType = "image/png",
                    Sha256 = new string('f', 64), Status = "Ready", SourceType = "Generated", RowVersion = new byte[8] };
                db.MediaAssets.Add(referenceAsset);
                db.CharacterReferences.Add(new CharacterReference { CharacterReferenceId = Guid.NewGuid(),
                    CharacterId = character.CharacterId, MediaAssetId = referenceAsset.MediaAssetId,
                    ReferenceType = "Primary", IsPrimary = true, ApprovalStatus = "Approved", RowVersion = new byte[8] });
            }
            await db.SaveChangesAsync();
        }
        var client = DispatchProxy.Create<IGenerationClient, CachedVideoClient>();
        var capture = (CachedVideoClient)(object)client;
        capture.TaskResponse = new VideoTaskResponse(requestId, "kling", "kling-test", "cached", "Completed", 100,
            "/api/generation/videos/cached/output", null, null);
        capture.NativePath = nativePath;
        var probe = new FfprobeService(ffprobe, runner);
        var audio = new AudioQualityValidator(ffmpeg, runner, probe);
        var workflow = new ProjectGenerationService(f.Factory, workspace, client, probe,
            new MediaToolPreflightService(new MediaToolPaths(ffmpeg, ffprobe), runner, TimeProvider.System),
            audio, new SceneAudioMixer(ffmpeg, runner, probe, audio), new SceneVideoTrimmer(ffmpeg, runner));

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(1, await workflow.GenerateVideosAsync(f.ProjectId, f.UserId, [f.SceneId], null, default, resumeOnly: attempt > 0));
            await using var db = f.Factory.CreateDbContext();
            var scene = await db.Scenes.SingleAsync();
            Assert.Equal("AudioReviewRequired", scene.Status);
            Assert.Null(scene.ApprovedRenderMediaAssetId);
            var mixed = await db.MediaAssets.SingleAsync(x => x.AssetType == "SceneVideoNarrated");
            var path = workspace.Resolve($"projects/{f.ProjectId:N}/{mixed.RelativePath}");
            Assert.True((await probe.ProbeAsync(path)).HasAudio);
            await audio.RequireAudibleAsync(path, "Expected audible canonical WAV", default);
            Assert.Single(db.VoiceGenerations);
            Assert.Single(await db.ProviderRequests.Where(x => x.RequestKind == "Video").ToListAsync());
            var metadata = JsonNode.Parse(mixed.MetadataJson!)!.AsObject();
            Assert.Equal((await db.VideoGenerations.SingleAsync()).OutputMediaAssetId!.Value.ToString("D"),
                metadata["rawVideoMediaAssetId"]!.GetValue<string>());
            if (attempt == 1) await File.WriteAllTextAsync(path, "corrupted cached mix");
            if (attempt == 2)
            {
                metadata["rawVideoMediaAssetId"] = Guid.NewGuid().ToString("D");
                mixed.MetadataJson = metadata.ToJsonString();
                await db.SaveChangesAsync();
            }
        }
        await f.Service.ApproveSceneNativeAudioAsync(f.ProjectId, f.UserId, f.SceneId, true, default);
        await using var approved = f.Factory.CreateDbContext();
        Assert.Equal("Approved", (await approved.Scenes.SingleAsync()).Status);
        Assert.Equal(4, capture.StatusCalls);
        Assert.Equal(0, capture.UnexpectedCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResumeOnly_RejectsMissingOrFailedRequestBeforeSubmitting(bool failed)
    {
        using var f = await CreateFixtureAsync(false, true);
        await using (var db = f.Factory.CreateDbContext())
        {
            var scene = await db.Scenes.SingleAsync();
            scene.ApprovedGenerationId = null;
            scene.ApprovedRenderMediaAssetId = null;
            scene.CharacterIdsJson = "[]";
            (await db.Projects.SingleAsync()).SpeechProductionPolicy = SpeechProductionPolicies.ProviderNativeVerified;
            var requests = await db.ProviderRequests.Where(x => x.RequestKind == "Video").ToListAsync();
            if (failed) foreach (var request in requests) request.Status = "Failed";
            else db.ProviderRequests.RemoveRange(requests);
            await db.SaveChangesAsync();
        }
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        var ffprobe = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffprobe.exe");
        var runner = new ExternalProcessRunner();
        var probe = new FfprobeService(ffprobe, runner);
        var audio = new AudioQualityValidator(ffmpeg, runner, probe);
        var client = DispatchProxy.Create<IGenerationClient, CachedVideoClient>();
        var workflow = new ProjectGenerationService(f.Factory, new ProjectWorkspaceService(f.WorkspaceRoot), client, probe,
            new MediaToolPreflightService(new MediaToolPaths(ffmpeg, ffprobe), runner, TimeProvider.System),
            audio, new SceneAudioMixer(ffmpeg, runner, probe, audio), new SceneVideoTrimmer(ffmpeg, runner));
        var error = await Assert.ThrowsAsync<ArgumentException>(() => workflow.GenerateVideosAsync(
            f.ProjectId, f.UserId, [f.SceneId], null, default, resumeOnly: true));
        Assert.Contains("Không có tác vụ video phù hợp", error.Message);
        Assert.Equal(0, ((CachedVideoClient)(object)client).UnexpectedCalls);
    }

    public class CachedVideoClient : DispatchProxy
    {
        public VideoTaskResponse TaskResponse { get; set; } = null!;
        public string NativePath { get; set; } = null!;
        public int StatusCalls { get; private set; }
        public int UnexpectedCalls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IGenerationClient.GetVideoStatusAsync))
            {
                Assert.Equal(TaskResponse.ProviderRequestId, args![0]);
                StatusCalls++;
                return Task.FromResult(TaskResponse);
            }
            if (method?.Name == nameof(IGenerationClient.DownloadVideoAsync))
            {
                File.Copy(NativePath, (string)args![1]!, true);
                return Task.CompletedTask;
            }
            UnexpectedCalls++;
            throw new InvalidOperationException($"Unexpected gateway call: {method?.Name}");
        }
    }
}
