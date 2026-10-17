-- Apply only to an explicitly approved server database after backup/restore verification.
-- No provider price, key, payment setting or commercial allocation is seeded.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF SCHEMA_ID(N'auth') IS NULL THROW 51000, 'Missing auth schema. Apply prerequisite migrations first.', 1;
IF OBJECT_ID(N'auth.LicensePlans', N'U') IS NULL OR OBJECT_ID(N'ai.OrganizationPools', N'U') IS NULL OR OBJECT_ID(N'ai.SchemaVersions', N'U') IS NULL
    THROW 51000, 'Missing license/provisioning prerequisites.', 1;
IF OBJECT_ID(N'auth.AdminSetupDrafts', N'U') IS NULL
BEGIN
    CREATE TABLE auth.AdminSetupDrafts (
        DraftId uniqueidentifier NOT NULL CONSTRAINT PK_AdminSetupDrafts PRIMARY KEY,
        ActorUserId nvarchar(450) NOT NULL,
        OrganizationId uniqueidentifier NULL,
        PoolId uniqueidentifier NULL,
        State varchar(30) NOT NULL CONSTRAINT DF_AdminSetupDrafts_State DEFAULT 'Draft',
        Step int NOT NULL CONSTRAINT DF_AdminSetupDrafts_Step DEFAULT 1,
        Revision bigint NOT NULL CONSTRAINT DF_AdminSetupDrafts_Revision DEFAULT 1,
        FormJson nvarchar(max) NOT NULL,
        ConfigurationFingerprint varchar(64) NULL,
        PlanId uniqueidentifier NULL,
        CreatedAtUtc datetime2(3) NOT NULL,
        UpdatedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT CK_AdminSetupDrafts_Step CHECK (Step BETWEEN 1 AND 3),
        CONSTRAINT CK_AdminSetupDrafts_Json CHECK (ISJSON(FormJson) = 1),
        CONSTRAINT CK_AdminSetupDrafts_State CHECK (State IN ('Draft','ConnectionsSaved','ConfigurationSaved','PublicationBlocked','Published','PlanDraftSaved','Cancelled')),
        CONSTRAINT FK_AdminSetupDrafts_User FOREIGN KEY (ActorUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_AdminSetupDrafts_Organization FOREIGN KEY (OrganizationId) REFERENCES ai.Organizations(OrganizationId),
        CONSTRAINT FK_AdminSetupDrafts_Pool FOREIGN KEY (PoolId) REFERENCES ai.OrganizationPools(OrganizationPoolId),
        CONSTRAINT FK_AdminSetupDrafts_Plan FOREIGN KEY (PlanId) REFERENCES auth.LicensePlans(LicensePlanId)
    );
    CREATE INDEX IX_AdminSetupDrafts_ActorUserId_UpdatedAtUtc ON auth.AdminSetupDrafts(ActorUserId, UpdatedAtUtc);
END;
IF OBJECT_ID(N'auth.AdminSetupOperations', N'U') IS NULL
BEGIN
    CREATE TABLE auth.AdminSetupOperations (
        OperationId uniqueidentifier NOT NULL CONSTRAINT PK_AdminSetupOperations PRIMARY KEY,
        DraftId uniqueidentifier NOT NULL,
        ActorUserId nvarchar(450) NOT NULL,
        Command varchar(30) NOT NULL,
        RequestHash varchar(64) NOT NULL,
        State varchar(30) NOT NULL,
        ResultId uniqueidentifier NULL,
        ErrorCode varchar(100) NULL,
        LeaseExpiresAtUtc datetime2(3) NULL,
        UpdatedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT FK_AdminSetupOperations_Draft FOREIGN KEY (DraftId) REFERENCES auth.AdminSetupDrafts(DraftId),
        CONSTRAINT CK_AdminSetupOperations_State CHECK (State IN ('Running','Succeeded','Failed'))
    );
END;
-- Keep existing server grants. Explicitly protect new objects for the established desktop role.
IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
BEGIN
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::auth.AdminSetupDrafts TO VideoMakerDesktopRole;
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::auth.AdminSetupOperations TO VideoMakerDesktopRole;
END;
IF NOT EXISTS (SELECT 1 FROM ai.SchemaVersions WHERE Version = '4.1.14-admin-setup-wizard')
    INSERT INTO ai.SchemaVersions(Version, Description) VALUES ('4.1.14-admin-setup-wizard', N'Bản nháp an toàn và receipt cho thiết lập mở bán 3 bước; SePay riêng.');
COMMIT;
