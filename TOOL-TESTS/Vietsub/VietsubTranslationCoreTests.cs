using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationCoreTests
{
    [Theory]
    [InlineData(null, VietsubTranslationRunModes.Continue)]
    [InlineData("continue", VietsubTranslationRunModes.Continue)]
    [InlineData("retry_failed", VietsubTranslationRunModes.RetryFailed)]
    [InlineData("RESTART_UNLOCKED", VietsubTranslationRunModes.RestartUnlocked)]
    [InlineData("unexpected", VietsubTranslationRunModes.Continue)]
    public void RunMode_NormalizesToAllowlistedValue(string? input, string expected)
    {
        Assert.Equal(expected, VietsubTranslationRunModes.Normalize(input));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("ZH-CN", "zh")]
    [InlineData("zh-Hans", "zh")]
    public void Language_NormalizesSupportedSource(string input, string expected)
    {
        Assert.Equal(expected, VietsubTranslationLimits.NormalizeSourceLanguage(input));
        Assert.Equal("vi", VietsubTranslationLimits.NormalizeTargetLanguage("VI"));
    }

    [Fact]
    public void ResultValidator_RejectsMissingDuplicateAndReorderedCueIds()
    {
        const string first = "c001";
        const string second = "c002";
        var result = new VietsubTranslationSceneResult(
            "test",
            "1",
            [
                new VietsubTranslationItemResult(second, "Hai", 0.9, []),
                new VietsubTranslationItemResult(second, "Hai lần nữa", 0.9, [])
            ]);

        var exception = Assert.Throws<VietsubTranslationException>(() =>
            VietsubTranslationResultValidator.EnsureValid(result, [first, second]));

        Assert.Equal(VietsubTranslationErrorCodes.ResultInvalid, exception.Code);
    }

    [Fact]
    public void Limits_RespectBothProductAndProviderCaps()
    {
        var capabilities = new VietsubLocalTranslationCapabilities(
            "test", "1", ["en"], "vi", true, false, 5, 2, 2_500, 3_500);

        Assert.Equal(5, VietsubTranslationLimits.ResolveMaximumTargetCues(12, capabilities));
        Assert.Equal(2, VietsubTranslationLimits.ResolveContextCueCount(4, capabilities));
        Assert.Equal(2_500, VietsubTranslationLimits.ResolveMaximumSourceCharacters(6_000, capabilities));
        Assert.Equal(3_500, VietsubTranslationLimits.ResolveMaximumOutputCharacters(8_000, capabilities));
    }

    [Fact]
    public void Language_RejectsUnsupportedPair()
    {
        var sourceError = Assert.Throws<VietsubTranslationException>(() =>
            VietsubTranslationLimits.NormalizeSourceLanguage("ja"));
        var targetError = Assert.Throws<VietsubTranslationException>(() =>
            VietsubTranslationLimits.NormalizeTargetLanguage("en"));

        Assert.Equal(VietsubTranslationErrorCodes.LanguageUnsupported, sourceError.Code);
        Assert.Equal(VietsubTranslationErrorCodes.LanguageUnsupported, targetError.Code);
    }

    [Fact]
    public void SceneContract_SerializesStableCueAliasForStructuredProviderOutput()
    {
        var cueId = Guid.NewGuid();
        var request = new VietsubTranslationSceneRequest(
            "Project", "en", "vi", "", "", "", [], [],
            [new VietsubTranslationCueInput("c001", cueId, 0, 0, 1_000, "speaker", "Hello", true, 18)],
            VietsubTranslationPass.Translate, "", "fingerprint");

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"cueAlias\":\"c001\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([cueId], request.TargetCueIds);
    }

    [Fact]
    public void ScenePlanner_PreservesTargetsOnceAndDoesNotCrossChapterGap()
    {
        var cues = Enumerable.Range(0, 10)
            .Select(index => Cue(
                index < 6 ? index * 1_000 : 20_000 + (index - 6) * 1_000,
                $"Sentence {index}",
                index % 2 == 0 ? "alice" : "bob"))
            .ToArray();
        cues[0].TranslatedText = "Bản dịch đã duyệt";
        cues[0].TranslationLocked = true;
        cues[7].TranslatedText = "Bản nháp chưa duyệt";
        var targets = new[] { cues[1], cues[2], cues[3], cues[7], cues[8] }
            .Select(cue => cue.CueId)
            .ToHashSet();

        var plans = VietsubTranslationScenePlanner.Plan(
            cues,
            targets,
            maximumTargetCues: 2,
            contextCueCount: 2,
            sceneGapMilliseconds: 5_000,
            maximumCharactersPerSecond: 18);

        var plannedTargets = plans.SelectMany(plan => plan.TargetCueIds).ToArray();
        Assert.Equal(targets.Count, plannedTargets.Length);
        Assert.Equal(targets, plannedTargets.ToHashSet());
        Assert.All(plans, plan => Assert.InRange(plan.TargetCueIds.Count, 1, 2));
        Assert.DoesNotContain(plans, plan =>
            plan.Cues.Any(cue => cue.StartMilliseconds < 10_000)
            && plan.Cues.Any(cue => cue.StartMilliseconds >= 20_000));
        Assert.Contains(plans.SelectMany(plan => plan.Cues), cue =>
            !cue.IsTarget && cue.ApprovedVietnameseContext == "Bản dịch đã duyệt");
        Assert.DoesNotContain(plans.SelectMany(plan => plan.Cues), cue =>
            cue.ApprovedVietnameseContext == "Bản nháp chưa duyệt");
        Assert.All(plans, plan => Assert.Equal(
            plan.Cues.Count,
            plan.Cues.Select(cue => cue.CueAlias).Distinct(StringComparer.Ordinal).Count()));
    }

    [Fact]
    public void ScenePlanner_SortsTimelineButKeepsOriginalCueIndex()
    {
        var later = Cue(2_000, "Later", "speaker_2");
        var earlier = Cue(0, "Earlier", "speaker_1");

        var scene = Assert.Single(VietsubTranslationScenePlanner.Plan(
            [later, earlier],
            new HashSet<Guid> { later.CueId, earlier.CueId },
            maximumTargetCues: 2,
            contextCueCount: 0,
            sceneGapMilliseconds: 5_000,
            maximumCharactersPerSecond: 18));

        Assert.Collection(
            scene.Cues,
            cue =>
            {
                Assert.Equal(earlier.CueId, cue.CueId);
                Assert.Equal(1, cue.CueIndex);
            },
            cue =>
            {
                Assert.Equal(later.CueId, cue.CueId);
                Assert.Equal(0, cue.CueIndex);
            });
    }

    [Fact]
    public void ChapterContext_IncludesOnlyApprovedVietnameseContinuity()
    {
        var cues = Enumerable.Range(0, 5)
            .Select(index => Cue(index * 1_000, $"Source {index}", index % 2 == 0 ? "alice" : "bob"))
            .ToArray();
        cues[0].TranslatedText = "Đã duyệt";
        cues[0].TranslationLocked = true;
        cues[0].TranslationSource = VietsubTranslationSources.Manual;
        cues[1].TranslatedText = "Chưa duyệt";
        var scene = Assert.Single(VietsubTranslationScenePlanner.Plan(
            cues,
            new HashSet<Guid> { cues[4].CueId },
            maximumTargetCues: 2,
            contextCueCount: 1,
            sceneGapMilliseconds: 5_000,
            maximumCharactersPerSecond: 18));

        var context = VietsubTranslationScenePlanner.BuildChapterContext(scene, cues);

        Assert.Contains("Recent approved Vietnamese continuity", context);
        Assert.Contains("Đã duyệt", context);
        Assert.DoesNotContain("Chưa duyệt", context);
    }

    [Fact]
    public void Fingerprint_IsStableAndChangesWithRelevantContext()
    {
        var cues = new[]
        {
            Cue(0, "Hello", "alice"),
            Cue(1_000, "How are you?", "bob"),
            Cue(2_000, "Fine", "alice")
        };
        var glossaryId = Guid.NewGuid();
        var configuration = new VietsubTranslationConfigurationSnapshot(
            "en",
            "vi",
            "engine",
            "1",
            18,
            "Summary",
            "Alice xưng tôi",
            "Tự nhiên",
            [new VietsubTranslationGlossaryEntry(glossaryId, "Hello", "Xin chào")],
            "memory-v1");
        var configurationFingerprint = VietsubTranslationFingerprintBuilder
            .BuildConfigurationFingerprint(configuration);

        var first = VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
            cues[1], 1, cues, 1, configurationFingerprint);
        var same = VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
            cues[1], 1, cues, 1, configurationFingerprint);
        cues[1].TranslatedText = "Bản dịch của chính target";
        cues[1].QualityStatus = VietsubTranslationQualityStatuses.Valid;
        var ignoresTargetTranslation = VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
            cues[1], 1, cues, 1, configurationFingerprint);
        cues[0].TranslatedText = "Bản nháp không được duyệt";
        var ignoresUnapprovedDraft = VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
            cues[1], 1, cues, 1, configurationFingerprint);
        cues[0].TranslationLocked = true;
        var changed = VietsubTranslationFingerprintBuilder.BuildCueFingerprint(
            cues[1], 1, cues, 1, configurationFingerprint);

        Assert.Equal(first, same);
        Assert.Equal(first, ignoresTargetTranslation);
        Assert.Equal(first, ignoresUnapprovedDraft);
        Assert.NotEqual(first, changed);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void ConfigurationFingerprint_IsIndependentOfGlossaryInputOrder()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new VietsubTranslationGlossaryEntry(firstId, "Master", "Sư phụ");
        var second = new VietsubTranslationGlossaryEntry(secondId, "Clan", "Tông môn");
        var left = Configuration([first, second]);
        var right = Configuration([second, first]);

        Assert.Equal(
            VietsubTranslationFingerprintBuilder.BuildConfigurationFingerprint(left),
            VietsubTranslationFingerprintBuilder.BuildConfigurationFingerprint(right));
    }

    [Fact]
    public void ConfigurationFingerprint_separates_standard_and_low_memory_profiles()
    {
        var configuration = Configuration([]);
        var standard = configuration with
        {
            RuntimeProfileId = VietsubTranslationWorkerProfiles.StandardProfileId
        };
        var lowMemory = configuration with
        {
            RuntimeProfileId = VietsubTranslationWorkerProfiles.LowMemoryProfileId
        };

        Assert.NotEqual(
            VietsubTranslationFingerprintBuilder.BuildConfigurationFingerprint(standard),
            VietsubTranslationFingerprintBuilder.BuildConfigurationFingerprint(lowMemory));
    }

    [Fact]
    public void JobParameters_preserve_profile_and_upgrade_legacy_jobs_to_standard()
    {
        var settings = new VietsubTranslationSettingsSnapshot(
            "en",
            "vi",
            VietsubTranslationEnginePolicies.ContextualRequired,
            3,
            12,
            8_000,
            18,
            string.Empty,
            string.Empty,
            string.Empty,
            []);
        var lowMemory = new VietsubTranslationJobParameters(
            3,
            VietsubTranslationRunModes.Continue,
            Guid.NewGuid(),
            1,
            "engine",
            "1",
            new string('a', 64),
            settings,
            VietsubTranslationWorkerProfiles.LowMemoryProfileId,
            ResourceWarningAccepted: true);
        var legacy = lowMemory with
        {
            StrategyVersion = 1,
            RuntimeProfileId = null
        };

        Assert.Equal(
            VietsubTranslationWorkerProfiles.LowMemoryProfileId,
            VietsubTranslationJobParameters.Parse(lowMemory.ToJson()).RuntimeProfileId);
        Assert.True(VietsubTranslationJobParameters.Parse(lowMemory.ToJson()).ResourceWarningAccepted);
        Assert.Equal(
            VietsubTranslationWorkerProfiles.StandardProfileId,
            VietsubTranslationJobParameters.Parse(legacy.ToJson()).RuntimeProfileId);
        Assert.False(VietsubTranslationJobParameters.Parse(legacy.ToJson()).ResourceWarningAccepted);
        var versionTwo = lowMemory with { StrategyVersion = 2 };
        Assert.False(VietsubTranslationJobParameters.Parse(versionTwo.ToJson()).ResourceWarningAccepted);
        var invalid = lowMemory with { RuntimeProfileId = "profile-from-dom" };
        var exception = Assert.Throws<VietsubTranslationException>(() =>
            VietsubTranslationJobParameters.Parse(invalid.ToJson()));
        Assert.Equal(VietsubTranslationErrorCodes.JobNotResumable, exception.Code);
    }

    [Fact]
    public void Quality_AssignsWarningsWithoutRejectingUsableTranslation()
    {
        var assessment = VietsubTranslationQualityValidator.Assess(
            "Master paid 100 dollars",
            "Sếp đã trả 200 đô la và câu này dài hơn nhiều",
            durationMilliseconds: 500,
            glossary: [new VietsubTranslationGlossaryEntry(Guid.NewGuid(), "Master", "Sư phụ")],
            maximumCharactersPerSecond: 12,
            providerConfidence: 0.5,
            sourceLanguageCode: "en",
            suggestedMaximumCharacters: 10);

        Assert.True(assessment.IsValid);
        Assert.Contains("LOW_CONFIDENCE", assessment.Warnings);
        Assert.Contains("NUMBER_MISMATCH", assessment.Warnings);
        Assert.Contains("GLOSSARY_MISSING:Master", assessment.Warnings);
        Assert.Contains("READING_SPEED_HIGH", assessment.Warnings);
        Assert.Contains("SUGGESTED_LENGTH_EXCEEDED", assessment.Warnings);
    }

    [Theory]
    [InlineData("", "EMPTY_TRANSLATION")]
    [InlineData("lặp lặp lặp lặp", "REPEATED_TOKEN_RUN")]
    public void Quality_RejectsPathologicalOutput(string translation, string expectedCode)
    {
        var assessment = VietsubTranslationQualityValidator.Assess(
            "Normal source",
            translation,
            2_000,
            sourceLanguageCode: "en");

        Assert.False(assessment.IsValid);
        Assert.Equal(expectedCode, assessment.FailureCode);
    }

    [Fact]
    public void Quality_GlossaryMatchUsesTermBoundary()
    {
        var assessment = VietsubTranslationQualityValidator.Assess(
            "The Master is here",
            "Một chủng tộc đang ở đây",
            2_000,
            [new VietsubTranslationGlossaryEntry(Guid.NewGuid(), "Master", "chủ")],
            sourceLanguageCode: "en");

        Assert.Contains("GLOSSARY_MISSING:Master", assessment.Warnings);
    }

    private static VietsubTranslationConfigurationSnapshot Configuration(
        IReadOnlyList<VietsubTranslationGlossaryEntry> glossary) =>
        new(
            "en",
            "vi",
            "engine",
            "1",
            18,
            "Summary",
            "Characters",
            "Style",
            glossary,
            "memory-v1");

    private static VietsubSubtitleCue Cue(long startMilliseconds, string text, string speaker) => new()
    {
        StartMilliseconds = startMilliseconds,
        EndMilliseconds = startMilliseconds + 800,
        OriginalText = text,
        Speaker = speaker
    };
}
