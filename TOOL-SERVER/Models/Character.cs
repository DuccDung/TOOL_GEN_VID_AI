using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class Character
{
    public Guid CharacterId { get; set; }

    public Guid ProjectId { get; set; }

    public string CharacterKey { get; set; } = null!;

    public int Version { get; set; }

    public string Name { get; set; } = null!;

    public string? Role { get; set; }

    public string? IdentityAnchor { get; set; }

    public string ProfileJson { get; set; } = null!;

    public string? WardrobeJson { get; set; }

    public string? ForbiddenChangesJson { get; set; }

    public string? VisualIdentity { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<CharacterReference> CharacterReferences { get; set; } = new List<CharacterReference>();

    public virtual Project Project { get; set; } = null!;
}
