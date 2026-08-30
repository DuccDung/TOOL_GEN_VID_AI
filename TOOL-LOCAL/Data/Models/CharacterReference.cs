using System;
using System.Collections.Generic;

namespace TOOL_LOCAL.Data.Models;

public partial class CharacterReference
{
    public Guid CharacterReferenceId { get; set; }

    public Guid CharacterId { get; set; }

    public Guid MediaAssetId { get; set; }

    public string ReferenceType { get; set; } = null!;

    public string? ProviderReferenceId { get; set; }

    public bool IsPrimary { get; set; }

    public string ApprovalStatus { get; set; } = null!;

    public string? ApprovalComment { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Character Character { get; set; } = null!;

    public virtual MediaAsset MediaAsset { get; set; } = null!;
}
