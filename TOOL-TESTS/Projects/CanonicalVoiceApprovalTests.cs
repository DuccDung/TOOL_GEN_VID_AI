using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TOOL_LOCAL.Data;
using TOOL_LOCAL.Data.Models;
using TOOL_LOCAL.Projects;
using TOOL_LOCAL.Storage;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed partial class CanonicalVoiceApprovalTests
{
    [Fact]
    public async Task ApproveSceneVoice_NarrationApprovesWavBeforeVideo()
    {
        using var fixture = await CreateFixtureAsync(onCamera: false, includeNarratedVideo: false);

        var dashboard = await fixture.Service.GetDashboardAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);
        var reviewScene = Assert.Single(dashboard!.Scenes);
        Assert.True(reviewScene.HasCanonicalVoicePreview);
        Assert.NotNull(reviewScene.CanonicalVoicePreview?.Url);
        Assert.Equal(4_500, reviewScene.CanonicalVoicePreview?.DurationMs);

        await fixture.Service.ApproveSceneNativeAudioAsync(
            fixture.ProjectId,
            fixture.UserId,
            fixture.SceneId,
            playbackConfirmed: true,
            CancellationToken.None);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var scene = await dbContext.Scenes.SingleAsync();
        var voice = await dbContext.VoiceGenerations.SingleAsync();
        Assert.Equal("Approved", voice.Status);
        Assert.NotNull(voice.ApprovedAtUtc);
        Assert.Equal(voice.VoiceGenerationId, scene.ApprovedVoiceGenerationId);
        Assert.Equal(SceneSpeechStatuses.SpeechApproved, scene.SpeechStatus);
        Assert.Equal("PromptReady", scene.Status);
        Assert.Null(scene.ApprovedGenerationId);
        Assert.Null(scene.ApprovedRenderMediaAssetId);
        Assert.Empty(dbContext.VideoGenerations);
        Assert.Empty(dbContext.SpeechVerificationReports);
    }

    [Fact]
    public async Task LegacyNativeAudioInvalid_WithCurrentCanonicalWav_OpensVideoGenerationWithoutRegeneration()
    {
        using var fixture = await CreateFixtureAsync(
            onCamera: false,
            includeNarratedVideo: false,
            sceneStatus: "NativeAudioInvalid",
            speechStatus: SceneSpeechStatuses.SpeechInvalid,
            lastErrorCode: SpeechSynchronizationErrorCodes.SpeechVerificationFailed,
            lastErrorMessage: "Transcript không khớp lời nói đã khóa.");

        var dashboard = await fixture.Service.GetDashboardAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);
        var reviewScene = Assert.Single(dashboard!.Scenes);
        Assert.Equal("PromptReady", reviewScene.Status);
        Assert.False(reviewScene.RequiresAudioReview);
        Assert.False(reviewScene.CanApproveNativeAudio);
        Assert.True(reviewScene.CanGenerate);
        Assert.Null(reviewScene.LastErrorCode);
        Assert.Null(reviewScene.LastErrorMessage);
        Assert.True(reviewScene.HasCanonicalVoicePreview);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var voice = await dbContext.VoiceGenerations.SingleAsync();
        Assert.Equal("Completed", voice.Status);
        Assert.Single(dbContext.VoiceGenerations);
        Assert.Empty(dbContext.SpeechVerificationReports);
    }

    [Fact]
    public async Task ShortButTechnicallyValidCanonicalWav_ShowsPacingWarningAndStillAllowsVideo()
    {
        using var fixture = await CreateFixtureAsync(
            onCamera: false,
            includeNarratedVideo: false,
            voiceDurationMs: 3_000);

        var dashboard = await fixture.Service.GetDashboardAsync(
            fixture.ProjectId,
            fixture.UserId,
            CancellationToken.None);

        var scene = Assert.Single(dashboard!.Scenes);
        Assert.True(scene.CanGenerate);
        Assert.False(scene.RequiresAudioReview);
        Assert.Equal("PromptReady", scene.Status);
        Assert.NotNull(scene.SpeechPacing);
        Assert.Equal(3m, scene.SpeechPacing.ActualDurationSeconds);
        Assert.Equal("TooShort", scene.SpeechPacing.ActualStatus);
    }

    [Fact]
    public async Task ApproveSceneVoice_OnCameraOpensBackgroundVideoWithoutApprovingVideo()
    {
        using var fixture = await CreateFixtureAsync(onCamera: true, includeNarratedVideo: false);

        await fixture.Service.ApproveSceneNativeAudioAsync(
            fixture.ProjectId,
            fixture.UserId,
            fixture.SceneId,
            playbackConfirmed: true,
            CancellationToken.None);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var scene = await dbContext.Scenes.SingleAsync();
        Assert.Equal(SceneSpeechStatuses.SpeechApproved, scene.SpeechStatus);
        Assert.Equal("PromptReady", scene.Status);
        Assert.Null(scene.LastErrorCode);
        Assert.NotNull(scene.ApprovedVoiceGenerationId);
        Assert.Null(scene.ApprovedGenerationId);
        Assert.Null(scene.ApprovedRenderMediaAssetId);
        Assert.Empty(dbContext.VideoGenerations);
        Assert.Equal("ScenePlanning", (await dbContext.Projects.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApproveCanonicalScene_PointsRenderAtMatchingMixedAsset(bool onCamera)
    {
        using var fixture = await CreateFixtureAsync(onCamera, includeNarratedVideo: true);

        await fixture.Service.ApproveSceneNativeAudioAsync(
            fixture.ProjectId,
            fixture.UserId,
            fixture.SceneId,
            playbackConfirmed: true,
            CancellationToken.None);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var scene = await dbContext.Scenes.SingleAsync();
        var narratedAsset = await dbContext.MediaAssets.SingleAsync(x => x.AssetType == "SceneVideoNarrated");
        Assert.Equal("Approved", scene.Status);
        Assert.Equal(SceneSpeechStatuses.SpeechApproved, scene.SpeechStatus);
        Assert.Equal(narratedAsset.MediaAssetId, scene.ApprovedRenderMediaAssetId);
        Assert.NotNull(scene.ApprovedGenerationId);
        Assert.Equal("ReadyToRender", (await dbContext.Projects.SingleAsync()).Status);
    }

    [Fact]
    public async Task UpdateScene_WhenSpeechChanges_SupersedesCanonicalVoice()
    {
        using var fixture = await CreateFixtureAsync(onCamera: false, includeNarratedVideo: false);

        await fixture.Service.UpdateSceneAsync(
            fixture.ProjectId,
            fixture.UserId,
            new UpdateSceneCommand(
                fixture.SceneId,
                "Đây là lời dẫn đã thay đổi.",
                "Presenter",
                "Presenter",
                KlingSpeechModes.NativeVoiceOver),
            CancellationToken.None);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var scene = await dbContext.Scenes.SingleAsync();
        var voice = await dbContext.VoiceGenerations.SingleAsync();
        Assert.Equal("Superseded", voice.Status);
        Assert.Null(voice.ApprovedAtUtc);
        Assert.Null(scene.ApprovedVoiceGenerationId);
        Assert.Null(scene.ApprovedRenderMediaAssetId);
        Assert.Equal(SceneSpeechStatuses.SpeechMissing, scene.SpeechStatus);
        Assert.Equal("PromptReady", scene.Status);
    }

    [Fact]
    public async Task UnapproveSceneAudio_OnCameraClearsVoiceApproval()
    {
        using var fixture = await CreateFixtureAsync(onCamera: true, includeNarratedVideo: false);
        await fixture.Service.ApproveSceneNativeAudioAsync(
            fixture.ProjectId,
            fixture.UserId,
            fixture.SceneId,
            playbackConfirmed: true,
            CancellationToken.None);

        await fixture.Service.UnapproveSceneAudioAsync(
            fixture.ProjectId,
            fixture.UserId,
            fixture.SceneId,
            CancellationToken.None);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var scene = await dbContext.Scenes.SingleAsync();
        var voice = await dbContext.VoiceGenerations.SingleAsync();
        Assert.Null(scene.ApprovedVoiceGenerationId);
        Assert.Equal(SceneSpeechStatuses.SpeechReviewRequired, scene.SpeechStatus);
        Assert.Equal("AudioReviewRequired", scene.Status);
        Assert.Equal("Completed", voice.Status);
        Assert.Null(voice.ApprovedAtUtc);
        Assert.Equal("ScenePlanning", (await dbContext.Projects.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnapproveSceneAudio_NarratedSceneClearsAllRenderPointers()
    {
        using var fixture = await CreateFixtureAsync(onCamera: false, includeNarratedVideo: true);
        await fixture.Service.ApproveSceneNativeAudioAsync(
            fixture.ProjectId,
            fixture.UserId,
            fixture.SceneId,
            playbackConfirmed: true,
            CancellationToken.None);

        await fixture.Service.UnapproveSceneAudioAsync(
            fixture.ProjectId,
            fixture.UserId,
            fixture.SceneId,
            CancellationToken.None);

        await using var dbContext = fixture.Factory.CreateDbContext();
        var scene = await dbContext.Scenes.SingleAsync();
        Assert.Null(scene.ApprovedGenerationId);
        Assert.Null(scene.ApprovedVoiceGenerationId);
        Assert.Null(scene.ApprovedRenderMediaAssetId);
        Assert.Equal(SceneSpeechStatuses.SpeechReviewRequired, scene.SpeechStatus);
        Assert.Equal("AudioReviewRequired", scene.Status);
        Assert.Equal("Completed", (await dbContext.VideoGenerations.SingleAsync()).Status);
        var voice = await dbContext.VoiceGenerations.SingleAsync();
        Assert.Equal("Completed", voice.Status);
        Assert.Null(voice.ApprovedAtUtc);
    }

    private static async Task<Fixture> CreateFixtureAsync(
        bool onCamera,
        bool includeNarratedVideo,
        string sceneStatus = "AudioReviewRequired",
        string speechStatus = SceneSpeechStatuses.SpeechReviewRequired,
        string? lastErrorCode = null,
        string? lastErrorMessage = null,
        long voiceDurationMs = 4_500)
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"canonical-voice-approval-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new SqlRowVersionFixtureInterceptor())
            .Options;
        var factory = new TestDbContextFactory(options);
        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var scriptId = Guid.NewGuid();
        var styleId = Guid.NewGuid();
        var promptId = Guid.NewGuid();
        var voiceProfileId = Guid.NewGuid();
        var voiceProfileVersionId = Guid.NewGuid();
        var voiceGenerationId = Guid.NewGuid();
        var voiceAssetId = Guid.NewGuid();
        var voiceRequestId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        const string userId = "canonical-voice-user";
        const string spokenText = "Xin chào Việt Nam.";
        var speechHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(SpeechTextNormalization.Normalize(spokenText))))
            .ToLowerInvariant();
        var voiceSnapshotHash = new string('c', 64);

        await using (var dbContext = factory.CreateDbContext())
        {
            var project = new Project
            {
                ProjectId = projectId,
                OrganizationId = Guid.NewGuid(),
                CreatedByUserId = userId,
                RemoteUserId = userId,
                Name = "Canonical voice project",
                Topic = "Test",
                LanguageCode = "vi-VN",
                Platform = "YouTube",
                AspectRatio = "16:9",
                SpeechProductionPolicy = SpeechProductionPolicies.CanonicalVoice,
                TargetDurationSeconds = 5,
                OutputWidth = 1280,
                OutputHeight = 720,
                OutputFrameRate = 25,
                Status = "ScenePlanning",
                CurrentScriptVersion = 1,
                CurrentCharacterVersion = 1,
                CurrentStyleVersion = 1,
                CurrentScenePlanVersion = 1,
                CurrencyCode = "USD",
                WorkspaceRelativePath = $"projects/{projectId:N}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            };
            var script = new Script
            {
                ScriptId = scriptId,
                ProjectId = projectId,
                Version = 1,
                StructureType = "Test",
                FullText = spokenText,
                StoryBeatsJson = "[]",
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            };
            var style = new StyleProfile
            {
                StyleProfileId = styleId,
                ProjectId = projectId,
                Version = 1,
                Name = "Test",
                VisualStyleJson = "{}",
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            };
            var scene = new Scene
            {
                SceneId = sceneId,
                ProjectId = projectId,
                ScriptId = scriptId,
                StyleProfileId = styleId,
                ScenePlanVersion = 1,
                SequenceNumber = 1,
                StoryPurpose = "Canonical speech",
                Narration = onCamera ? null : spokenText,
                Dialogue = onCamera ? spokenText : null,
                CharacterIdsJson = onCamera ? JsonSerializer.Serialize(new[] { characterId }) : "[]",
                VisualDescription = "Presenter",
                ContentDurationMs = 5_000,
                GenerationDurationMs = 5_000,
                TimelineEndMs = 5_000,
                EntryStateJson = "{}",
                ExitStateJson = "{}",
                Status = sceneStatus,
                SpeechStatus = speechStatus,
                LastErrorCode = lastErrorCode,
                LastErrorMessage = lastErrorMessage,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            };
            var profile = new VoiceProfile
            {
                VoiceProfileId = voiceProfileId,
                ProjectId = projectId,
                Scope = onCamera ? VoiceProfileScopes.Character : VoiceProfileScopes.ProjectNarrator,
                CharacterId = onCamera ? characterId : null,
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            };
            var profileVersion = new VoiceProfileVersion
            {
                VoiceProfileVersionId = voiceProfileVersionId,
                VoiceProfileId = voiceProfileId,
                Version = 1,
                ProviderCode = "openai",
                ModelCode = "gpt-4o-mini-tts",
                VoiceCode = "female-sweet",
                ProviderVoiceCode = "coral",
                LanguageCode = "vi-VN",
                SpeakingRate = 1m,
                VoiceInstructions = "Rõ ràng",
                SnapshotHash = voiceSnapshotHash,
                Status = VoiceProfileVersionStatuses.Approved,
                CreatedAtUtc = now,
                ApprovedAtUtc = now,
                RowVersion = new byte[8]
            };
            if (onCamera)
            {
                dbContext.Characters.Add(new Character
                {
                    CharacterId = characterId,
                    ProjectId = projectId,
                    CharacterKey = "presenter",
                    Version = 1,
                    Name = "Người dẫn",
                    ProfileJson = "{}",
                    Status = "Approved",
                    ApprovedVoiceProfileVersionId = voiceProfileVersionId,
                    CreatedAtUtc = now,
                    ApprovedAtUtc = now,
                    RowVersion = new byte[8]
                });
            }
            else
            {
                project.ApprovedNarratorVoiceProfileVersionId = voiceProfileVersionId;
            }

            dbContext.AddRange(project, script, style, scene, profile, profileVersion);
            dbContext.ScenePrompts.Add(new ScenePrompt
            {
                ScenePromptId = promptId,
                SceneId = sceneId,
                Version = 1,
                PromptTemplateName = "Test",
                PromptTemplateVersion = "1",
                CanonicalInputJson = "{}",
                FinalPrompt = "Presenter",
                PromptHash = new string('a', 64),
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.ProviderRequests.Add(
                CreateProviderRequest(voiceRequestId, projectId, sceneId, "Voice", now));
            dbContext.MediaAssets.Add(new MediaAsset
            {
                MediaAssetId = voiceAssetId,
                ProjectId = projectId,
                SceneId = sceneId,
                AssetType = "SceneVoice",
                RelativePath = "voice/scene-001.wav",
                MimeType = "audio/wav",
                SizeBytes = 1_024,
                Sha256 = new string('b', 64),
                DurationMs = voiceDurationMs,
                AudioSampleRate = 24_000,
                Status = "Ready",
                SourceType = "Generated",
                CreatedAtUtc = now,
                VerifiedAtUtc = now,
                RowVersion = new byte[8]
            });
            dbContext.VoiceGenerations.Add(new VoiceGeneration
            {
                VoiceGenerationId = voiceGenerationId,
                ProjectId = projectId,
                ScriptId = scriptId,
                SceneId = sceneId,
                ScenePlanVersion = 1,
                ProviderRequestId = voiceRequestId,
                Version = 1,
                VoiceCode = "female-sweet",
                ProviderVoiceCode = "coral",
                NarrationHash = speechHash,
                VoiceSnapshotHash = voiceSnapshotHash,
                VoiceProfileVersionId = voiceProfileVersionId,
                VerificationStatus = SpeechVerificationStatuses.NotRequested,
                LanguageCode = "vi-VN",
                SpeakingRate = 1m,
                Status = includeNarratedVideo ? "Approved" : "Completed",
                DurationMs = voiceDurationMs,
                OutputMediaAssetId = voiceAssetId,
                CreatedAtUtc = now,
                CompletedAtUtc = now,
                ApprovedAtUtc = includeNarratedVideo ? now : null,
                RowVersion = new byte[8]
            });
            if (includeNarratedVideo)
            {
                scene.ApprovedVoiceGenerationId = voiceGenerationId;
                AddNarratedVideo(dbContext, projectId, sceneId, promptId, voiceGenerationId, speechHash, now);
            }
            await dbContext.SaveChangesAsync();
        }

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"videomaker-canonical-approval-{Guid.NewGuid():N}");
        var voicePath = Path.Combine(workspaceRoot, "projects", projectId.ToString("N"), "voice", "scene-001.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(voicePath)!);
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        return new Fixture(
            factory,
            new ProjectService(factory, new ProjectWorkspaceService(workspaceRoot)),
            projectId,
            sceneId,
            userId,
            workspaceRoot);
    }

    private static ProviderRequest CreateProviderRequest(
        Guid requestId,
        Guid projectId,
        Guid sceneId,
        string kind,
        DateTime now) =>
        new()
        {
            ProviderRequestId = requestId,
            ProjectId = projectId,
            SceneId = sceneId,
            RequestKind = kind,
            ProviderCode = "openai",
            ModelCode = kind == "Voice" ? "gpt-4o-mini-tts" : "whisper-1",
            IdempotencyKey = $"{kind}:{sceneId:N}",
            Status = "Completed",
            RequestJson = "{}",
            CurrencyCode = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };

    private static void AddNarratedVideo(
        VideoFactoryDbContext dbContext,
        Guid projectId,
        Guid sceneId,
        Guid promptId,
        Guid voiceGenerationId,
        string speechHash,
        DateTime now)
    {
        var videoRequestId = Guid.NewGuid();
        var rawAssetId = Guid.NewGuid();
        dbContext.ProviderRequests.Add(CreateProviderRequest(videoRequestId, projectId, sceneId, "Video", now));
        dbContext.MediaAssets.AddRange(
            new MediaAsset
            {
                MediaAssetId = rawAssetId,
                ProjectId = projectId,
                SceneId = sceneId,
                AssetType = "SceneVideo",
                RelativePath = "scenes/scene-001.mp4",
                MimeType = "video/mp4",
                SizeBytes = 1_024,
                Sha256 = new string('d', 64),
                DurationMs = 5_000,
                Status = "Ready",
                SourceType = "Generated",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            },
            new MediaAsset
            {
                MediaAssetId = Guid.NewGuid(),
                ProjectId = projectId,
                SceneId = sceneId,
                AssetType = "SceneVideoNarrated",
                RelativePath = "scenes/scene-001-narrated.mp4",
                MimeType = "video/mp4",
                SizeBytes = 2_048,
                Sha256 = new string('e', 64),
                DurationMs = 5_000,
                Status = "Ready",
                SourceType = "Generated",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    rawVideoMediaAssetId = rawAssetId,
                    voiceGenerationId,
                    voiceSnapshotHash = new string('c', 64),
                    speechHash,
                    mixStrategy = SpeechMixStrategies.ReplaceAllNativeAudio,
                    audioSyncPolicyVersion = "scene-audio-sync-v3",
                    canonicalVoiceAudible = true
                }),
                CreatedAtUtc = now.AddSeconds(1),
                VerifiedAtUtc = now.AddSeconds(1),
                RowVersion = new byte[8]
            });
        dbContext.VideoGenerations.Add(new VideoGeneration
        {
            VideoGenerationId = Guid.NewGuid(),
            SceneId = sceneId,
            ScenePromptId = promptId,
            ProviderRequestId = videoRequestId,
            AttemptNumber = 1,
            Status = "Approved",
            RequestedDurationMs = 5_000,
            ActualDurationMs = 5_000,
            OutputMediaAssetId = rawAssetId,
            CreatedAtUtc = now,
            CompletedAtUtc = now,
            RowVersion = new byte[8]
        });
    }

    private sealed record Fixture(
        TestDbContextFactory Factory,
        ProjectService Service,
        Guid ProjectId,
        Guid SceneId,
        string UserId,
        string WorkspaceRoot) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(WorkspaceRoot))
            {
                Directory.Delete(WorkspaceRoot, recursive: true);
            }
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<VideoFactoryDbContext> options)
        : IDbContextFactory<VideoFactoryDbContext>
    {
        public VideoFactoryDbContext CreateDbContext() => new(options);

        public Task<VideoFactoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
