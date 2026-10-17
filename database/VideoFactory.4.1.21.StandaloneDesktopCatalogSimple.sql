/* Extend the existing 4.1.20 catalog to standalone Setup protocol 5 alongside legacy protocol 4.
   Only the scope CHECK and this version marker change. Existing rows and permissions stay unchanged.
   Run in the approved application database after backup/restore review. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT <> 0 THROW 51230, 'Run this migration in its own session.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'ai.SchemaVersions',N'U') IS NULL OR OBJECT_ID(N'auth.DesktopDirectReleases',N'U') IS NULL
        OR NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.20-direct-desktop-catalog-simple')
        THROW 51231, 'Verify and apply the complete 4.1.20 catalog schema first.', 1;
    DECLARE @object int=OBJECT_ID(N'auth.DesktopDirectReleases');
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@object)<>24 OR EXISTS(
        SELECT 1 FROM (VALUES(N'CatalogProtocol',N'int',4),(N'ApplicationDistributionProtocol',N'int',4),
            (N'Channel',N'varchar',20),(N'Platform',N'varchar',20),(N'Profile',N'varchar',50),(N'ModelBundleId',N'varchar',100)) e([Name],TypeName,MaxLength)
        LEFT JOIN sys.columns c ON c.object_id=@object AND c.name=e.[Name]
        WHERE c.column_id IS NULL OR c.system_type_id<>TYPE_ID(e.TypeName) OR c.max_length<>e.MaxLength
            OR c.is_nullable=1 OR c.is_computed=1 OR c.is_identity=1)
        THROW 51232, 'Unexpected catalog scope columns; inspect the existing schema.', 1;
    DECLARE @legacy nvarchar(2000)=N'([CatalogProtocol]=(4) AND [ApplicationDistributionProtocol]=(3) AND [Channel]=''Stable'' AND [Platform]=''win-x64'' AND len([Profile])>(0) AND len([ModelBundleId])>(0))';
    DECLARE @standalone nvarchar(2000)=N'(([CatalogProtocol]=(4) OR [CatalogProtocol]=(5)) AND [ApplicationDistributionProtocol]=(3) AND [Channel]=''Stable'' AND [Platform]=''win-x64'' AND len([Profile])>(0) AND len([ModelBundleId])>(0))';
    DECLARE @actual nvarchar(max);
    SELECT @actual=definition FROM sys.check_constraints WHERE parent_object_id=@object AND name=N'CK_DesktopDirectReleases_Scope'
        AND is_disabled=0 AND is_not_trusted=0 AND is_not_for_replication=0;
    IF @actual IS NULL OR (@actual COLLATE Latin1_General_100_BIN2<>@legacy COLLATE Latin1_General_100_BIN2
        AND @actual COLLATE Latin1_General_100_BIN2<>@standalone COLLATE Latin1_General_100_BIN2)
        THROW 51233, 'Unexpected or untrusted existing catalog scope CHECK.', 1;
    IF @actual COLLATE Latin1_General_100_BIN2=@legacy COLLATE Latin1_General_100_BIN2
    BEGIN
        ALTER TABLE auth.DesktopDirectReleases DROP CONSTRAINT CK_DesktopDirectReleases_Scope;
        ALTER TABLE auth.DesktopDirectReleases WITH CHECK ADD CONSTRAINT CK_DesktopDirectReleases_Scope CHECK
            (([CatalogProtocol]=4 OR [CatalogProtocol]=5) AND [ApplicationDistributionProtocol]=3 AND [Channel]='Stable'
                AND [Platform]='win-x64' AND LEN([Profile])>0 AND LEN([ModelBundleId])>0);
    END;
    IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=@object AND name=N'CK_DesktopDirectReleases_Scope'
        AND is_disabled=0 AND is_not_trusted=0 AND is_not_for_replication=0
        AND definition COLLATE Latin1_General_100_BIN2=@standalone COLLATE Latin1_General_100_BIN2)
        THROW 51234, 'Standalone catalog scope verification failed.', 1;
    IF NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.21-standalone-desktop-catalog-simple')
        INSERT ai.SchemaVersions([Version],[Description]) VALUES('4.1.21-standalone-desktop-catalog-simple',
            N'Standalone Setup catalog protocol 5 alongside legacy protocol 4; existing data and permissions unchanged.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
