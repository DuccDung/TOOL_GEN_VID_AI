using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class Scene
{
    public Guid SceneId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ScriptId { get; set; }

    public Guid StyleProfileId { get; set; }

    public int ScenePlanVersion { get; set; }

    public int SequenceNumber { get; set; }

    public string? ContinuityGroupKey { get; set; }

    public string? StoryBeatId { get; set; }

    public string StoryPurpose { get; set; } = null!;

    public string? Narration { get; set; }

    public string? Dialogue { get; set; }

    public string VisualDescription { get; set; } = null!;

    public string? LocationKey { get; set; }

    public string? CameraDirection { get; set; }

    public string? Lighting { get; set; }

    public string? Motion { get; set; }

    public string? Emotion { get; set; }

    public string? TransitionAfter { get; set; }

    public long ContentDurationMs { get; set; }

    public long GenerationDurationMs { get; set; }

    public long TimelineStartMs { get; set; }

    public long TimelineEndMs { get; set; }

    public long HeadTrimMs { get; set; }

    public long TailTrimMs { get; set; }

    public long OverlapAfterMs { get; set; }

    public Guid? PreviousSceneId { get; set; }

    public Guid? NextSceneId { get; set; }

    public Guid? GenerationDependencySceneId { get; set; }

    public string? CharacterIdsJson { get; set; }

    public string EntryStateJson { get; set; } = null!;

    public string ExitStateJson { get; set; } = null!;

    public string? RequiredCapabilitiesJson { get; set; }

    public string Status { get; set; } = null!;

    public Guid? ApprovedGenerationId { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual VideoGeneration? ApprovedGeneration { get; set; }

    public virtual Scene? GenerationDependencyScene { get; set; }

    public virtual ICollection<Scene> InverseGenerationDependencyScene { get; set; } = new List<Scene>();

    public virtual ICollection<Scene> InverseNextScene { get; set; } = new List<Scene>();

    public virtual ICollection<Scene> InversePreviousScene { get; set; } = new List<Scene>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual ICollection<MediaAsset> MediaAssets { get; set; } = new List<MediaAsset>();

    public virtual Scene? NextScene { get; set; }

    public virtual Scene? PreviousScene { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<ProviderRequest> ProviderRequests { get; set; } = new List<ProviderRequest>();

    public virtual ICollection<ScenePrompt> ScenePrompts { get; set; } = new List<ScenePrompt>();

    public virtual Script Script { get; set; } = null!;

    public virtual ICollection<SoundEffect> SoundEffects { get; set; } = new List<SoundEffect>();

    public virtual StyleProfile StyleProfile { get; set; } = null!;

    public virtual ICollection<UsageCost> UsageCosts { get; set; } = new List<UsageCost>();

    public virtual ICollection<VideoGeneration> VideoGenerations { get; set; } = new List<VideoGeneration>();

    public virtual ICollection<VoiceGeneration> VoiceGenerations { get; set; } = new List<VoiceGeneration>();
}
