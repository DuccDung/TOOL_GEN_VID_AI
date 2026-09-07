using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-translation-executor-{Guid.NewGuid():N}");

    [Fact]
    public async Task ServiceAndExecutor_TranslateTrackAndPublishAtomicVietnameseSrt()
    {
        var provider = new FakeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(
            provider,
            [Cue(0, "Hello", "alice"), Cue(1_000, "Welcome home", "bob")]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);

        var queued = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            queued.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        Assert.Equal(fixture.Track.TrackId, completed.OutputTrackId);
        Assert.Equal(1, provider.CallCount);
        var translatedTrack = Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId));
        Assert.Equal(3, translatedTrack.Revision);
        Assert.All(translatedTrack.Cues, cue =>
        {
            Assert.StartsWith("Bản dịch: ", cue.TranslatedText, StringComparison.Ordinal);
            Assert.Equal(VietsubTranslationSources.LocalAuto, cue.TranslationSource);
            Assert.Equal(VietsubTranslationQualityStatuses.Valid, cue.QualityStatus);
            Assert.False(cue.TranslationLocked);
            Assert.Equal(provider.Capabilities.EngineId, cue.TranslationEngineId);
            Assert.Equal(provider.Capabilities.EngineVersion, cue.TranslationEngineVersion);
        });
        var artifact = Assert.Single(
            translatedTrack.Artifacts,
            item => item.ArtifactType == "SRT_TRANSLATED");
        var artifactPath = fixture.Paths.GetProjectPath(
            fixture.Project.ProjectId,
            artifact.WorkspaceRelativePath);
        Assert.True(File.Exists(artifactPath));
        Assert.False(File.Exists(artifactPath + ".partial"));
        var srt = await File.ReadAllTextAsync(artifactPath, Encoding.UTF8);
        Assert.Contains("Bản dịch: Hello", srt, StringComparison.Ordinal);
        Assert.Contains("Bản dịch: Welcome home", srt, StringComparison.Ordinal);
        var items = await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            queued.Id);
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(VietsubTranslationJobItemStatuses.Completed, item.Status));
        var persistedJob = await fixture.Jobs.GetAsync(fixture.Project.ProjectId, queued.Id);
        Assert.DoesNotContain("Hello", persistedJob!.ParametersJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executor_ContextEditedWhileProviderRuns_MarksTargetStaleWithoutOverwrite()
    {
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeTranslationProvider(async (request, _, cancellationToken) =>
        {
            await releaseProvider.Task.WaitAsync(cancellationToken);
            return FakeTranslationProvider.CreateResult(request);
        });
        var contextCue = Cue(0, "I will help you", "alice");
        contextCue.TranslatedText = "Tôi sẽ giúp bạn";
        contextCue.TranslationLocked = true;
        contextCue.TranslationSource = VietsubTranslationSources.Manual;
        contextCue.QualityStatus = VietsubTranslationQualityStatuses.ManualReviewed;
        var targetCue = Cue(1_000, "Thank you", "bob");
        await using var fixture = await CreateFixtureAsync(provider, [contextCue, targetCue]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(contextCueCount: 1),
            CancellationToken.None);

        var queued = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        await provider.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var subtitleService = new VietsubSubtitleService(fixture.Paths, fixture.Subtitles);
        await subtitleService.UpdateCueAsync(
            fixture.Project,
            contextCue.CueId,
            contextCue.OriginalText,
            "Ta sẽ giúp ngươi",
            contextCue.Speaker);
        releaseProvider.TrySetResult();

        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            queued.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        var track = Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId));
        Assert.Equal("Ta sẽ giúp ngươi", track.Cues.Single(cue => cue.CueId == contextCue.CueId).TranslatedText);
        Assert.Equal(string.Empty, track.Cues.Single(cue => cue.CueId == targetCue.CueId).TranslatedText);
        var item = Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            queued.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Stale, item.Status);
        Assert.Equal(VietsubTranslationErrorCodes.TrackChanged, item.ErrorCode);
    }

    [Fact]
    public async Task Executor_InvalidRestartResult_DoesNotReplaceExistingGoodTranslation()
    {
        var provider = new FakeTranslationProvider((request, _, _) => Task.FromResult(
            FakeTranslationProvider.CreateResult(request, _ => "lặp lặp lặp lặp")));
        var cue = Cue(0, "A useful sentence", "alice");
        cue.TranslatedText = "Bản dịch tốt đang có";
        cue.TranslationSource = VietsubTranslationSources.LocalAuto;
        cue.TranslationSourceFingerprint = Fingerprint("old-result");
        cue.TranslationEngineId = "old-engine";
        cue.TranslationEngineVersion = "1";
        cue.QualityStatus = VietsubTranslationQualityStatuses.Valid;
        await using var fixture = await CreateFixtureAsync(provider, [cue]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);

        var queued = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.RestartUnlocked,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            queued.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        var currentCue = Assert.Single(
            Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId)).Cues);
        Assert.Equal("Bản dịch tốt đang có", currentCue.TranslatedText);
        Assert.Equal("old-engine", currentCue.TranslationEngineId);
        var item = Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            queued.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Invalid, item.Status);
        Assert.Equal("REPEATED_TOKEN_RUN", item.ErrorCode);
    }

    [Fact]
    public async Task Executor_RestartUnlockedRegeneratesAutoCueButPreservesManualLockedCue()
    {
        var provider = new FakeTranslationProvider();
        var manualCue = Cue(0, "Manual source", "alice");
        manualCue.TranslatedText = "Bản dịch thủ công";
        manualCue.TranslationLocked = true;
        manualCue.TranslationSource = VietsubTranslationSources.Manual;
        manualCue.QualityStatus = VietsubTranslationQualityStatuses.ManualReviewed;
        var autoCue = Cue(4_000, "Automatic source", "bob");
        autoCue.TranslatedText = "Bản dịch tự động cũ";
        autoCue.TranslationSource = VietsubTranslationSources.LocalAuto;
        autoCue.TranslationSourceFingerprint = Fingerprint("old-auto");
        autoCue.TranslationEngineId = "old-engine";
        autoCue.TranslationEngineVersion = "1";
        autoCue.QualityStatus = VietsubTranslationQualityStatuses.Valid;
        await using var fixture = await CreateFixtureAsync(provider, [manualCue, autoCue]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);

        var queued = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.RestartUnlocked,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            queued.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        var requestedIds = Assert.Single(provider.RequestedTargetCueIds);
        Assert.Equal([autoCue.CueId], requestedIds);
        var track = Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId));
        var currentManual = track.Cues.Single(cue => cue.CueId == manualCue.CueId);
        Assert.Equal("Bản dịch thủ công", currentManual.TranslatedText);
        Assert.True(currentManual.TranslationLocked);
        Assert.Equal(VietsubTranslationSources.Manual, currentManual.TranslationSource);
        var currentAuto = track.Cues.Single(cue => cue.CueId == autoCue.CueId);
        Assert.Equal("Bản dịch: Automatic source", currentAuto.TranslatedText);
        Assert.Equal(provider.Capabilities.EngineId, currentAuto.TranslationEngineId);
        Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            queued.Id));
    }

    [Fact]
    public async Task Executor_ApprovedContextMemoryHit_BypassesProvider()
    {
        var provider = new FakeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(provider, [Cue(0, "Master", "alice")]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        var settingsInput = Settings();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            settingsInput,
            CancellationToken.None);
        var configurationFingerprint = VietsubTranslationFingerprintBuilder.BuildConfigurationFingerprint(
            new VietsubTranslationConfigurationSnapshot(
                "en",
                "vi",
                provider.Capabilities.EngineId,
                provider.Capabilities.EngineVersion,
                settingsInput.MaximumCharactersPerSecond,
                settingsInput.ContextSummary,
                settingsInput.CharacterInstructions,
                settingsInput.StyleInstructions,
                [],
                "project-memory-v1"));
        var cueFingerprint = VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
            fixture.Track.Cues[0],
            0,
            fixture.Track.Cues,
            settingsInput.ContextCueCount,
            configurationFingerprint);
        await fixture.Translations.SaveApprovedMemoryAsync(
            fixture.Project.ProjectId,
            "en",
            "vi",
            "Master",
            "Sư phụ",
            cueFingerprint,
            VietsubTranslationMemorySourceKinds.ManualApproved);

        var queued = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            queued.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        Assert.Equal(0, provider.CallCount);
        var cue = Assert.Single(
            Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId)).Cues);
        Assert.Equal("Sư phụ", cue.TranslatedText);
        var item = Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            queued.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Completed, item.Status);
    }

    [Fact]
    public async Task Executor_ValidatedCacheHit_BypassesSecondProviderCall()
    {
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeTranslationProvider(async (request, _, cancellationToken) =>
        {
            await releaseProvider.Task.WaitAsync(cancellationToken);
            return FakeTranslationProvider.CreateResult(request);
        });
        var sourceCue = Cue(0, "Cache me", "alice");
        await using var fixture = await CreateFixtureAsync(provider, [sourceCue]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);
        var first = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        await provider.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var subtitleService = new VietsubSubtitleService(fixture.Paths, fixture.Subtitles);
        await subtitleService.UpdateCueAsync(
            fixture.Project,
            sourceCue.CueId,
            sourceCue.OriginalText,
            "Bản sửa tạm",
            sourceCue.Speaker);
        releaseProvider.TrySetResult();
        Assert.Equal(
            VietsubJobStatusNames.Completed,
            (await WaitForTerminalAsync(fixture.Manager, fixture.Project.ProjectId, first.Id)).Status);

        await subtitleService.UpdateCueAsync(
            fixture.Project,
            sourceCue.CueId,
            sourceCue.OriginalText,
            string.Empty,
            sourceCue.Speaker);
        var currentTrack = Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId));
        var second = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                currentTrack.TrackId,
                currentTrack.Revision),
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            second.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        Assert.Equal(1, provider.CallCount);
        var translatedCue = Assert.Single(
            Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId)).Cues);
        Assert.Equal("Bản dịch: Cache me", translatedCue.TranslatedText);
        var item = Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            second.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Completed, item.Status);
    }

    [Fact]
    public async Task Service_RetryFailedTargetsFailedCueAndCompletesWithSameProviderSnapshot()
    {
        var provider = new FakeTranslationProvider((request, call, _) =>
        {
            if (call == 1)
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.ProcessFailed,
                    "Fixture process failed.",
                    retryable: true);
            }
            return Task.FromResult(FakeTranslationProvider.CreateResult(request));
        });
        await using var fixture = await CreateFixtureAsync(provider, [Cue(0, "Retry me", "alice")]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);
        var first = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        var failed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            first.Id);
        Assert.Equal(VietsubJobStatusNames.Failed, failed.Status);
        Assert.Equal(VietsubTranslationErrorCodes.ProcessFailed, failed.ErrorCode);

        var retry = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.RetryFailed,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            retry.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        Assert.Equal(2, provider.CallCount);
        var cue = Assert.Single(
            Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId)).Cues);
        Assert.Equal("Bản dịch: Retry me", cue.TranslatedText);
        var item = Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            retry.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Completed, item.Status);
    }

    [Fact]
    public async Task Executor_WorkerCrashPreservesCommittedSceneAndRetryTargetsOnlyFailedScene()
    {
        var provider = new FakeTranslationProvider((request, call, _) =>
        {
            if (call == 2)
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.ProcessCrashed,
                    "Fixture worker crashed.",
                    retryable: true);
            }
            return Task.FromResult(FakeTranslationProvider.CreateResult(request));
        });
        var committedCue = Cue(0, "Keep committed", "alice");
        var failedCue = Cue(20_000, "Retry after crash", "bob");
        await using var fixture = await CreateFixtureAsync(provider, [committedCue, failedCue]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);

        var first = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        var failed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            first.Id);

        Assert.Equal(VietsubJobStatusNames.Failed, failed.Status);
        Assert.Equal(VietsubTranslationErrorCodes.ProcessCrashed, failed.ErrorCode);
        var trackAfterCrash = Assert.Single(
            await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId));
        var committedTranslation = trackAfterCrash.Cues[0].TranslatedText;
        Assert.EndsWith("Keep committed", committedTranslation, StringComparison.Ordinal);
        Assert.Equal(string.Empty, trackAfterCrash.Cues[1].TranslatedText);
        var firstItems = await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            first.Id);
        Assert.Equal(VietsubTranslationJobItemStatuses.Completed, firstItems[0].Status);
        Assert.Equal(VietsubTranslationJobItemStatuses.Failed, firstItems[1].Status);
        Assert.Equal(VietsubTranslationErrorCodes.ProcessCrashed, firstItems[1].ErrorCode);

        var retry = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.RetryFailed,
                fixture.Track.TrackId,
                trackAfterCrash.Revision),
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            retry.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        Assert.Equal(3, provider.CallCount);
        Assert.Equal([committedCue.CueId], provider.RequestedTargetCueIds.ElementAt(0));
        Assert.Equal([failedCue.CueId], provider.RequestedTargetCueIds.ElementAt(1));
        Assert.Equal([failedCue.CueId], provider.RequestedTargetCueIds.ElementAt(2));
        var finalTrack = Assert.Single(
            await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId));
        Assert.Equal(committedTranslation, finalTrack.Cues[0].TranslatedText);
        Assert.EndsWith("Retry after crash", finalTrack.Cues[1].TranslatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executor_PauseResume_RetriesInterruptedSceneWithoutDuplicateCommit()
    {
        var provider = new FakeTranslationProvider(async (request, call, cancellationToken) =>
        {
            if (call == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return FakeTranslationProvider.CreateResult(request);
        });
        await using var fixture = await CreateFixtureAsync(provider, [Cue(0, "Resume me", "alice")]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);
        var queued = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        await provider.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var paused = await fixture.Manager.PauseAsync(fixture.Project.ProjectId, queued.Id);
        Assert.Equal(VietsubJobStatusNames.Paused, paused.Status);
        await fixture.Manager.ResumeAsync(fixture.Project.ProjectId, queued.Id);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            queued.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        Assert.Equal(2, completed.AttemptCount);
        Assert.Equal(2, provider.CallCount);
        var track = Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId));
        Assert.Equal(2, track.Revision);
        Assert.Equal("Bản dịch: Resume me", Assert.Single(track.Cues).TranslatedText);
        var item = Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            queued.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Completed, item.Status);
        Assert.Equal(1, item.AttemptCount);
    }

    [Fact]
    public async Task Executor_SourceEditedWhilePaused_ResumeMarksOriginalItemStaleWithoutCallingProviderAgain()
    {
        var provider = new FakeTranslationProvider(async (request, call, cancellationToken) =>
        {
            if (call == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return FakeTranslationProvider.CreateResult(request);
        });
        var originalCue = Cue(0, "Original source", "alice");
        await using var fixture = await CreateFixtureAsync(provider, [originalCue]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);
        var queued = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        await provider.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var paused = await fixture.Manager.PauseAsync(fixture.Project.ProjectId, queued.Id);
        Assert.Equal(VietsubJobStatusNames.Paused, paused.Status);

        var subtitleService = new VietsubSubtitleService(fixture.Paths, fixture.Subtitles);
        await subtitleService.UpdateCueAsync(
            fixture.Project,
            originalCue.CueId,
            "Source edited by user",
            string.Empty,
            originalCue.Speaker);
        await fixture.Manager.ResumeAsync(fixture.Project.ProjectId, queued.Id);
        var completed = await WaitForTerminalAsync(
            fixture.Manager,
            fixture.Project.ProjectId,
            queued.Id);

        Assert.Equal(VietsubJobStatusNames.Completed, completed.Status);
        Assert.Equal(1, provider.CallCount);
        var cue = Assert.Single(
            Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId)).Cues);
        Assert.Equal("Source edited by user", cue.OriginalText);
        Assert.Equal(string.Empty, cue.TranslatedText);
        var item = Assert.Single(await fixture.Translations.LoadJobItemsAsync(
            fixture.Project.ProjectId,
            queued.Id));
        Assert.Equal(VietsubTranslationJobItemStatuses.Stale, item.Status);
        Assert.Equal(VietsubTranslationErrorCodes.TrackChanged, item.ErrorCode);
    }

    [Fact]
    public async Task Service_AuthorizationFailureWinsBeforeRuntimeAndDoesNotCreateJob()
    {
        var providerRegistry = new VietsubTranslationProviderRegistry();
        await using var fixture = await CreateFixtureAsync(
            provider: null,
            [Cue(0, "Denied", "alice")],
            new FakeAuthorizer(new VietsubLocalJobAuthorizationException(
                VietsubLocalJobAuthorizationErrorCodes.AccessDenied,
                "denied")),
            providerRegistry);
        await using var session = fixture.CreateSession();
        await session.StartAsync();

        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            fixture.Service.StartAsync(
                session,
                "owner",
                fixture.Project.OrganizationId,
                new VietsubStartTranslationInput(
                    VietsubTranslationRunModes.Continue,
                    fixture.Track.TrackId,
                    fixture.Track.Revision),
                CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.AccessDenied, error.Code);
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
    }

    [Fact]
    public async Task Service_RejectsStaleTrackRevisionBeforeRuntimeResolution()
    {
        var registry = new VietsubTranslationProviderRegistry();
        await using var fixture = await CreateFixtureAsync(
            provider: null,
            [Cue(0, "Stale revision", "alice")],
            providerRegistry: registry);
        await using var session = fixture.CreateSession();
        await session.StartAsync();

        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            fixture.Service.StartAsync(
                session,
                "owner",
                fixture.Project.OrganizationId,
                new VietsubStartTranslationInput(
                    VietsubTranslationRunModes.Continue,
                    fixture.Track.TrackId,
                    fixture.Track.Revision + 1),
                CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.TrackChanged, error.Code);
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
    }

    [Fact]
    public async Task Service_RequiresAnActivePaddleOcrTrackBeforeRuntimeResolution()
    {
        await using var fixture = await CreateFixtureAsync(
            provider: null,
            [Cue(0, "Imported subtitle", "alice")],
            trackSource: "IMPORTED_SRT");
        await using var session = fixture.CreateSession();
        await session.StartAsync();

        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            fixture.Service.StartAsync(
                session,
                "owner",
                fixture.Project.OrganizationId,
                new VietsubStartTranslationInput(
                    VietsubTranslationRunModes.Continue,
                    fixture.Track.TrackId,
                    fixture.Track.Revision),
                CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.SourceTrackRequired, error.Code);
        Assert.Equal("Bạn cần quét OCR nhận dạng phụ đề trước khi dịch.", error.Message);
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
    }

    [Fact]
    public async Task Service_RequiresOcrCuesBeforeRuntimeResolution()
    {
        await using var fixture = await CreateFixtureAsync(provider: null, []);
        await using var session = fixture.CreateSession();
        await session.StartAsync();

        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            fixture.Service.StartAsync(
                session,
                "owner",
                fixture.Project.OrganizationId,
                new VietsubStartTranslationInput(
                    VietsubTranslationRunModes.Continue,
                    fixture.Track.TrackId,
                    fixture.Track.Revision),
                CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.SourceTrackRequired, error.Code);
        Assert.Equal("Bạn cần quét OCR nhận dạng phụ đề trước khi dịch.", error.Message);
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
    }

    [Fact]
    public async Task Service_ValidOcrTrackWithoutApprovedRuntime_FailsWithoutCreatingJob()
    {
        await using var fixture = await CreateFixtureAsync(
            provider: null,
            [Cue(0, "OCR subtitle", "alice")]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();

        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            fixture.Service.StartAsync(
                session,
                "owner",
                fixture.Project.OrganizationId,
                new VietsubStartTranslationInput(
                    VietsubTranslationRunModes.Continue,
                    fixture.Track.TrackId,
                    fixture.Track.Revision),
                CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.RuntimeNotInstalled, error.Code);
        Assert.Equal("Chưa có engine dịch local sẵn sàng.", error.Message);
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
    }

    [Fact]
    public async Task Service_RejectsSecondActiveLocalJobBeforeAnotherProviderCall()
    {
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeTranslationProvider(async (request, _, cancellationToken) =>
        {
            await releaseProvider.Task.WaitAsync(cancellationToken);
            return FakeTranslationProvider.CreateResult(request);
        });
        await using var fixture = await CreateFixtureAsync(provider, [Cue(0, "Only once", "alice")]);
        await using var session = fixture.CreateSession();
        await session.StartAsync();
        await fixture.Service.UpdateSettingsAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            Settings(),
            CancellationToken.None);
        var first = await fixture.Service.StartAsync(
            session,
            "owner",
            fixture.Project.OrganizationId,
            new VietsubStartTranslationInput(
                VietsubTranslationRunModes.Continue,
                fixture.Track.TrackId,
                fixture.Track.Revision),
            CancellationToken.None);
        await provider.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            fixture.Service.StartAsync(
                session,
                "owner",
                fixture.Project.OrganizationId,
                new VietsubStartTranslationInput(
                    VietsubTranslationRunModes.Continue,
                    fixture.Track.TrackId,
                    fixture.Track.Revision),
                CancellationToken.None));
        Assert.Equal(VietsubTranslationErrorCodes.JobConflict, error.Code);
        Assert.Equal(1, provider.CallCount);

        releaseProvider.TrySetResult();
        Assert.Equal(
            VietsubJobStatusNames.Completed,
            (await WaitForTerminalAsync(fixture.Manager, fixture.Project.ProjectId, first.Id)).Status);
    }

    private async Task<TranslationFixture> CreateFixtureAsync(
        FakeTranslationProvider? provider,
        IReadOnlyList<VietsubSubtitleCue> cues,
        IVietsubLocalJobAuthorizer? authorizer = null,
        VietsubTranslationProviderRegistry? providerRegistry = null,
        string trackSource = "PADDLE_OCR_LOCAL")
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Translation executor test");
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "English source",
            LanguageCode = "en",
            Source = trackSource,
            Cues = cues.ToList()
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        project.SourceLanguageCode = "en";
        project.TargetLanguageCode = "vi";
        project.Status = VietsubProjectStatuses.Ready;
        await projects.SaveAsync(project);

        var jobs = new VietsubJobStore(paths, subtitles);
        var translations = new VietsubTranslationStore(paths, subtitles);
        providerRegistry ??= new VietsubTranslationProviderRegistry(
            provider is null ? [] : [provider]);
        var executor = new VietsubTranslationJobExecutor(
            projects,
            subtitles,
            translations,
            providerRegistry,
            jobs,
            paths);
        var manager = new VietsubJobManager(
            jobs,
            new VietsubJobExecutorRegistry([executor]));
        var service = new VietsubTranslationService(
            authorizer ?? new FakeAuthorizer(),
            subtitles,
            providerRegistry,
            manager);
        return new TranslationFixture(
            paths,
            subtitles,
            projects,
            project,
            track,
            jobs,
            translations,
            manager,
            service);
    }

    private static VietsubTranslationSettingsInput Settings(int contextCueCount = 1) => new(
        "en",
        "vi",
        VietsubTranslationEnginePolicies.ContextualRequired,
        contextCueCount,
        12,
        8_000,
        18,
        "Một cuộc trò chuyện thử nghiệm.",
        "Alice xưng tôi, Bob xưng bạn.",
        "Tự nhiên, ngắn gọn.",
        []);

    private static VietsubSubtitleCue Cue(long startMilliseconds, string originalText, string speaker) => new()
    {
        StartMilliseconds = startMilliseconds,
        EndMilliseconds = startMilliseconds + 3_000,
        OriginalText = originalText,
        Speaker = speaker
    };

    private static string Fingerprint(string seed) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    private static async Task<VietsubJobSummary> WaitForTerminalAsync(
        VietsubJobManager manager,
        Guid projectId,
        Guid jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            var job = await manager.GetAsync(projectId, jobId, timeout.Token)
                ?? throw new Xunit.Sdk.XunitException("Translation job biến mất.");
            if (job.Status is VietsubJobStatusNames.Completed or VietsubJobStatusNames.Failed)
            {
                return job;
            }
            await Task.Delay(20, timeout.Token);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record TranslationFixture(
        VietsubAppPaths Paths,
        VietsubSubtitleStore Subtitles,
        VietsubProjectStore Projects,
        VietsubProjectManifest Project,
        VietsubSubtitleTrack Track,
        VietsubJobStore Jobs,
        VietsubTranslationStore Translations,
        VietsubJobManager Manager,
        VietsubTranslationService Service) : IAsyncDisposable
    {
        public VietsubProjectSession CreateSession() => new(Projects, Project, TimeSpan.FromMilliseconds(10));

        public ValueTask DisposeAsync() => Manager.DisposeAsync();
    }

    private sealed class FakeAuthorizer(Exception? error = null) : IVietsubLocalJobAuthorizer
    {
        public Task AuthorizeAsync(
            string userId,
            Guid organizationId,
            VietsubProjectManifest project,
            CancellationToken cancellationToken)
        {
            if (error is not null)
            {
                return Task.FromException(error);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTranslationProvider(
        Func<VietsubTranslationSceneRequest, int, CancellationToken, Task<VietsubTranslationSceneResult>>? handler = null)
        : IVietsubLocalTranslationProvider
    {
        private int _callCount;

        public VietsubLocalTranslationCapabilities Capabilities { get; } = new(
            "fixture-contextual",
            "1.0.0",
            ["en", "zh"],
            "vi",
            true,
            false,
            12,
            3,
            6_000,
            8_000);

        public int CallCount => Volatile.Read(ref _callCount);

        public TaskCompletionSource FirstCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<IReadOnlyList<Guid>> RequestedTargetCueIds { get; } = new();

        public async Task<VietsubTranslationSceneResult> TranslateAsync(
            VietsubTranslationSceneRequest request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            RequestedTargetCueIds.Enqueue(request.TargetCueIds);
            FirstCallStarted.TrySetResult();
            return handler is null
                ? CreateResult(request)
                : await handler(request, call, cancellationToken);
        }

        public static VietsubTranslationSceneResult CreateResult(
            VietsubTranslationSceneRequest request,
            Func<VietsubTranslationCueInput, string>? translate = null) => new(
                "fixture-contextual",
                "1.0.0",
                request.Cues
                    .Where(cue => cue.IsTarget)
                    .Select(cue => new VietsubTranslationItemResult(
                        cue.CueAlias,
                        translate?.Invoke(cue) ?? $"Bản dịch: {cue.OriginalText}",
                        0.95,
                        []))
                    .ToArray());
    }
}
