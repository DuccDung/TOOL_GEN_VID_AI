using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Data.Models;

namespace TOOL_LOCAL.Data;

public partial class VideoFactoryDbContext : DbContext
{
    public VideoFactoryDbContext(DbContextOptions<VideoFactoryDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AppSetting> AppSettings { get; set; }

    public virtual DbSet<Approval> Approvals { get; set; }

    public virtual DbSet<Character> Characters { get; set; }

    public virtual DbSet<CharacterReference> CharacterReferences { get; set; }

    public virtual DbSet<Concept> Concepts { get; set; }

    public virtual DbSet<CostRate> CostRates { get; set; }

    public virtual DbSet<FinalVideo> FinalVideos { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<JobDependency> JobDependencies { get; set; }

    public virtual DbSet<JobEvent> JobEvents { get; set; }

    public virtual DbSet<MediaAsset> MediaAssets { get; set; }

    public virtual DbSet<MusicAsset> MusicAssets { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<Provider> Providers { get; set; }

    public virtual DbSet<ProviderModel> ProviderModels { get; set; }

    public virtual DbSet<ProviderRequest> ProviderRequests { get; set; }

    public virtual DbSet<RenderJob> RenderJobs { get; set; }

    public virtual DbSet<Scene> Scenes { get; set; }

    public virtual DbSet<ScenePrompt> ScenePrompts { get; set; }

    public virtual DbSet<SchemaVersion> SchemaVersions { get; set; }

    public virtual DbSet<Script> Scripts { get; set; }

    public virtual DbSet<SoundEffect> SoundEffects { get; set; }

    public virtual DbSet<StyleProfile> StyleProfiles { get; set; }

    public virtual DbSet<Subtitle> Subtitles { get; set; }

    public virtual DbSet<UsageCost> UsageCosts { get; set; }

    public virtual DbSet<VideoGeneration> VideoGenerations { get; set; }

    public virtual DbSet<VoiceGeneration> VoiceGenerations { get; set; }

    public virtual DbSet<VwProjectProgress> VwProjectProgresses { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

            entity.HasIndex(e => new { e.RemoteUserId, e.Status, e.UpdatedAtUtc }, "IX_Projects_RemoteUser_Status")
                .IsDescending(false, false, true)
                .HasFilter("([RemoteUserId] IS NOT NULL)");

            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.UpdatedAtUtc }, "IX_Projects_Organization_Status")
                .IsDescending(false, false, true)
                .HasFilter("([OrganizationId] IS NOT NULL)");

            entity.HasIndex(e => new { e.Status, e.UpdatedAtUtc }, "IX_Projects_Status_UpdatedAt").IsDescending(false, true);

            entity.Property(e => e.ProjectId).HasDefaultValueSql("(newsequentialid())", "DF_Projects_Id");
            entity.Property(e => e.CreatedByUserId).HasMaxLength(450);
            entity.Property(e => e.ActualCost).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.AspectRatio)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("9:16", "DF_Projects_AspectRatio");
            entity.Property(e => e.BudgetLimit).HasColumnType("decimal(19, 6)");
            entity.Property(e => e.CompletedAtUtc).HasPrecision(3);
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

            entity.HasIndex(e => e.IdempotencyKey, "UQ_ProviderRequests_IdempotencyKey").IsUnique();

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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
