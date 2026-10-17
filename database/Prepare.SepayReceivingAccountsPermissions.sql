/* DBA-only preparation for a restricted migration account.
   Run in the explicitly approved target database after backup/restore verification.
   Set the actual database USER (not necessarily the login) below. No secrets here.
   Grants metadata visibility only; does not elevate the migration user to db_owner. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabase sysname = N'<approved-database>';
DECLARE @MigrationUser sysname = N'<migration-database-user>';
IF DB_NAME() <> @ExpectedDatabase OR @MigrationUser LIKE N'<%>'
    THROW 51125, 'Set and verify the approved target database and migration database user before execution.', 1;
IF DATABASE_PRINCIPAL_ID(@MigrationUser) IS NULL OR SCHEMA_ID(N'auth') IS NULL OR
   DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NULL
    THROW 51126, 'Required migration user, auth schema or desktop role is missing.', 1;
IF ISNULL(IS_ROLEMEMBER(N'VideoMakerDesktopRole', @MigrationUser), -1) <> 0
    THROW 51127, 'The migration/server user must not be a desktop role member.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[auth] TO [VideoMakerDesktopRole];
    DECLARE @GrantMetadata nvarchar(max) =
        N'GRANT VIEW DEFINITION ON ROLE::[VideoMakerDesktopRole] TO ' + QUOTENAME(@MigrationUser) + N';' +
        N'GRANT VIEW DEFINITION ON SCHEMA::[auth] TO ' + QUOTENAME(@MigrationUser) + N';';
    EXEC sys.sp_executesql @GrantMetadata;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
