namespace TOOL_SERVER.Models;

public sealed class ShortVideoOutfit
{
    public Guid SceneId { get; set; }
    public Guid ProjectId { get; set; }
    public int Revision { get; set; }
    public string CharacterJson { get; set; } = "";
    public string OutfitJson { get; set; } = "";
    public string Background { get; set; } = "";
    public string Motion { get; set; } = "";
    public Guid? CompositionId { get; set; }
}

public sealed class ShortVideoOperation
{
    public Guid OperationId { get; set; }
    public Guid SceneId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "Quoted";
    public int Revision { get; set; }
    public Guid? CompositionId { get; set; }
    public string QuoteJson { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedByUserId { get; set; }
}
