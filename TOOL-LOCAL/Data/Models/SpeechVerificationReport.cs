using System;

namespace TOOL_LOCAL.Data.Models;

public partial class SpeechVerificationReport
{
    public Guid SpeechVerificationReportId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid SceneId { get; set; }

    public Guid? VoiceGenerationId { get; set; }

    public Guid? SourceMediaAssetId { get; set; }

    public Guid ProviderRequestId { get; set; }

    public string ExpectedSpeechHash { get; set; } = null!;

    public string MediaSha256 { get; set; } = null!;

    public string Transcript { get; set; } = null!;

    public string NormalizedTranscript { get; set; } = null!;

    public decimal WordErrorRate { get; set; }

    public decimal CharacterErrorRate { get; set; }

    public decimal RequiredTermRecall { get; set; }

    public string RequiredTermsJson { get; set; } = "[]";

    public string MissingTermsJson { get; set; } = "[]";

    public long? SpeechStartMs { get; set; }

    public long? SpeechEndMs { get; set; }

    public string? WordTimingsJson { get; set; }

    public string Status { get; set; } = null!;

    public bool ReviewApproved { get; set; }

    public string? ReviewReason { get; set; }

    public string? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;
}
