using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Updates;

namespace TOOL_TESTS.Updates;

// Offline schema-contract checks. These do not claim this migration has run on SQL Server.
public sealed class DesktopDirectCatalogMigrationTests
{
    [Fact]
    public void Catalog_mapping_has_declared_identity_without_inventing_measured_archive_metadata()
    {
        using var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>().UseSqlite("Data Source=:memory:").Options);
        var entity = db.Model.FindEntityType(typeof(DesktopDirectRelease))!;
        Assert.Equal("auth", entity.GetSchema()); Assert.Equal("DesktopDirectReleases", entity.GetTableName());
        var hash = entity.FindProperty(nameof(DesktopDirectRelease.ExpectedSha256))!;
        Assert.False(hash.IsNullable); Assert.Equal(64, hash.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(DesktopDirectRelease.PublishedAtUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DesktopDirectRelease.FirstPublishedAtUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DesktopDirectRelease.Revision))!.IsConcurrencyToken);
        Assert.DoesNotContain(entity.GetProperties(), x => x.Name is "SizeBytes" or "VerifiedAtUtc" or "NativeBuildNumber" or "VerifiedPreparationId");
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["Profile", "Channel", "Platform", "Version"]));
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["ActorUserId", "ClientRequestId"]));
    }

    [Fact]
    public void Additive_migration_preserves_the_legacy_proof_tables_and_constraints()
    {
        var source = ReadMigration();
        Assert.DoesNotMatch(@"(?is)\b(?:ALTER|DROP)\s+TABLE\s+(?:\[?auth\]?\.)?\[?(?:AppReleases|AppReleaseArtifacts|DesktopReleasePreparations|DesktopBootstrappers)\b", source);
        Assert.DoesNotContain("VerifiedPreparationId", source);
        Assert.DoesNotContain("InspectionEvidenceJson", source);
        Assert.DoesNotContain("SizeBytes", source);
        Assert.Contains("4.1.18-shared-desktop-bootstrapper", source);
        Assert.Contains("[CatalogProtocol]=4 AND [ApplicationDistributionProtocol]=3", source);
    }

    [Fact]
    public void Existing_schema_and_desktop_access_protection_are_checked_before_marking_migration_complete()
    {
        var source = ReadMigration();
        var marker = source.IndexOf("INSERT ai.SchemaVersions", StringComparison.Ordinal);
        Assert.True(marker > 0);
        foreach (var guard in new[] { "IF @@TRANCOUNT <> 0", "SET XACT_ABORT ON", "c.system_type_id", "c.is_nullable", "c.scale<>3",
                     "is_not_trusted=0", "Latin1_General_100_BIN2", "i.is_disabled=0", "i.is_primary_key=1", "VideoMakerDesktopRole",
                     "DENY SELECT,INSERT,UPDATE,DELETE" })
        {
            var index = source.IndexOf(guard, StringComparison.Ordinal);
            Assert.True(index >= 0 && index < marker, $"Required schema/access guard must precede the schema version: {guard}");
        }
        Assert.Contains("ROLLBACK TRANSACTION", source);
        Assert.Contains("IF NOT EXISTS(SELECT 1 FROM ai.SchemaVersions", source);
    }

    private static string ReadMigration()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "database", "VideoFactory.4.1.19.DirectDesktopCatalog.sql");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("Direct catalog migration was not found in the source checkout.");
    }
}
