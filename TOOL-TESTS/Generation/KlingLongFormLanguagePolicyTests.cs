using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_TESTS.Generation;

public sealed class KlingLongFormLanguagePolicyTests
{
    [Theory]
    [InlineData(ProviderCodes.Kling, GenerationWorkflowTypes.OpenAiStructuredPlan, "en-US", "vi-VN")]
    [InlineData(ProviderCodes.Kling, GenerationWorkflowTypes.DirectShortVideo, "en-US", "en-US")]
    [InlineData(ProviderCodes.BytePlus, GenerationWorkflowTypes.OpenAiStructuredPlan, "en-US", "en-US")]
    public void Resolve_OnlyForcesVietnameseForKlingLongForm(
        string providerCode,
        string structureType,
        string projectLanguageCode,
        string expected)
    {
        Assert.Equal(
            expected,
            KlingLongFormLanguagePolicy.Resolve(providerCode, projectLanguageCode, structureType));
    }

    [Fact]
    public void Validator_DetectsVietnameseWithAndWithoutDiacriticsAndRejectsEnglishProse()
    {
        Assert.True(KlingVietnameseContentValidator.ContainsHighConfidenceVietnamese(
            "Một cô gái đang đi bộ trên phố cổ."));
        Assert.True(KlingVietnameseContentValidator.ContainsHighConfidenceVietnamese(
            "mot co gai dang di bo tren pho co"));
        Assert.True(KlingVietnameseContentValidator.ContainsHighConfidenceVietnamese("Đà Nẵng"));
        Assert.False(KlingVietnameseContentValidator.ContainsHighConfidenceVietnamese(
            "A woman walks through a quiet old town at sunrise."));
    }

    [Fact]
    public void PlanValidator_ReportsTheExactHumanReadableFields()
    {
        var plan = CreateVietnamesePlan() with
        {
            Title = "A Better Morning",
            Scenes =
            [
                CreateVietnamesePlan().Scenes[0] with
                {
                    VisualPrompt = "A woman walks through a quiet old town at sunrise."
                }
            ]
        };

        var violations = KlingVietnameseContentValidator.FindPlanViolations(plan);

        Assert.Contains(violations, x => x.Field == "title" && x.Reason == "language_invalid");
        Assert.Contains(violations, x =>
            x.Field == "scenes[0].visual_prompt" && x.Reason == "language_invalid");
    }

    [Fact]
    public void PlanValidator_AllowsProperAssetNamesButStillRequiresVietnameseDescriptions()
    {
        var plan = CreateVietnamesePlan() with
        {
            Assets =
            [
                CreateVietnamesePlan().Assets![0] with
                {
                    Name = "iPhone 17 Pro",
                    CanonicalDescription = "A black phone rests face down on the wooden desk."
                }
            ]
        };

        var violations = KlingVietnameseContentValidator.FindPlanViolations(plan);

        Assert.DoesNotContain(violations, x => x.Field == "assets[0].name");
        Assert.Contains(violations, x =>
            x.Field == "assets[0].canonical_description" && x.Reason == "language_invalid");
    }

    [Fact]
    public void FieldValidator_TreatsMaterializedAssetNamesAsProperNames()
    {
        var violations = KlingVietnameseContentValidator.FindViolations([
            ("project_asset.name", "Apple Watch", true),
            ("project_asset.canonical_description", "Đồng hồ màu đen có dây silicon cố định.", true)
        ]);

        Assert.Empty(violations);
    }

    [Fact]
    public void FieldValidator_DoesNotExemptLongEnglishProsePlacedInANameField()
    {
        var violations = KlingVietnameseContentValidator.FindViolations([
            ("assets[0].name", "A clean modern presentation room with one large window and soft morning sunlight", true)
        ]);

        var violation = Assert.Single(violations);
        Assert.Equal("assets[0].name", violation.Field);
        Assert.Equal("language_invalid", violation.Reason);
    }

    [Fact]
    public void FieldValidator_ClassifiesEmptyEnglishAndOptionalValues()
    {
        var violations = KlingVietnameseContentValidator.FindViolations([
            ("title", "  ", true),
            ("negative_prompt", "subtitles, watermark", true),
            ("scenes[0].spoken_text", string.Empty, false)
        ]);

        Assert.Contains(violations, x => x.Field == "title" && x.Reason == "required");
        Assert.Contains(violations, x =>
            x.Field == "negative_prompt" && x.Reason == "language_invalid");
        Assert.DoesNotContain(violations, x => x.Field == "scenes[0].spoken_text");
    }

    private static GeneratedContentPlan CreateVietnamesePlan() =>
        new(
            "Buổi sáng tốt hơn",
            "Bắt đầu bằng một thói quen nhỏ.",
            "Câu chuyện chuyển đổi thực tế.",
            "Người trưởng thành bận rộn",
            "Hãy thử thói quen này vào ngày mai.",
            "Người dẫn chương trình giải thích một thói quen buổi sáng đơn giản.",
            "Hiện thực điện ảnh ấm áp",
            "phụ đề, logo, watermark",
            [],
            [
                new GeneratedContentScene(
                    1,
                    "Giới thiệu bước thực tế đầu tiên.",
                    "Hãy bắt đầu bằng một cốc nước.",
                    "Người dẫn đứng cạnh cửa sổ nhà bếp sáng và nâng một cốc nước trong suốt.",
                    5,
                    [],
                    KlingSpeechModes.NativeVoiceOver,
                    null,
                    "ấm áp và tự tin",
                    "âm nền buổi sáng yên tĩnh",
                    "tiếng cốc di chuyển nhẹ",
                    ["bright-kitchen"])
            ],
            [
                new GeneratedProjectAsset(
                    "bright-kitchen",
                    ProjectAssetTypes.Background,
                    "Nhà bếp sáng",
                    "Nhà bếp màu trắng ấm với cửa sổ lớn cố định ở bên trái.",
                    [1])
            ]);
}
