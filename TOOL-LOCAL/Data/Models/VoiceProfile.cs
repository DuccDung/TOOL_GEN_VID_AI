namespace TOOL_LOCAL.Data.Models;

public partial class VoiceProfile
{
    public Guid VoiceProfileId { get; set; }

    public Guid ProjectId { get; set; }

    public string Scope { get; set; } = null!;

    public Guid? CharacterId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual Character? Character { get; set; }

    public virtual ICollection<VoiceProfileVersion> Versions { get; set; } = new List<VoiceProfileVersion>();
}
