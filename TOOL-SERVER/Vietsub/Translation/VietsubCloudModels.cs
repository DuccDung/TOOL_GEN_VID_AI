using Microsoft.EntityFrameworkCore;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Vietsub.Translation;

public sealed class VietsubCloudJob
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public string UserId { get; set; } = "";
    public Guid SessionId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid ClientOperationId { get; set; }
    public Guid TrackId { get; set; }
    public string SnapshotHash { get; set; } = "";
    public string? ProtectedInput { get; set; }
    public string ModelCode { get; set; } = "";
    public Guid ProviderModelId { get; set; }
    public Guid CredentialId { get; set; }
    public string RateSnapshotJson { get; set; } = "";
    public string CurrencyCode { get; set; } = "USD";
    public string PromptVersion { get; set; } = "subtitle-v1";
    public int MaximumOutputTokens { get; set; }
    public string Status { get; set; } = VietsubCloudStates.Queued;
    public bool Active { get; set; } = true;
    public string? ErrorCode { get; set; }
    public int TotalCues { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime? ResultExpiresAtUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public bool Acknowledged { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class VietsubCloudBatch
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public int Ordinal { get; set; }
    public int Attempt { get; set; }
    public int TargetCount { get; set; }
    public string Status { get; set; } = "QUEUED";
    public Guid RequestId { get; set; }
    public Guid? ReservationId { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string? UsageJson { get; set; }
    public string? ResponseId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ProtectedResult { get; set; }
    public bool Settled { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

// Request identity and safe accounting history survive explicit retries of a batch.
public sealed class VietsubCloudAttempt
{
    public Guid RequestId { get; set; }
    public Guid JobId { get; set; }
    public int Ordinal { get; set; }
    public int Attempt { get; set; }
    public string Status { get; set; } = "DISPATCHING";
    public string? UsageJson { get; set; }
    public string? ResponseId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal static class VietsubCloudModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<VietsubCloudAttempt>(e =>
        {
            e.ToTable("CloudTranslationAttempts", "vs"); e.HasKey(x => x.RequestId);
            e.Property(x => x.Status).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.ResponseId).HasMaxLength(200);
            e.HasIndex(x => new { x.JobId, x.Ordinal, x.Attempt }).IsUnique();
            e.HasOne<VietsubCloudJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<VietsubCloudJob>(e =>
        {
            e.ToTable("CloudTranslationJobs", "vs"); e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.ModelCode).HasMaxLength(200);
            e.Property(x => x.SnapshotHash).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.PromptVersion).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.OrganizationId, x.ClientOperationId }).IsUnique();
            e.HasIndex(x => x.ProjectId).IsUnique().HasFilter("[Active] = 1");
            e.HasIndex(x => new { x.Status, x.LeaseUntilUtc });
        });
        builder.Entity<VietsubCloudBatch>(e =>
        {
            e.ToTable("CloudTranslationBatches", "vs"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.ResponseId).HasMaxLength(200);
            e.Property(x => x.EstimatedCost).HasPrecision(19, 6);
            e.Property(x => x.ActualCost).HasPrecision(19, 6);
            e.HasIndex(x => new { x.JobId, x.Ordinal }).IsUnique();
            e.HasIndex(x => x.RequestId).IsUnique();
            e.HasOne<VietsubCloudJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

public sealed class VietsubCloudTranslationOptions
{
    public bool Enabled { get; set; }
    public string ModelCode { get; set; } = "";
    public int MaximumOutputTokens { get; set; } = 6_000;
    public int RequestTimeoutSeconds { get; set; } = 120;
    public int MaximumConcurrentJobs { get; set; } = 2;
    public int MaximumConcurrentJobsPerOrganization { get; set; } = 1;
}
