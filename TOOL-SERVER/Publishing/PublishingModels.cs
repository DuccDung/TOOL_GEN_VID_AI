using Microsoft.EntityFrameworkCore;

namespace TOOL_SERVER.Publishing;

internal sealed class PublishingSchedule
{
    public Guid ScheduleId { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public Guid DeviceId { get; set; }
    public Guid SessionId { get; set; }
    public int Revision { get; set; } = 1;
    public string Status { get; set; } = "Draft";
    public string InputJson { get; set; } = "";
    public string InputHash { get; set; } = "";
    public DateTime? NextPublishAtUtc { get; set; }
    public DateTime? ConsentAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class PublishingImageAsset
{
    public Guid ImageId { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "";
    public string InfoJson { get; set; } = "";
    public byte[] ProtectedPayload { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

internal sealed class PublishingRun
{
    public Guid RunId { get; set; }
    public Guid ScheduleId { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public Guid DeviceId { get; set; }
    public Guid SessionId { get; set; }
    public int ScheduleRevision { get; set; }
    public DateTime ProductionConsentAtUtc { get; set; }
    public string InputJson { get; set; } = "";
    public DateTime GenerateAtUtc { get; set; }
    public DateTime PublishAtUtc { get; set; }
    public DateTime DeadlineAtUtc { get; set; }
    public string Status { get; set; } = "Queued";
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public string? ResumeStatus { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? SceneId { get; set; }
    public Guid? ImageQuoteId { get; set; }
    public Guid? VideoQuoteId { get; set; }
    public Guid? ProviderRequestId { get; set; }
    public decimal EstimatedCost { get; set; }
    public string? MediaSha256 { get; set; }
    public long MediaSizeBytes { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime NextCheckAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class PublishingDelivery
{
    public Guid DeliveryId { get; set; }
    public Guid RunId { get; set; }
    public string Platform { get; set; } = "";
    public Guid ConnectionId { get; set; }
    public string AccountName { get; set; } = "";
    public string SettingsJson { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? ExternalId { get; set; }
    public string? ProtectedUploadUrl { get; set; }
    public string? PostUrl { get; set; }
    public string? Message { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class PublishingConnection
{
    public Guid ConnectionId { get; set; }
    public string UserId { get; set; } = "";
    public string Platform { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Connected";
    public string ProtectedTokens { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class PublishingOAuthSession
{
    public Guid OAuthSessionId { get; set; }
    public string UserId { get; set; } = "";
    public Guid DeviceId { get; set; }
    public Guid SessionId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Platform { get; set; } = "";
    public string StateHash { get; set; } = "";
    public string ProtectedVerifier { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public bool Consumed { get; set; }
}

internal sealed class PublishingDbContext(DbContextOptions<PublishingDbContext> options) : DbContext(options)
{
    public DbSet<PublishingSchedule> Schedules => Set<PublishingSchedule>();
    public DbSet<PublishingRun> Runs => Set<PublishingRun>();
    public DbSet<PublishingImageAsset> Images => Set<PublishingImageAsset>();
    public DbSet<PublishingDelivery> Deliveries => Set<PublishingDelivery>();
    public DbSet<PublishingConnection> Connections => Set<PublishingConnection>();
    public DbSet<PublishingOAuthSession> OAuthSessions => Set<PublishingOAuthSession>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("social");
        model.Entity<PublishingSchedule>(e =>
        {
            e.ToTable("PublishingSchedules"); e.HasKey(x => x.ScheduleId);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => new { x.Status, x.NextPublishAtUtc });
        });
        model.Entity<PublishingRun>(e =>
        {
            e.ToTable("PublishingRuns"); e.HasKey(x => x.RunId);
            e.Property(x => x.Status).IsConcurrencyToken(); e.Property(x => x.LeaseId).IsConcurrencyToken();
            e.Property(x => x.EstimatedCost).HasPrecision(19, 6);
            e.HasIndex(x => new { x.ScheduleId, x.PublishAtUtc }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextCheckAtUtc });
            e.HasOne<PublishingSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PublishingImageAsset>(e => { e.ToTable("PublishingImages"); e.HasKey(x => x.ImageId); });
        model.Entity<PublishingDelivery>(e =>
        {
            e.ToTable("PublishingDeliveries"); e.HasKey(x => x.DeliveryId);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.RunId, x.Platform, x.ConnectionId }).IsUnique();
            e.HasOne<PublishingRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PublishingConnection>(e =>
        {
            e.ToTable("PublishingConnections"); e.HasKey(x => x.ConnectionId);
            e.HasIndex(x => new { x.UserId, x.Platform, x.ExternalId }).IsUnique();
            e.Property(x => x.ExternalId).HasMaxLength(150);
        });
        model.Entity<PublishingOAuthSession>(e =>
        {
            e.ToTable("PublishingOAuthSessions"); e.HasKey(x => x.OAuthSessionId);
            e.HasIndex(x => x.StateHash).IsUnique(); e.Property(x => x.Consumed).IsConcurrencyToken();
        });
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(x => x.ClrType == typeof(string)))
                property.SetMaxLength(property.Name switch
                {
                    "UserId" or "ReviewedByUserId" => 450,
                    "Status" or "Platform" or "Role" => 32,
                    "StateHash" or "InputHash" or "MediaSha256" => 64,
                    "ErrorCode" => 100,
                    "ExternalId" => 150,
                    "DisplayName" or "AccountName" => 200,
                    "Message" => 1000,
                    "PostUrl" => 512,
                    _ => null
                });
    }
}
