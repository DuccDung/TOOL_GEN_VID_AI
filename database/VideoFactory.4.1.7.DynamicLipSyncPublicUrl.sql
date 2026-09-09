/*
    Dynamic public URL administration for the server-owned lip-sync workflow.

    The value is stored on demand in vf.AppSettings by a Global Admin. This
    migration adds no URL and enables no provider, model, rate or paid request.
    Run only after a verified backup and VideoFactory.4.1.6.LipSyncGeneration.sql.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[AppSettings]', N'U') IS NULL OR
       OBJECT_ID(N'[auth].[AccountAuditLogs]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
        THROW 51170, 'VideoFactory schema with AppSettings and audit logs is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.6-lip-sync-generation'
    )
        THROW 51171, 'Apply VideoFactory.4.1.6.LipSyncGeneration.sql before dynamic lip-sync URL migration.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.7-dynamic-lip-sync-public-url'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.7-dynamic-lip-sync-public-url',
            N'Global Admin-managed public HTTPS root URL for signed Fal lip-sync inputs.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

/* vf.AppSettings is server-owned even though the transitional Desktop role
   otherwise has broad DML on the vf workflow schema. */
IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[AppSettings]
        TO [VideoMakerDesktopRole];
GO

IF NOT EXISTS
(
    SELECT 1 FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.1.7-dynamic-lip-sync-public-url'
)
    THROW 51172, 'Dynamic lip-sync public URL migration verification failed.', 1;

PRINT N'VideoFactory dynamic lip-sync public URL schema 4.1.7 is ready.';
GO
