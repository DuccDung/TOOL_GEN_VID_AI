namespace TOOL_LOCAL.AI.Contracts;

public sealed record ProjectBrief(
    string Topic,
    string LanguageCode,
    string Platform,
    string AspectRatio,
    int TargetDurationSeconds);

public sealed record TopicAnalysisContract(
    string ContentType,
    string PrimaryIntent,
    string TargetAudience,
    IReadOnlyCollection<string> Keywords,
    IReadOnlyCollection<string> EmotionalTriggers,
    IReadOnlyCollection<string> SafetyRisks,
    string RecommendedStoryStructure);

public sealed record HookCandidateContract(
    string Text,
    decimal Score,
    string Reason);

public sealed record VideoConceptContract(
    string Title,
    string ViralAngle,
    string Audience,
    IReadOnlyCollection<HookCandidateContract> Hooks,
    int SelectedHookIndex,
    string StoryStructure,
    string CallToAction,
    string ThumbnailConcept,
    decimal ViralScore);

public sealed record StoryBeatContract(
    string BeatType,
    string StoryPurpose,
    string Narration,
    string? Dialogue,
    string VisualIntent,
    string Emotion,
    int VisualComplexity = 1);

public sealed record ScriptContract(
    string Title,
    string Hook,
    IReadOnlyCollection<StoryBeatContract> Beats,
    string CallToAction,
    int EstimatedDurationSeconds);

public sealed record CharacterProfileContract(
    string CharacterKey,
    string Name,
    string Role,
    string Gender,
    int? Age,
    string Face,
    string Hair,
    string Eyes,
    string Skin,
    string Body,
    string Clothing,
    string Accessories,
    string Personality,
    string VisualIdentity,
    IReadOnlyCollection<string> ImmutableTraits);

public sealed record StyleProfileContract(
    string VisualStyle,
    string ColorStyle,
    string CameraStyle,
    string LightingStyle,
    string EnvironmentStyle,
    string RenderQuality,
    IReadOnlyCollection<string> GlobalNegativeTerms);

public sealed record ContinuityStateContract(
    string CharacterPose,
    string CharacterPosition,
    string LookDirection,
    string Clothing,
    IReadOnlyCollection<string> HeldProps,
    string Location,
    string TimeOfDay,
    string Lighting,
    string Emotion);

public sealed record PlannedSceneContract(
    int SequenceNumber,
    string SceneKey,
    string BeatType,
    string StoryPurpose,
    decimal TimeStartSeconds,
    decimal TimeEndSeconds,
    decimal ContentDurationSeconds,
    int GenerationDurationSeconds,
    string Narration,
    string? Dialogue,
    string VisualDescription,
    string Camera,
    string Motion,
    string Emotion,
    string Transition,
    ContinuityStateContract StartState,
    ContinuityStateContract EndState,
    string? PreviousSceneKey,
    string? NextSceneKey);

public sealed record ScenePlanContract(
    decimal TotalContentDurationSeconds,
    IReadOnlyCollection<PlannedSceneContract> Scenes);

public sealed record CanonicalVideoPromptContract(
    string PositivePrompt,
    string NegativePrompt,
    string ContinuityInstruction,
    IReadOnlyCollection<string> ReferenceImagePaths,
    int DurationSeconds,
    int Width,
    int Height,
    decimal FramesPerSecond);

public sealed record QualityIssueContract(
    string Code,
    string Severity,
    string Message,
    string? SuggestedFix);

public sealed record QualityReportContract(
    decimal Score,
    bool Approved,
    IReadOnlyCollection<QualityIssueContract> Issues);
