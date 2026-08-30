using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class Project
{
    public Guid ProjectId { get; set; }

    public Guid? OrganizationId { get; set; }

    public string? CreatedByUserId { get; set; }

    public string? RemoteUserId { get; set; }

    public Guid? RemoteDeviceId { get; set; }

    public string? OwnerDisplayNameSnapshot { get; set; }

    public string Name { get; set; } = null!;

    public string Topic { get; set; } = null!;

    public string LanguageCode { get; set; } = null!;

    public string? VoiceCode { get; set; }

    public decimal? VoiceSpeakingRate { get; set; }

    public string? VideoProviderCode { get; set; }

    public string? VideoModelCode { get; set; }

    public int? VideoPolicyVersion { get; set; }

    public string? VideoResolution { get; set; }

    public bool? VideoNativeAudio { get; set; }

    public DateTime? VideoSnapshotAtUtc { get; set; }

    public string Platform { get; set; } = null!;

    public string AspectRatio { get; set; } = null!;

    public int TargetDurationSeconds { get; set; }

    public int OutputWidth { get; set; }

    public int OutputHeight { get; set; }

    public int OutputFrameRate { get; set; }

    public string Status { get; set; } = null!;

    public int? CurrentConceptVersion { get; set; }

    public int? CurrentScriptVersion { get; set; }

    public int? CurrentCharacterVersion { get; set; }

    public int? CurrentStyleVersion { get; set; }

    public int? CurrentScenePlanVersion { get; set; }

    public bool RequireContentApproval { get; set; }

    public bool RequireStoryboardApproval { get; set; }

    public decimal? BudgetLimit { get; set; }

    public decimal EstimatedCost { get; set; }

    public decimal ActualCost { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string WorkspaceRelativePath { get; set; } = null!;

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<Approval> Approvals { get; set; } = new List<Approval>();

    public virtual ICollection<Character> Characters { get; set; } = new List<Character>();

    public virtual ICollection<Concept> Concepts { get; set; } = new List<Concept>();

    public virtual ICollection<FinalVideo> FinalVideos { get; set; } = new List<FinalVideo>();

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    public virtual ICollection<MediaAsset> MediaAssets { get; set; } = new List<MediaAsset>();

    public virtual ICollection<MusicAsset> MusicAssets { get; set; } = new List<MusicAsset>();

    public virtual ICollection<ProviderRequest> ProviderRequests { get; set; } = new List<ProviderRequest>();

    public virtual ICollection<RenderJob> RenderJobs { get; set; } = new List<RenderJob>();

    public virtual ICollection<Scene> Scenes { get; set; } = new List<Scene>();

    public virtual ICollection<Script> Scripts { get; set; } = new List<Script>();

    public virtual ICollection<SoundEffect> SoundEffects { get; set; } = new List<SoundEffect>();

    public virtual ICollection<StyleProfile> StyleProfiles { get; set; } = new List<StyleProfile>();

    public virtual ICollection<Subtitle> Subtitles { get; set; } = new List<Subtitle>();

    public virtual ICollection<UsageCost> UsageCosts { get; set; } = new List<UsageCost>();

    public virtual ICollection<VoiceGeneration> VoiceGenerations { get; set; } = new List<VoiceGeneration>();
}
