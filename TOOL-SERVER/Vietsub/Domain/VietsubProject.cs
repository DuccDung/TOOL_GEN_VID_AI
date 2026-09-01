namespace TOOL_SERVER.Vietsub.Domain;

public sealed class VietsubProject
{
    public Guid ProjectId { get; set; }

    public Guid OrganizationId { get; set; }

    public string CreatedByUserId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Status { get; set; } = VietsubProjectStatuses.Draft;

    public string SourceLanguageCode { get; set; } = "auto";

    public string TargetLanguageCode { get; set; } = "vi";

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public static class VietsubProjectStatuses
{
    public const string Draft = "DRAFT";
    public const string Ready = "READY";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}
