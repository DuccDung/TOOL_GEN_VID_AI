using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class SceneFirstFrame
{
    public Guid SceneFirstFrameId { get; set; }

    public Guid SceneId { get; set; }

    public Guid MediaAssetId { get; set; }

    public int Version { get; set; }

    public string Status { get; set; } = null!;

    public Guid? SourceCharacterReferenceId { get; set; }

    public Guid? GeneratedByProviderRequestId { get; set; }

    public int ScenePlanVersion { get; set; }

    public Guid ScenePromptId { get; set; }

    public int ScenePromptVersion { get; set; }

    public string AspectRatio { get; set; } = null!;

    public string PromptTemplateVersion { get; set; } = null!;

    public string CreatedByUserId { get; set; } = null!;

    public string? ApprovedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime? InvalidatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ProviderRequest? GeneratedByProviderRequest { get; set; }

    public virtual ICollection<ProviderRequest> InputProviderRequests { get; set; } = new List<ProviderRequest>();

    public virtual MediaAsset MediaAsset { get; set; } = null!;

    public virtual Scene Scene { get; set; } = null!;

    public virtual ScenePrompt ScenePrompt { get; set; } = null!;

    public virtual CharacterReference? SourceCharacterReference { get; set; }
}
