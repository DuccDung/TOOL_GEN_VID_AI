/* Personal Local registry: NULL OrganizationId means user-owned Local workspace.
   Apply to the server database after verifying instance, backup and restore.
   Existing organization projects and all foreign keys remain intact. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'vs.Projects', N'U') IS NULL
        OR OBJECT_ID(N'auth.AccountAuditLogs', N'U') IS NULL
        OR OBJECT_ID(N'ai.SchemaVersions', N'U') IS NULL
        THROW 51116, 'Vietsub registry, account audit and schema version tables are required.', 1;
    IF COL_LENGTH(N'vs.Projects', N'OrganizationId') IS NULL
        THROW 51117, 'Vietsub Projects.OrganizationId column is required.', 1;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'vs.Projects')
               AND name = N'OrganizationId' AND is_nullable = 0)
        ALTER TABLE [vs].[Projects] ALTER COLUMN [OrganizationId] uniqueidentifier NULL;

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.16-vietsub-personal-projects')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES ('4.1.16-vietsub-personal-projects', N'Allow user-owned Local projects without an organization; preserve existing ownership and foreign keys.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
