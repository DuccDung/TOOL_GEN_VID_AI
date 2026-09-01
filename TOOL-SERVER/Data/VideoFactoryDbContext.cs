using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Models;

namespace TOOL_SERVER.Data;

public partial class VideoFactoryDbContext : DbContext
{
    public VideoFactoryDbContext(DbContextOptions<VideoFactoryDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AccountAuditLog> AccountAuditLogs { get; set; }

    public virtual DbSet<AppRelease> AppReleases { get; set; }

    public virtual DbSet<AppReleaseArtifact> AppReleaseArtifacts { get; set; }

    public virtual DbSet<AppSetting> AppSettings { get; set; }

    public virtual DbSet<Approval> Approvals { get; set; }

    public virtual DbSet<AspNetRole> AspNetRoles { get; set; }

    public virtual DbSet<AspNetRoleClaim> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetUser> AspNetUsers { get; set; }

    public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUserToken> AspNetUserTokens { get; set; }

    public virtual DbSet<Character> Characters { get; set; }

    public virtual DbSet<CharacterReference> CharacterReferences { get; set; }

    public virtual DbSet<Concept> Concepts { get; set; }

    public virtual DbSet<CostRate> CostRates { get; set; }

    public virtual DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    public virtual DbSet<FinalVideo> FinalVideos { get; set; }

    public virtual DbSet<GeneratedImageOutput> GeneratedImageOutputs { get; set; }

    public virtual DbSet<GeneratedVoiceOutput> GeneratedVoiceOutputs { get; set; }

    public virtual DbSet<GeneratedVideoOutput> GeneratedVideoOutputs { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<JobDependency> JobDependencies { get; set; }

    public virtual DbSet<JobEvent> JobEvents { get; set; }

    public virtual DbSet<LicenseActivation> LicenseActivations { get; set; }

    public virtual DbSet<LicensePlan> LicensePlans { get; set; }

    public virtual DbSet<MediaAsset> MediaAssets { get; set; }

    public virtual DbSet<MusicAsset> MusicAssets { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectAsset> ProjectAssets { get; set; }

    public virtual DbSet<ProjectAssetVersion> ProjectAssetVersions { get; set; }

    public virtual DbSet<Provider> Providers { get; set; }

    public virtual DbSet<ProviderModel> ProviderModels { get; set; }

    public virtual DbSet<ProviderRequest> ProviderRequests { get; set; }

    public virtual DbSet<ProviderRequestAssetVersion> ProviderRequestAssetVersions { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<RegisteredDevice> RegisteredDevices { get; set; }

    public virtual DbSet<RenderJob> RenderJobs { get; set; }

    public virtual DbSet<Scene> Scenes { get; set; }

    public virtual DbSet<SceneAssetAssignment> SceneAssetAssignments { get; set; }

    public virtual DbSet<ScenePrompt> ScenePrompts { get; set; }

    public virtual DbSet<SchemaVersion> SchemaVersions { get; set; }

    public virtual DbSet<SchemaVersion1> SchemaVersions1 { get; set; }

    public virtual DbSet<Script> Scripts { get; set; }

    public virtual DbSet<ServerSetting> ServerSettings { get; set; }

    public virtual DbSet<SoundEffect> SoundEffects { get; set; }

    public virtual DbSet<StyleProfile> StyleProfiles { get; set; }

    public virtual DbSet<Subtitle> Subtitles { get; set; }

    public virtual DbSet<UsageCost> UsageCosts { get; set; }

    public virtual DbSet<UserLicense> UserLicenses { get; set; }

    public virtual DbSet<UserSession> UserSessions { get; set; }

    public virtual DbSet<VideoGeneration> VideoGenerations { get; set; }

    public virtual DbSet<VoiceGeneration> VoiceGenerations { get; set; }

    public virtual DbSet<VwProjectProgress> VwProjectProgresses { get; set; }

    public virtual DbSet<VwUserAccountSummary> VwUserAccountSummaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountAuditLog>(entity =>
        {
            entity.ToTable("AccountAuditLogs", "auth");

            entity.HasIndex(e => new { e.EventType, e.OccurredAtUtc }, "IX_AccountAuditLogs_Event_OccurredAt").IsDescending(false, true);

            entity.HasIndex(e => new { e.UserId, e.OccurredAtUtc }, "IX_AccountAuditLogs_User_OccurredAt").IsDescending(false, true);

            entity.Property(e => e.CorrelationId)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.EventType)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .IsUnicode(false);
            entity.Property(e => e.OccurredAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_AccountAuditLogs_OccurredAtUtc");
            entity.Property(e => e.UserAgent).HasMaxLength(1000);
        });

        modelBuilder.Entity<AppRelease>(entity =>
        {
            entity.ToTable("AppReleases", "auth");

            entity.HasIndex(e => new { e.Version, e.BuildNumber, e.Channel, e.Platform }, "UQ_AppReleases_Version_Build_Channel_Platform").IsUnique();

            entity.Property(e => e.AppReleaseId).HasDefaultValueSql("(newsequentialid())", "DF_AppReleases_Id");
            entity.Property(e => e.Channel)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Stable", "DF_AppReleases_Channel");
            entity.Property(e => e.BuildNumber).HasDefaultValue(1, "DF_AppReleases_BuildNumber");
            entity.Property(e => e.DownloadUrl).HasMaxLength(2000);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_AppReleases_IsActive");
            entity.Property(e => e.MinimumSupportedDesktopVersion)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Platform)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("win-x64", "DF_AppReleases_Platform");
            entity.Property(e => e.PublishedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_AppReleases_PublishedAtUtc");
            entity.Property(e => e.Sha256)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.Version)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<AppReleaseArtifact>(entity =>
        {
            entity.ToTable("AppReleaseArtifacts", "auth");

            entity.HasIndex(e => new { e.AppReleaseId, e.Kind }, "UQ_AppReleaseArtifacts_Release_Kind").IsUnique();
            entity.Property(e => e.AppReleaseArtifactId).HasDefaultValueSql("(newsequentialid())", "DF_AppReleaseArtifacts_Id");
            entity.Property(e => e.CreatedAtUtc).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())", "DF_AppReleaseArtifacts_CreatedAtUtc");
            entity.Property(e => e.FileName).HasMaxLength(260);
            entity.Property(e => e.Kind).HasMaxLength(30).IsUnicode(false);
            entity.Property(e => e.RelativePath).HasMaxLength(1000);
            entity.Property(e => e.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            entity.HasOne(d => d.AppRelease).WithMany(p => p.Artifacts)
                .HasForeignKey(d => d.AppReleaseId)
                .HasConstraintName("FK_AppReleaseArtifacts_AppReleases");
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("AppSettings", "vf");

            entity.HasIndex(e => e.SettingKey, "UQ_AppSettings_SettingKey").IsUnique();

            entity.Property(e => e.AppSettingId).HasDefaultValueSql("(newsequentialid())", "DF_AppSettings_Id");
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SettingKey).HasMaxLength(200);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_AppSettings_UpdatedAtUtc");
        });

        modelBuilder.Entity<Approval>(entity =>
        {
            entity.ToTable("Approvals", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.TargetType, e.TargetId, e.DecidedAtUtc }, "IX_Approvals_Target").IsDescending(false, false, false, true);

            entity.Property(e => e.ApprovalId).HasDefaultValueSql("(newsequentialid())", "DF_Approvals_Id");
            entity.Property(e => e.ApprovedBy).HasMaxLength(200);
            entity.Property(e => e.Comment).HasMaxLength(2000);
            entity.Property(e => e.DecidedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Approvals_DecidedAtUtc");
            entity.Property(e => e.Decision)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.TargetType)
                .HasMaxLength(40)
                .IsUnicode(false);

            entity.HasOne(d => d.Project).WithMany(p => p.Approvals)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Approvals_Projects");
        });

        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");

            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<AspNetRoleClaim>(entity =>
        {
            entity.HasIndex(e => e.RoleId, "IX_AspNetRoleClaims_RoleId");

            entity.HasOne(d => d.Role).WithMany(p => p.AspNetRoleClaims).HasForeignKey(d => d.RoleId);
        });

        modelBuilder.Entity<AspNetUser>(entity =>
        {
            entity.HasIndex(e => e.NormalizedEmail, "EmailIndex");

            entity.HasIndex(e => e.NormalizedUserName, "UserNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedUserName] IS NOT NULL)");

            entity.Property(e => e.AccountStatus)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Active", "DF_AspNetUsers_AccountStatus");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_AspNetUsers_CreatedAtUtc");
            entity.Property(e => e.DeletedAtUtc).HasPrecision(3);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.LastLoginAtUtc).HasPrecision(3);
            entity.Property(e => e.NormalizedEmail).HasMaxLength(256);
            entity.Property(e => e.NormalizedUserName).HasMaxLength(256);
            entity.Property(e => e.PasswordChangedAtUtc).HasPrecision(3);
            entity.Property(e => e.PreferredLanguageCode)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("vi-VN", "DF_AspNetUsers_Language");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.TermsAcceptedAtUtc).HasPrecision(3);
            entity.Property(e => e.TimeZoneId)
                .HasMaxLength(100)
                .HasDefaultValue("SE Asia Standard Time", "DF_AspNetUsers_TimeZone");
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_AspNetUsers_UpdatedAtUtc");
            entity.Property(e => e.UserName).HasMaxLength(256);

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRole",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<AspNetUser>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("AspNetUserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<AspNetUserClaim>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_AspNetUserClaims_UserId");

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserClaims).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserLogin>(entity =>
        {
            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });

            entity.HasIndex(e => e.UserId, "IX_AspNetUserLogins_UserId");

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.ProviderKey).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserLogins).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserToken>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.Name).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserTokens).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<Character>(entity =>
        {
            entity.ToTable("Characters", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.CharacterKey, e.Version }, "UQ_Characters_Project_Key_Version").IsUnique();

            entity.Property(e => e.CharacterId).HasDefaultValueSql("(newsequentialid())", "DF_Characters_Id");
            entity.Property(e => e.ApprovedAtUtc).HasPrecision(3);
            entity.Property(e => e.CharacterKey)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Characters_CreatedAtUtc");
            entity.Property(e => e.IdentityAnchor)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Role).HasMaxLength(200);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Draft", "DF_Characters_Status");

            entity.HasOne(d => d.Project).WithMany(p => p.Characters)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Characters_Projects");
        });

        modelBuilder.Entity<CharacterReference>(entity =>
        {
            entity.ToTable("CharacterReferences", "vf");

            entity.HasIndex(e => new { e.CharacterId, e.MediaAssetId }, "UQ_CharacterReferences_Character_Asset").IsUnique();

            entity.Property(e => e.CharacterReferenceId).HasDefaultValueSql("(newsequentialid())", "DF_CharacterReferences_Id");
            entity.Property(e => e.ApprovalComment).HasMaxLength(2000);
            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pending", "DF_CharacterReferences_Approval");
            entity.Property(e => e.ApprovedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_CharacterReferences_CreatedAtUtc");
            entity.Property(e => e.ProviderReferenceId).HasMaxLength(300);
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.Character).WithMany(p => p.CharacterReferences)
                .HasForeignKey(d => d.CharacterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CharacterReferences_Characters");

            entity.HasOne(d => d.MediaAsset).WithMany(p => p.CharacterReferences)
                .HasForeignKey(d => d.MediaAssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CharacterReferences_MediaAssets");
        });

        modelBuilder.Entity<Concept>(entity =>
        {
            entity.ToTable("Concepts", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.Version }, "UQ_Concepts_Project_Version").IsUnique();

            entity.Property(e => e.ConceptId).HasDefaultValueSql("(newsequentialid())", "DF_Concepts_Id");
            entity.Property(e => e.Angle).HasMaxLength(2000);
            entity.Property(e => e.ApprovedAtUtc).HasPrecision(3);
            entity.Property(e => e.Audience).HasMaxLength(2000);
            entity.Property(e => e.CallToAction).HasMaxLength(2000);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Concepts_CreatedAtUtc");
            entity.Property(e => e.ModelCode).HasMaxLength(200);
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SelectedHook).HasMaxLength(2000);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Draft", "DF_Concepts_Status");
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.ViralScore).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.Project).WithMany(p => p.Concepts)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Concepts_Projects");
        });

        modelBuilder.Entity<CostRate>(entity =>
        {
            entity.ToTable("CostRates", "vf");

            entity.Property(e => e.CostRateId).HasDefaultValueSql("(newsequentialid())", "DF_CostRates_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_CostRates_CreatedAtUtc");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength()
                .HasDefaultValue("USD", "DF_CostRates_Currency");
            entity.Property(e => e.EffectiveFromUtc).HasPrecision(3);
            entity.Property(e => e.EffectiveToUtc).HasPrecision(3);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_CostRates_IsActive");
            entity.Property(e => e.Unit)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(19, 8)");
            entity.Property(e => e.UsageType)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.ProviderModel).WithMany(p => p.CostRates)
                .HasForeignKey(d => d.ProviderModelId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CostRates_ProviderModels");
        });

        modelBuilder.Entity<FinalVideo>(entity =>
        {
            entity.ToTable("FinalVideos", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.Version }, "UQ_FinalVideos_Project_Version").IsUnique();

            entity.Property(e => e.FinalVideoId).HasDefaultValueSql("(newsequentialid())", "DF_FinalVideos_Id");
            entity.Property(e => e.ApprovedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_FinalVideos_CreatedAtUtc");
            entity.Property(e => e.ExportedAtUtc).HasPrecision(3);
            entity.Property(e => e.ExportedPath).HasMaxLength(1000);
            entity.Property(e => e.QualityScore).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("AwaitingApproval", "DF_FinalVideos_Status");

            entity.HasOne(d => d.MediaAsset).WithMany(p => p.FinalVideos)
                .HasForeignKey(d => d.MediaAssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FinalVideos_MediaAssets");

            entity.HasOne(d => d.Project).WithMany(p => p.FinalVideos)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FinalVideos_Projects");

            entity.HasOne(d => d.RenderJob).WithMany(p => p.FinalVideos)
                .HasForeignKey(d => d.RenderJobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FinalVideos_RenderJobs");
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("Jobs", "vf");

            entity.HasIndex(e => new { e.Status, e.AvailableAtUtc, e.Priority, e.CreatedAtUtc }, "IX_Jobs_ClaimQueue").IsDescending(false, false, true, false);

            entity.HasIndex(e => new { e.ProjectId, e.Status }, "IX_Jobs_Project_Status");

            entity.HasIndex(e => e.IdempotencyKey, "UX_Jobs_IdempotencyKey")
                .IsUnique()
                .HasFilter("([IdempotencyKey] IS NOT NULL)");

            entity.Property(e => e.JobId).HasDefaultValueSql("(newsequentialid())", "DF_Jobs_Id");
            entity.Property(e => e.AvailableAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Jobs_AvailableAtUtc");
            entity.Property(e => e.CompletedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Jobs_CreatedAtUtc");
            entity.Property(e => e.ErrorCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
            entity.Property(e => e.HeartbeatAtUtc).HasPrecision(3);
            entity.Property(e => e.JobType)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.LeaseExpiresAtUtc).HasPrecision(3);
            entity.Property(e => e.LockedAtUtc).HasPrecision(3);
            entity.Property(e => e.LockedBy).HasMaxLength(200);
            entity.Property(e => e.MaxAttempts).HasDefaultValue(3, "DF_Jobs_MaxAttempts");
            entity.Property(e => e.ProgressPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.StartedAtUtc).HasPrecision(3);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Pending", "DF_Jobs_Status");
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Jobs_UpdatedAtUtc");

            entity.HasOne(d => d.ParentJob).WithMany(p => p.InverseParentJob)
                .HasForeignKey(d => d.ParentJobId)
                .HasConstraintName("FK_Jobs_ParentJob");

            entity.HasOne(d => d.Project).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Jobs_Projects");

            entity.HasOne(d => d.Scene).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.SceneId)
                .HasConstraintName("FK_Jobs_Scenes");
        });

        modelBuilder.Entity<JobDependency>(entity =>
        {
            entity.ToTable("JobDependencies", "vf");

            entity.HasIndex(e => new { e.DependsOnJobId, e.JobId }, "IX_JobDependencies_DependsOn");

            entity.HasIndex(e => new { e.JobId, e.DependsOnJobId }, "UQ_JobDependencies_Pair").IsUnique();

            entity.Property(e => e.JobDependencyId).HasDefaultValueSql("(newsequentialid())", "DF_JobDependencies_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_JobDependencies_CreatedAtUtc");

            entity.HasOne(d => d.DependsOnJob).WithMany(p => p.JobDependencyDependsOnJobs)
                .HasForeignKey(d => d.DependsOnJobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_JobDependencies_DependsOn");

            entity.HasOne(d => d.Job).WithMany(p => p.JobDependencyJobs)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_JobDependencies_Job");
        });

        modelBuilder.Entity<JobEvent>(entity =>
        {
            entity.ToTable("JobEvents", "vf");

            entity.HasIndex(e => new { e.JobId, e.OccurredAtUtc }, "IX_JobEvents_Job_OccurredAt").IsDescending(false, true);

            entity.Property(e => e.EventType)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.FromStatus)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Message).HasMaxLength(4000);
            entity.Property(e => e.OccurredAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_JobEvents_OccurredAtUtc");
            entity.Property(e => e.ToStatus)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.Job).WithMany(p => p.JobEvents)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_JobEvents_Jobs");
        });

        modelBuilder.Entity<LicenseActivation>(entity =>
        {
            entity.ToTable("LicenseActivations", "auth");

            entity.HasIndex(e => new { e.DeviceId, e.Status, e.LastVerifiedAtUtc }, "IX_LicenseActivations_Device_Status").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.UserLicenseId, e.DeviceId }, "UQ_LicenseActivations_License_Device").IsUnique();

            entity.Property(e => e.LicenseActivationId).HasDefaultValueSql("(newsequentialid())", "DF_LicenseActivations_Id");
            entity.Property(e => e.ActivatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_LicenseActivations_ActivatedAtUtc");
            entity.Property(e => e.LastVerifiedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_LicenseActivations_LastVerifiedAtUtc");
            entity.Property(e => e.RevokedAtUtc).HasPrecision(3);
            entity.Property(e => e.RevokedReason).HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active", "DF_LicenseActivations_Status");

            entity.HasOne(d => d.Device).WithMany(p => p.LicenseActivations)
                .HasForeignKey(d => d.DeviceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LicenseActivations_RegisteredDevices");

            entity.HasOne(d => d.UserLicense).WithMany(p => p.LicenseActivations)
                .HasForeignKey(d => d.UserLicenseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LicenseActivations_UserLicenses");
        });

        modelBuilder.Entity<LicensePlan>(entity =>
        {
            entity.ToTable("LicensePlans", "auth");

            entity.HasIndex(e => e.PlanCode, "UQ_LicensePlans_PlanCode").IsUnique();

            entity.Property(e => e.LicensePlanId).HasDefaultValueSql("(newsequentialid())", "DF_LicensePlans_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_LicensePlans_CreatedAtUtc");
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_LicensePlans_IsActive");
            entity.Property(e => e.MaxActivatedDevices).HasDefaultValue(1, "DF_LicensePlans_MaxDevices");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.OfflineGraceHours).HasDefaultValue(24, "DF_LicensePlans_OfflineGrace");
            entity.Property(e => e.PlanCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_LicensePlans_UpdatedAtUtc");
        });

        modelBuilder.Entity<GeneratedImageOutput>(entity =>
        {
            entity.ToTable("GeneratedImageOutputs", "vf");

            entity.HasKey(e => e.ProviderRequestId);
            entity.Property(e => e.MimeType).HasMaxLength(150).IsUnicode(false);
            entity.Property(e => e.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            entity.Property(e => e.CreatedAtUtc).HasPrecision(3);
            entity.Property(e => e.ExpiresAtUtc).HasPrecision(3);
            entity.Property(e => e.DownloadedAtUtc).HasPrecision(3);
            entity.HasIndex(e => e.ExpiresAtUtc, "IX_GeneratedImageOutputs_ExpiresAtUtc");

            entity.HasOne(e => e.ProviderRequest)
                .WithOne(e => e.GeneratedImageOutput)
                .HasForeignKey<GeneratedImageOutput>(e => e.ProviderRequestId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_GeneratedImageOutputs_ProviderRequests");
        });

        modelBuilder.Entity<GeneratedVoiceOutput>(entity =>
        {
            entity.ToTable("GeneratedVoiceOutputs", "vf");

            entity.HasKey(e => e.ProviderRequestId);
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.MimeType)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.Sha256)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.CreatedAtUtc).HasPrecision(3);
            entity.Property(e => e.ExpiresAtUtc).HasPrecision(3);
            entity.Property(e => e.DownloadedAtUtc).HasPrecision(3);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasIndex(e => e.ExpiresAtUtc, "IX_GeneratedVoiceOutputs_ExpiresAtUtc");

            entity.HasOne(e => e.ProviderRequest)
                .WithOne(e => e.GeneratedVoiceOutput)
                .HasForeignKey<GeneratedVoiceOutput>(e => e.ProviderRequestId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_GeneratedVoiceOutputs_ProviderRequests");
        });

        modelBuilder.Entity<GeneratedVideoOutput>(entity =>
        {
            entity.ToTable("GeneratedVideoOutputs", "vf");
            entity.HasKey(e => e.ProviderRequestId);
            entity.Property(e => e.StorageKey).HasMaxLength(500);
            entity.Property(e => e.MimeType).HasMaxLength(150).IsUnicode(false);
            entity.Property(e => e.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            entity.Property(e => e.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(e => e.ExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(e => e.DownloadedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(e => e.DeletedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(e => e.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(e => new { e.Status, e.ExpiresAtUtc }, "IX_GeneratedVideoOutputs_Status_ExpiresAtUtc");
            entity.HasOne(e => e.ProviderRequest)
                .WithOne(e => e.GeneratedVideoOutput)
                .HasForeignKey<GeneratedVideoOutput>(e => e.ProviderRequestId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_GeneratedVideoOutputs_ProviderRequests");
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.ToTable("MediaAssets", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.AssetType, e.Status }, "IX_MediaAssets_Project_Type_Status");

            entity.HasIndex(e => new { e.ProjectId, e.RelativePath }, "UQ_MediaAssets_Project_Path").IsUnique();

            entity.HasIndex(e => e.SourceProviderRequestId, "UX_MediaAssets_SourceProviderRequest")
                .IsUnique()
                .HasFilter("([SourceProviderRequestId] IS NOT NULL)");

            entity.Property(e => e.MediaAssetId).HasDefaultValueSql("(newsequentialid())", "DF_MediaAssets_Id");
            entity.Property(e => e.AssetType)
                .HasMaxLength(60)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_MediaAssets_CreatedAtUtc");
            entity.Property(e => e.DeletedAtUtc).HasPrecision(3);
            entity.Property(e => e.DisplayName).HasMaxLength(300);
            entity.Property(e => e.FrameRate).HasColumnType("decimal(9, 3)");
            entity.Property(e => e.MimeType)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.RelativePath).HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Sha256)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.SourceExternalRequestId).HasMaxLength(300);
            entity.Property(e => e.SourceProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.SourceType)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Generated", "DF_MediaAssets_SourceType");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Ready", "DF_MediaAssets_Status");
            entity.Property(e => e.VerifiedAtUtc).HasPrecision(3);

            entity.HasOne(d => d.Project).WithMany(p => p.MediaAssets)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MediaAssets_Projects");

            entity.HasOne(d => d.Scene).WithMany(p => p.MediaAssets)
                .HasForeignKey(d => d.SceneId)
                .HasConstraintName("FK_MediaAssets_Scenes");

            entity.HasOne<ProviderRequest>()
                .WithMany()
                .HasForeignKey(d => d.SourceProviderRequestId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_MediaAssets_SourceProviderRequest");
        });

        modelBuilder.Entity<MusicAsset>(entity =>
        {
            entity.ToTable("MusicAssets", "vf");

            entity.Property(e => e.MusicAssetId).HasDefaultValueSql("(newsequentialid())", "DF_MusicAssets_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_MusicAssets_CreatedAtUtc");
            entity.Property(e => e.GainDb)
                .HasDefaultValue(-18m, "DF_MusicAssets_Gain")
                .HasColumnType("decimal(7, 3)");
            entity.Property(e => e.LoopEnabled).HasDefaultValue(true, "DF_MusicAssets_Loop");
            entity.Property(e => e.SourceType)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Title).HasMaxLength(300);

            entity.HasOne(d => d.MediaAsset).WithMany(p => p.MusicAssets)
                .HasForeignKey(d => d.MediaAssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MusicAssets_MediaAssets");

            entity.HasOne(d => d.Project).WithMany(p => p.MusicAssets)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MusicAssets_Projects");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects", "vf");

            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.UpdatedAtUtc }, "IX_Projects_Organization_Status")
                .IsDescending(false, false, true)
                .HasFilter("([OrganizationId] IS NOT NULL)");

            entity.HasIndex(e => new { e.RemoteUserId, e.Status, e.UpdatedAtUtc }, "IX_Projects_RemoteUser_Status")
                .IsDescending(false, false, true)
                .HasFilter("([RemoteUserId] IS NOT NULL)");

            entity.HasIndex(e => new { e.Status, e.UpdatedAtUtc }, "IX_Projects_Status_UpdatedAt").IsDescending(false, true);

            entity.Property(e => e.ProjectId).HasDefaultValueSql("(newsequentialid())", "DF_Projects_Id");
            entity.Property(e => e.ActualCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.AspectRatio)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("9:16", "DF_Projects_AspectRatio");
            entity.Property(e => e.BudgetLimit).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.CompletedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedByUserId).HasMaxLength(450);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Projects_CreatedAtUtc");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength()
                .HasDefaultValue("USD", "DF_Projects_Currency");
            entity.Property(e => e.DeletedAtUtc).HasPrecision(3);
            entity.Property(e => e.EstimatedCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.LanguageCode)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("vi-VN", "DF_Projects_Language");
            entity.Property(e => e.VoiceCode).HasMaxLength(100);
            entity.Property(e => e.VoiceSpeakingRate).HasColumnType("decimal(6, 3)");
            entity.Property(e => e.VideoProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.VideoModelCode).HasMaxLength(200);
            entity.Property(e => e.VideoResolution)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.VideoSnapshotAtUtc).HasColumnType("datetime2(3)");
            entity.Property(e => e.LastErrorCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.LastErrorMessage).HasMaxLength(4000);
            entity.Property(e => e.Name).HasMaxLength(300);
            entity.Property(e => e.OutputFrameRate).HasDefaultValue(30, "DF_Projects_OutputFrameRate");
            entity.Property(e => e.OutputHeight).HasDefaultValue(1920, "DF_Projects_OutputHeight");
            entity.Property(e => e.OutputWidth).HasDefaultValue(1080, "DF_Projects_OutputWidth");
            entity.Property(e => e.OwnerDisplayNameSnapshot).HasMaxLength(200);
            entity.Property(e => e.Platform)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("TikTok", "DF_Projects_Platform");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .IsUnicode(false)
                .HasDefaultValue("Draft", "DF_Projects_Status");
            entity.Property(e => e.TargetDurationSeconds).HasDefaultValue(30, "DF_Projects_TargetDuration");
            entity.Property(e => e.Topic).HasMaxLength(2000);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Projects_UpdatedAtUtc");
            entity.Property(e => e.WorkspaceRelativePath).HasMaxLength(500);

            entity.HasOne(d => d.RemoteUser).WithMany(p => p.Projects).HasForeignKey(d => d.RemoteUserId);

            entity.HasOne(d => d.RegisteredDevice).WithMany(p => p.Projects)
                .HasPrincipalKey(p => new { p.DeviceId, p.UserId })
                .HasForeignKey(d => new { d.RemoteDeviceId, d.RemoteUserId })
                .HasConstraintName("FK_Projects_RegisteredDevices_RemoteOwner");
        });

        modelBuilder.Entity<Provider>(entity =>
        {
            entity.ToTable("Providers", "vf");

            entity.HasIndex(e => e.ProviderCode, "UQ_Providers_Code").IsUnique();

            entity.Property(e => e.ProviderId).HasDefaultValueSql("(newsequentialid())", "DF_Providers_Id");
            entity.Property(e => e.BaseUrl).HasMaxLength(1000);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Providers_CreatedAtUtc");
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.IsEnabled).HasDefaultValue(true, "DF_Providers_IsEnabled");
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SecretReference).HasMaxLength(500);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Providers_UpdatedAtUtc");
        });

        modelBuilder.Entity<ProviderModel>(entity =>
        {
            entity.ToTable("ProviderModels", "vf");

            entity.HasIndex(e => new { e.ProviderId, e.ModelCode, e.Modality }, "UQ_ProviderModels_Provider_Model").IsUnique();

            entity.Property(e => e.ProviderModelId).HasDefaultValueSql("(newsequentialid())", "DF_ProviderModels_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ProviderModels_CreatedAtUtc");
            entity.Property(e => e.DisplayName).HasMaxLength(300);
            entity.Property(e => e.IsEnabled).HasDefaultValue(true, "DF_ProviderModels_IsEnabled");
            entity.Property(e => e.Modality)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.ModelCode).HasMaxLength(200);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ProviderModels_UpdatedAtUtc");

            entity.HasOne(d => d.Provider).WithMany(p => p.ProviderModels)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProviderModels_Providers");
        });

        modelBuilder.Entity<ProviderRequest>(entity =>
        {
            entity.ToTable("ProviderRequests", "vf");

            entity.HasIndex(e => new { e.Status, e.NextPollAtUtc }, "IX_ProviderRequests_PollDue");

            entity.HasIndex(e => new { e.OrganizationId, e.IdempotencyKey }, "UQ_ProviderRequests_Organization_Idempotency").IsUnique();

            entity.HasIndex(e => new { e.OrganizationId, e.RequestedByUserId, e.CreatedAtUtc }, "IX_ProviderRequests_Organization_User_Created")
                .IsDescending(false, false, true);

            entity.HasIndex(e => new { e.CharacterId, e.CreatedAtUtc }, "IX_ProviderRequests_Character_Created")
                .IsDescending(false, true)
                .HasFilter("([CharacterId] IS NOT NULL)");

            entity.HasIndex(e => new { e.ProviderCode, e.ExternalRequestId }, "UX_ProviderRequests_ExternalRequest")
                .IsUnique()
                .HasFilter("([ExternalRequestId] IS NOT NULL)");

            entity.Property(e => e.ProviderRequestId).HasDefaultValueSql("(newsequentialid())", "DF_ProviderRequests_Id");
            entity.Property(e => e.ActualCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.CompletedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ProviderRequests_CreatedAtUtc");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength()
                .HasDefaultValue("USD", "DF_ProviderRequests_Currency");
            entity.Property(e => e.ErrorCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
            entity.Property(e => e.EstimatedCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.ExternalRequestId).HasMaxLength(300);
            entity.Property(e => e.RequestedByUserId).HasMaxLength(450);
            entity.Property(e => e.RequestHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.LastPolledAtUtc).HasPrecision(3);
            entity.Property(e => e.ModelCode).HasMaxLength(200);
            entity.Property(e => e.NextPollAtUtc).HasPrecision(3);
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.RequestKind)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Created", "DF_ProviderRequests_Status");
            entity.Property(e => e.SubmittedAtUtc).HasPrecision(3);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ProviderRequests_UpdatedAtUtc");

            entity.HasOne(d => d.Job).WithMany(p => p.ProviderRequests)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_ProviderRequests_Jobs");

            entity.HasOne(d => d.Project).WithMany(p => p.ProviderRequests)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProviderRequests_Projects");

            entity.HasOne(d => d.Character).WithMany()
                .HasForeignKey(d => d.CharacterId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_ProviderRequests_Characters");

            entity.HasOne(d => d.Provider).WithMany(p => p.ProviderRequests)
                .HasForeignKey(d => d.ProviderId)
                .HasConstraintName("FK_ProviderRequests_Providers");

            entity.HasOne(d => d.ProviderModel).WithMany(p => p.ProviderRequests)
                .HasForeignKey(d => d.ProviderModelId)
                .HasConstraintName("FK_ProviderRequests_ProviderModels");

            entity.HasOne(d => d.Scene).WithMany(p => p.ProviderRequests)
                .HasForeignKey(d => d.SceneId)
                .HasConstraintName("FK_ProviderRequests_Scenes");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens", "auth");

            entity.HasIndex(e => new { e.SessionId, e.ExpiresAtUtc }, "IX_RefreshTokens_Session_Expiry").IsDescending(false, true);

            entity.HasIndex(e => new { e.UserId, e.TokenFamilyId, e.CreatedAtUtc }, "IX_RefreshTokens_User_Family").IsDescending(false, false, true);

            entity.HasIndex(e => e.TokenHash, "UQ_RefreshTokens_TokenHash").IsUnique();

            entity.Property(e => e.RefreshTokenId).HasDefaultValueSql("(newsequentialid())", "DF_RefreshTokens_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_RefreshTokens_CreatedAtUtc");
            entity.Property(e => e.CreatedByIpAddress)
                .HasMaxLength(45)
                .IsUnicode(false);
            entity.Property(e => e.ExpiresAtUtc).HasPrecision(3);
            entity.Property(e => e.JwtId).HasMaxLength(100);
            entity.Property(e => e.RevokedAtUtc).HasPrecision(3);
            entity.Property(e => e.RevokedReason).HasMaxLength(500);
            entity.Property(e => e.TokenHash)
                .HasMaxLength(32)
                .IsFixedLength();
            entity.Property(e => e.TokenPrefix)
                .HasMaxLength(16)
                .IsUnicode(false);
            entity.Property(e => e.UsedAtUtc).HasPrecision(3);

            entity.HasOne(d => d.ReplacedByToken).WithMany(p => p.InverseReplacedByToken)
                .HasForeignKey(d => d.ReplacedByTokenId)
                .HasConstraintName("FK_RefreshTokens_ReplacedBy");

            entity.HasOne(d => d.Session).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RefreshTokens_UserSessions");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RefreshTokens_AspNetUsers");
        });

        modelBuilder.Entity<RegisteredDevice>(entity =>
        {
            entity.HasKey(e => e.DeviceId);

            entity.ToTable("RegisteredDevices", "auth");

            entity.HasIndex(e => new { e.UserId, e.IsRevoked, e.LastSeenAtUtc }, "IX_RegisteredDevices_User_Status").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.UserId, e.DeviceFingerprintHash }, "UQ_RegisteredDevices_User_Fingerprint").IsUnique();

            entity.HasIndex(e => new { e.DeviceId, e.UserId }, "UX_RegisteredDevices_Device_User").IsUnique();

            entity.Property(e => e.DeviceId).HasDefaultValueSql("(newsequentialid())", "DF_RegisteredDevices_Id");
            entity.Property(e => e.ApplicationVersion).HasMaxLength(50);
            entity.Property(e => e.DeviceFingerprintHash)
                .HasMaxLength(32)
                .IsFixedLength();
            entity.Property(e => e.DeviceName).HasMaxLength(200);
            entity.Property(e => e.FirstSeenAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_RegisteredDevices_FirstSeenAtUtc");
            entity.Property(e => e.LastSeenAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_RegisteredDevices_LastSeenAtUtc");
            entity.Property(e => e.OperatingSystem).HasMaxLength(200);
            entity.Property(e => e.RevokedAtUtc).HasPrecision(3);
            entity.Property(e => e.RevokedReason).HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.User).WithMany(p => p.RegisteredDevices)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RegisteredDevices_AspNetUsers");
        });

        modelBuilder.Entity<ProjectAsset>(entity =>
        {
            entity.ToTable("ProjectAssets", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.AssetKey }, "UQ_ProjectAssets_Project_AssetKey").IsUnique();
            entity.HasIndex(e => new { e.ProjectId, e.AssetType, e.Name }, "UQ_ProjectAssets_Project_Type_Name").IsUnique();
            entity.HasIndex(e => new { e.ProjectId, e.Status }, "IX_ProjectAssets_Project_Status");

            entity.Property(e => e.ProjectAssetId).HasDefaultValueSql("(newsequentialid())", "DF_ProjectAssets_Id");
            entity.Property(e => e.AssetType).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.AssetKey).HasMaxLength(80);
            entity.Property(e => e.Name).HasMaxLength(160);
            entity.Property(e => e.CanonicalDescription).HasMaxLength(2000);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Draft", "DF_ProjectAssets_Status");
            entity.Property(e => e.SourceKind)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Manual", "DF_ProjectAssets_SourceKind");
            entity.Property(e => e.CurrentVersion).HasDefaultValue(0, "DF_ProjectAssets_CurrentVersion");
            entity.Property(e => e.LockedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ProjectAssets_CreatedAtUtc");
            entity.Property(e => e.CreatedByUserId).HasMaxLength(450);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ProjectAssets_UpdatedAtUtc");
            entity.Property(e => e.UpdatedByUserId).HasMaxLength(450);
            entity.Property(e => e.RowVersion).IsRowVersion().IsConcurrencyToken();

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProjectAssets_Projects");
        });

        modelBuilder.Entity<ProjectAssetVersion>(entity =>
        {
            entity.ToTable("ProjectAssetVersions", "vf");

            entity.HasIndex(e => new { e.ProjectAssetId, e.Version }, "UQ_ProjectAssetVersions_Asset_Version").IsUnique();

            entity.Property(e => e.ProjectAssetVersionId).HasDefaultValueSql("(newsequentialid())", "DF_ProjectAssetVersions_Id");
            entity.Property(e => e.AssetType).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(160);
            entity.Property(e => e.CanonicalDescription).HasMaxLength(2000);
            entity.Property(e => e.LockedAtUtc).HasPrecision(3);
            entity.Property(e => e.LockedByUserId).HasMaxLength(450);

            entity.HasOne(e => e.ProjectAsset)
                .WithMany(e => e.Versions)
                .HasForeignKey(e => e.ProjectAssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProjectAssetVersions_ProjectAssets");
        });

        modelBuilder.Entity<SceneAssetAssignment>(entity =>
        {
            entity.ToTable("SceneAssetAssignments", "vf");

            entity.HasKey(e => new { e.SceneId, e.ProjectAssetId });
            entity.HasIndex(e => e.ProjectAssetId, "IX_SceneAssetAssignments_ProjectAsset");
            entity.Property(e => e.AssignedByUserId).HasMaxLength(450);
            entity.Property(e => e.AssignedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_SceneAssetAssignments_AssignedAtUtc");

            entity.HasOne(e => e.Scene)
                .WithMany()
                .HasForeignKey(e => e.SceneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SceneAssetAssignments_Scenes");
            entity.HasOne(e => e.ProjectAsset)
                .WithMany(e => e.SceneAssignments)
                .HasForeignKey(e => e.ProjectAssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SceneAssetAssignments_ProjectAssets");
        });

        modelBuilder.Entity<ProviderRequestAssetVersion>(entity =>
        {
            entity.ToTable("ProviderRequestAssetVersions", "vf");

            entity.HasKey(e => new { e.ProviderRequestId, e.ProjectAssetVersionId });
            entity.HasIndex(e => e.ProjectAssetVersionId, "IX_ProviderRequestAssetVersions_AssetVersion");

            entity.HasOne(e => e.ProviderRequest)
                .WithMany()
                .HasForeignKey(e => e.ProviderRequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProviderRequestAssetVersions_ProviderRequests");
            entity.HasOne(e => e.ProjectAssetVersion)
                .WithMany(e => e.ProviderRequestSnapshots)
                .HasForeignKey(e => e.ProjectAssetVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProviderRequestAssetVersions_ProjectAssetVersions");
        });

        modelBuilder.Entity<RenderJob>(entity =>
        {
            entity.ToTable("RenderJobs", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.Version }, "UQ_RenderJobs_Project_Version").IsUnique();

            entity.Property(e => e.RenderJobId).HasDefaultValueSql("(newsequentialid())", "DF_RenderJobs_Id");
            entity.Property(e => e.CompletedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_RenderJobs_CreatedAtUtc");
            entity.Property(e => e.ErrorCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
            entity.Property(e => e.FfmpegVersion).HasMaxLength(200);
            entity.Property(e => e.ManifestHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.ProgressPercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.StartedAtUtc).HasPrecision(3);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Pending", "DF_RenderJobs_Status");

            entity.HasOne(d => d.Job).WithMany(p => p.RenderJobs)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_RenderJobs_Jobs");

            entity.HasOne(d => d.OutputMediaAsset).WithMany(p => p.RenderJobs)
                .HasForeignKey(d => d.OutputMediaAssetId)
                .HasConstraintName("FK_RenderJobs_OutputAsset");

            entity.HasOne(d => d.Project).WithMany(p => p.RenderJobs)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RenderJobs_Projects");
        });

        modelBuilder.Entity<Scene>(entity =>
        {
            entity.ToTable("Scenes", "vf");

            entity.HasIndex(e => e.GenerationDependencySceneId, "IX_Scenes_Dependency").HasFilter("([GenerationDependencySceneId] IS NOT NULL)");

            entity.HasIndex(e => new { e.ProjectId, e.Status, e.SequenceNumber }, "IX_Scenes_Project_Status_Sequence");

            entity.HasIndex(e => new { e.ProjectId, e.ScenePlanVersion, e.SequenceNumber }, "UQ_Scenes_Project_Plan_Sequence").IsUnique();

            entity.Property(e => e.SceneId).HasDefaultValueSql("(newsequentialid())", "DF_Scenes_Id");
            entity.Property(e => e.CameraDirection).HasMaxLength(2000);
            entity.Property(e => e.ContinuityGroupKey)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Scenes_CreatedAtUtc");
            entity.Property(e => e.Emotion).HasMaxLength(1000);
            entity.Property(e => e.LastErrorCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.LastErrorMessage).HasMaxLength(4000);
            entity.Property(e => e.Lighting).HasMaxLength(2000);
            entity.Property(e => e.LocationKey)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.Motion).HasMaxLength(2000);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Pending", "DF_Scenes_Status");
            entity.Property(e => e.StoryBeatId)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.StoryPurpose).HasMaxLength(1000);
            entity.Property(e => e.TransitionAfter).HasMaxLength(1000);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Scenes_UpdatedAtUtc");

            entity.HasOne(d => d.ApprovedGeneration).WithMany(p => p.Scenes)
                .HasForeignKey(d => d.ApprovedGenerationId)
                .HasConstraintName("FK_Scenes_ApprovedGeneration");

            entity.HasOne(d => d.GenerationDependencyScene).WithMany(p => p.InverseGenerationDependencyScene)
                .HasForeignKey(d => d.GenerationDependencySceneId)
                .HasConstraintName("FK_Scenes_DependencyScene");

            entity.HasOne(d => d.NextScene).WithMany(p => p.InverseNextScene)
                .HasForeignKey(d => d.NextSceneId)
                .HasConstraintName("FK_Scenes_NextScene");

            entity.HasOne(d => d.PreviousScene).WithMany(p => p.InversePreviousScene)
                .HasForeignKey(d => d.PreviousSceneId)
                .HasConstraintName("FK_Scenes_PreviousScene");

            entity.HasOne(d => d.Project).WithMany(p => p.Scenes)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Scenes_Projects");

            entity.HasOne(d => d.Script).WithMany(p => p.Scenes)
                .HasForeignKey(d => d.ScriptId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Scenes_Scripts");

            entity.HasOne(d => d.StyleProfile).WithMany(p => p.Scenes)
                .HasForeignKey(d => d.StyleProfileId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Scenes_StyleProfiles");
        });

        modelBuilder.Entity<ScenePrompt>(entity =>
        {
            entity.ToTable("ScenePrompts", "vf");

            entity.HasIndex(e => new { e.SceneId, e.Version }, "UQ_ScenePrompts_Scene_Version").IsUnique();

            entity.Property(e => e.ScenePromptId).HasDefaultValueSql("(newsequentialid())", "DF_ScenePrompts_Id");
            entity.Property(e => e.ApprovedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ScenePrompts_CreatedAtUtc");
            entity.Property(e => e.ModelCode).HasMaxLength(200);
            entity.Property(e => e.PromptHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.PromptTemplateName)
                .HasMaxLength(150)
                .IsUnicode(false);
            entity.Property(e => e.PromptTemplateVersion)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Draft", "DF_ScenePrompts_Status");

            entity.HasOne(d => d.Scene).WithMany(p => p.ScenePrompts)
                .HasForeignKey(d => d.SceneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ScenePrompts_Scenes");
        });

        modelBuilder.Entity<SchemaVersion>(entity =>
        {
            entity.HasKey(e => e.SchemaVersionId).HasName("PK_AuthSchemaVersions");

            entity.ToTable("SchemaVersions", "auth");

            entity.HasIndex(e => e.Version, "UQ_AuthSchemaVersions_Version").IsUnique();

            entity.Property(e => e.AppliedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_AuthSchemaVersions_AppliedAtUtc");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Version)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<SchemaVersion1>(entity =>
        {
            entity.HasKey(e => e.SchemaVersionId);

            entity.ToTable("SchemaVersions", "vf");

            entity.HasIndex(e => e.Version, "UQ_SchemaVersions_Version").IsUnique();

            entity.Property(e => e.AppliedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_SchemaVersions_AppliedAtUtc");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Version)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Script>(entity =>
        {
            entity.ToTable("Scripts", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.Version }, "UQ_Scripts_Project_Version").IsUnique();

            entity.Property(e => e.ScriptId).HasDefaultValueSql("(newsequentialid())", "DF_Scripts_Id");
            entity.Property(e => e.ApprovedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Scripts_CreatedAtUtc");
            entity.Property(e => e.ModelCode).HasMaxLength(200);
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.QualityScore).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Draft", "DF_Scripts_Status");
            entity.Property(e => e.StructureType)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.Title).HasMaxLength(500);

            entity.HasOne(d => d.Concept).WithMany(p => p.Scripts)
                .HasForeignKey(d => d.ConceptId)
                .HasConstraintName("FK_Scripts_Concepts");

            entity.HasOne(d => d.Project).WithMany(p => p.Scripts)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Scripts_Projects");
        });

        modelBuilder.Entity<ServerSetting>(entity =>
        {
            entity.ToTable("ServerSettings", "auth");

            entity.HasIndex(e => e.SettingKey, "UQ_ServerSettings_SettingKey").IsUnique();

            entity.Property(e => e.ServerSettingId).HasDefaultValueSql("(newsequentialid())", "DF_ServerSettings_Id");
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SettingKey).HasMaxLength(200);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ServerSettings_UpdatedAtUtc");
        });

        modelBuilder.Entity<SoundEffect>(entity =>
        {
            entity.ToTable("SoundEffects", "vf");

            entity.Property(e => e.SoundEffectId).HasDefaultValueSql("(newsequentialid())", "DF_SoundEffects_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_SoundEffects_CreatedAtUtc");
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.GainDb).HasColumnType("decimal(7, 3)");

            entity.HasOne(d => d.MediaAsset).WithMany(p => p.SoundEffects)
                .HasForeignKey(d => d.MediaAssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SoundEffects_MediaAssets");

            entity.HasOne(d => d.Project).WithMany(p => p.SoundEffects)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SoundEffects_Projects");

            entity.HasOne(d => d.Scene).WithMany(p => p.SoundEffects)
                .HasForeignKey(d => d.SceneId)
                .HasConstraintName("FK_SoundEffects_Scenes");
        });

        modelBuilder.Entity<StyleProfile>(entity =>
        {
            entity.ToTable("StyleProfiles", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.Version }, "UQ_StyleProfiles_Project_Version").IsUnique();

            entity.Property(e => e.StyleProfileId).HasDefaultValueSql("(newsequentialid())", "DF_StyleProfiles_Id");
            entity.Property(e => e.ApprovedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_StyleProfiles_CreatedAtUtc");
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Draft", "DF_StyleProfiles_Status");

            entity.HasOne(d => d.Project).WithMany(p => p.StyleProfiles)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StyleProfiles_Projects");
        });

        modelBuilder.Entity<Subtitle>(entity =>
        {
            entity.ToTable("Subtitles", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.Version, e.Format }, "UQ_Subtitles_Project_Version_Format").IsUnique();

            entity.Property(e => e.SubtitleId).HasDefaultValueSql("(newsequentialid())", "DF_Subtitles_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Subtitles_CreatedAtUtc");
            entity.Property(e => e.Format)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.LanguageCode)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Ready", "DF_Subtitles_Status");

            entity.HasOne(d => d.MediaAsset).WithMany(p => p.Subtitles)
                .HasForeignKey(d => d.MediaAssetId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Subtitles_MediaAssets");

            entity.HasOne(d => d.Project).WithMany(p => p.Subtitles)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Subtitles_Projects");

            entity.HasOne(d => d.VoiceGeneration).WithMany(p => p.Subtitles)
                .HasForeignKey(d => d.VoiceGenerationId)
                .HasConstraintName("FK_Subtitles_VoiceGenerations");
        });

        modelBuilder.Entity<UsageCost>(entity =>
        {
            entity.ToTable("UsageCosts", "vf");

            entity.HasIndex(e => new { e.ProjectId, e.OccurredAtUtc }, "IX_UsageCosts_Project_OccurredAt").IsDescending(false, true);

            entity.HasIndex(e => e.UsageKey, "UQ_UsageCosts_UsageKey").IsUnique();

            entity.Property(e => e.UsageCostId).HasDefaultValueSql("(newsequentialid())", "DF_UsageCosts_Id");
            entity.Property(e => e.CostKind)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UsageCosts_CreatedAtUtc");
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength()
                .HasDefaultValue("USD", "DF_UsageCosts_Currency");
            entity.Property(e => e.ModelCode).HasMaxLength(200);
            entity.Property(e => e.OccurredAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UsageCosts_OccurredAtUtc");
            entity.Property(e => e.ProviderCode)
                .HasMaxLength(80)
                .IsUnicode(false);
            entity.Property(e => e.Quantity).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.TotalCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.Unit)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(19, 8)");
            entity.Property(e => e.UsageType)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Job).WithMany(p => p.UsageCosts)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_UsageCosts_Jobs");

            entity.HasOne(d => d.Project).WithMany(p => p.UsageCosts)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UsageCosts_Projects");

            entity.HasOne(d => d.ProviderRequest).WithMany(p => p.UsageCosts)
                .HasForeignKey(d => d.ProviderRequestId)
                .HasConstraintName("FK_UsageCosts_ProviderRequests");

            entity.HasOne(d => d.Scene).WithMany(p => p.UsageCosts)
                .HasForeignKey(d => d.SceneId)
                .HasConstraintName("FK_UsageCosts_Scenes");
        });

        modelBuilder.Entity<UserLicense>(entity =>
        {
            entity.ToTable("UserLicenses", "auth");

            entity.HasIndex(e => new { e.UserId, e.Status, e.ExpiresAtUtc }, "IX_UserLicenses_User_Status");

            entity.HasIndex(e => e.LicenseKeyHash, "UX_UserLicenses_LicenseKeyHash")
                .IsUnique()
                .HasFilter("([LicenseKeyHash] IS NOT NULL)");

            entity.Property(e => e.UserLicenseId).HasDefaultValueSql("(newsequentialid())", "DF_UserLicenses_Id");
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UserLicenses_CreatedAtUtc");
            entity.Property(e => e.ExpiresAtUtc).HasPrecision(3);
            entity.Property(e => e.GrantedByUserId).HasMaxLength(450);
            entity.Property(e => e.LicenseKeyHash)
                .HasMaxLength(32)
                .IsFixedLength();
            entity.Property(e => e.RevokedAtUtc).HasPrecision(3);
            entity.Property(e => e.RevokedReason).HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.StartsAtUtc).HasPrecision(3);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active", "DF_UserLicenses_Status");
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UserLicenses_UpdatedAtUtc");

            entity.HasOne(d => d.GrantedByUser).WithMany(p => p.UserLicenseGrantedByUsers)
                .HasForeignKey(d => d.GrantedByUserId)
                .HasConstraintName("FK_UserLicenses_GrantedBy");

            entity.HasOne(d => d.LicensePlan).WithMany(p => p.UserLicenses)
                .HasForeignKey(d => d.LicensePlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserLicenses_LicensePlans");

            entity.HasOne(d => d.User).WithMany(p => p.UserLicenseUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserLicenses_AspNetUsers");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.SessionId);

            entity.ToTable("UserSessions", "auth");

            entity.HasIndex(e => new { e.Status, e.AbsoluteExpiresAtUtc }, "IX_UserSessions_Expiry");

            entity.HasIndex(e => new { e.UserId, e.Status, e.LastSeenAtUtc }, "IX_UserSessions_User_Status").IsDescending(false, false, true);

            entity.Property(e => e.SessionId).HasDefaultValueSql("(newsequentialid())", "DF_UserSessions_Id");
            entity.Property(e => e.AbsoluteExpiresAtUtc).HasPrecision(3);
            entity.Property(e => e.ApplicationVersion).HasMaxLength(50);
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .IsUnicode(false);
            entity.Property(e => e.LastSeenAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UserSessions_LastSeenAtUtc");
            entity.Property(e => e.RevokedAtUtc).HasPrecision(3);
            entity.Property(e => e.RevokedReason).HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.StartedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UserSessions_StartedAtUtc");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active", "DF_UserSessions_Status");
            entity.Property(e => e.UserAgent).HasMaxLength(1000);

            entity.HasOne(d => d.Device).WithMany(p => p.UserSessions)
                .HasForeignKey(d => d.DeviceId)
                .HasConstraintName("FK_UserSessions_RegisteredDevices");

            entity.HasOne(d => d.User).WithMany(p => p.UserSessions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserSessions_AspNetUsers");
        });

        modelBuilder.Entity<VideoGeneration>(entity =>
        {
            entity.ToTable("VideoGenerations", "vf");

            entity.HasIndex(e => new { e.SceneId, e.Status, e.AttemptNumber }, "IX_VideoGenerations_Scene_Status").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.SceneId, e.AttemptNumber }, "UQ_VideoGenerations_Scene_Attempt").IsUnique();

            entity.Property(e => e.VideoGenerationId).HasDefaultValueSql("(newsequentialid())", "DF_VideoGenerations_Id");
            entity.Property(e => e.CompletedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_VideoGenerations_CreatedAtUtc");
            entity.Property(e => e.QualityScore).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Pending", "DF_VideoGenerations_Status");

            entity.HasOne(d => d.Job).WithMany(p => p.VideoGenerations)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_VideoGenerations_Jobs");

            entity.HasOne(d => d.OutputMediaAsset).WithMany(p => p.VideoGenerations)
                .HasForeignKey(d => d.OutputMediaAssetId)
                .HasConstraintName("FK_VideoGenerations_OutputAsset");

            entity.HasOne(d => d.ProviderRequest).WithMany(p => p.VideoGenerations)
                .HasForeignKey(d => d.ProviderRequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VideoGenerations_ProviderRequests");

            entity.HasOne(d => d.Scene).WithMany(p => p.VideoGenerations)
                .HasForeignKey(d => d.SceneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VideoGenerations_Scenes");

            entity.HasOne(d => d.ScenePrompt).WithMany(p => p.VideoGenerations)
                .HasForeignKey(d => d.ScenePromptId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VideoGenerations_ScenePrompts");
        });

        modelBuilder.Entity<VoiceGeneration>(entity =>
        {
            entity.ToTable("VoiceGenerations", "vf");

            entity.HasIndex(e => new { e.SceneId, e.Version }, "UX_VoiceGenerations_Scene_Version")
                .IsUnique()
                .HasFilter("([SceneId] IS NOT NULL)");
            entity.HasIndex(e => new { e.SceneId, e.NarrationHash, e.CreatedAtUtc }, "IX_VoiceGenerations_Scene_NarrationHash")
                .IsDescending(false, false, true)
                .HasFilter("([SceneId] IS NOT NULL AND [NarrationHash] IS NOT NULL)");

            entity.Property(e => e.VoiceGenerationId).HasDefaultValueSql("(newsequentialid())", "DF_VoiceGenerations_Id");
            entity.Property(e => e.CompletedAtUtc).HasPrecision(3);
            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_VoiceGenerations_CreatedAtUtc");
            entity.Property(e => e.LanguageCode)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.NarrationHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.ProviderVoiceCode).HasMaxLength(100);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SpeakingRate)
                .HasDefaultValue(1m, "DF_VoiceGenerations_Rate")
                .HasColumnType("decimal(6, 3)");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("Pending", "DF_VoiceGenerations_Status");
            entity.Property(e => e.VoiceCode).HasMaxLength(200);

            entity.HasOne(d => d.Scene).WithMany(p => p.VoiceGenerations)
                .HasForeignKey(d => d.SceneId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_VoiceGenerations_Scenes");

            entity.HasOne(d => d.OutputMediaAsset).WithMany(p => p.VoiceGenerations)
                .HasForeignKey(d => d.OutputMediaAssetId)
                .HasConstraintName("FK_VoiceGenerations_OutputAsset");

            entity.HasOne(d => d.Project).WithMany(p => p.VoiceGenerations)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VoiceGenerations_Projects");

            entity.HasOne(d => d.ProviderRequest).WithMany(p => p.VoiceGenerations)
                .HasForeignKey(d => d.ProviderRequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VoiceGenerations_ProviderRequests");

            entity.HasOne(d => d.Script).WithMany(p => p.VoiceGenerations)
                .HasForeignKey(d => d.ScriptId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VoiceGenerations_Scripts");
        });

        modelBuilder.Entity<VwProjectProgress>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_ProjectProgress", "vf");

            entity.Property(e => e.ActualCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.BudgetLimit).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.CreatedAtUtc).HasPrecision(3);
            entity.Property(e => e.CurrencyCode)
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.EstimatedCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.Name).HasMaxLength(300);
            entity.Property(e => e.RemoteUserId).HasMaxLength(450);
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .IsUnicode(false);
            entity.Property(e => e.Topic).HasMaxLength(2000);
            entity.Property(e => e.UpdatedAtUtc).HasPrecision(3);
        });

        modelBuilder.Entity<VwUserAccountSummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_UserAccountSummary", "auth");

            entity.Property(e => e.AccountStatus)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAtUtc).HasPrecision(3);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.LastLoginAtUtc).HasPrecision(3);
            entity.Property(e => e.NearestLicenseExpiryUtc).HasPrecision(3);
            entity.Property(e => e.UpdatedAtUtc).HasPrecision(3);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.UserName).HasMaxLength(256);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
