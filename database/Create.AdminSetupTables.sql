-- Tao hai bang thiet lap Admin trong database hosting.
-- Chay lai giu nguyen cac bang va du lieu da co.
USE [tap51004_tool];

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'auth.AdminSetupDrafts', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[AdminSetupDrafts]
        (
            [DraftId] uniqueidentifier NOT NULL CONSTRAINT [PK_AdminSetupDrafts] PRIMARY KEY,
            [ActorUserId] nvarchar(450) NOT NULL,
            [OrganizationId] uniqueidentifier NULL,
            [PoolId] uniqueidentifier NULL,
            [State] varchar(30) NOT NULL CONSTRAINT [DF_AdminSetupDrafts_State] DEFAULT 'Draft',
            [Step] int NOT NULL CONSTRAINT [DF_AdminSetupDrafts_Step] DEFAULT 1,
            [Revision] bigint NOT NULL CONSTRAINT [DF_AdminSetupDrafts_Revision] DEFAULT 1,
            [FormJson] nvarchar(max) NOT NULL,
            [ConfigurationFingerprint] varchar(64) NULL,
            [PlanId] uniqueidentifier NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,

            CONSTRAINT [CK_AdminSetupDrafts_Step] CHECK ([Step] BETWEEN 1 AND 3),
            CONSTRAINT [CK_AdminSetupDrafts_Json] CHECK (ISJSON([FormJson]) = 1),
            CONSTRAINT [CK_AdminSetupDrafts_State] CHECK
                ([State] IN ('Draft', 'ConnectionsSaved', 'ConfigurationSaved', 'PublicationBlocked', 'Published', 'PlanDraftSaved', 'Cancelled')),
            CONSTRAINT [FK_AdminSetupDrafts_User] FOREIGN KEY ([ActorUserId])
                REFERENCES [dbo].[AspNetUsers] ([Id]),
            CONSTRAINT [FK_AdminSetupDrafts_Organization] FOREIGN KEY ([OrganizationId])
                REFERENCES [ai].[Organizations] ([OrganizationId]),
            CONSTRAINT [FK_AdminSetupDrafts_Pool] FOREIGN KEY ([PoolId])
                REFERENCES [ai].[OrganizationPools] ([OrganizationPoolId]),
            CONSTRAINT [FK_AdminSetupDrafts_Plan] FOREIGN KEY ([PlanId])
                REFERENCES [auth].[LicensePlans] ([LicensePlanId])
        );
    END;

    IF OBJECT_ID(N'auth.AdminSetupOperations', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[AdminSetupOperations]
        (
            [OperationId] uniqueidentifier NOT NULL CONSTRAINT [PK_AdminSetupOperations] PRIMARY KEY,
            [DraftId] uniqueidentifier NOT NULL,
            [ActorUserId] nvarchar(450) NOT NULL,
            [Command] varchar(30) NOT NULL,
            [RequestHash] varchar(64) NOT NULL,
            [State] varchar(30) NOT NULL,
            [ResultId] uniqueidentifier NULL,
            [ErrorCode] varchar(100) NULL,
            [LeaseExpiresAtUtc] datetime2(3) NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,

            CONSTRAINT [FK_AdminSetupOperations_Draft] FOREIGN KEY ([DraftId])
                REFERENCES [auth].[AdminSetupDrafts] ([DraftId]),
            CONSTRAINT [CK_AdminSetupOperations_State] CHECK ([State] IN ('Running', 'Succeeded', 'Failed'))
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'auth.AdminSetupDrafts')
          AND [name] = N'IX_AdminSetupDrafts_ActorUserId_UpdatedAtUtc'
    )
        CREATE INDEX [IX_AdminSetupDrafts_ActorUserId_UpdatedAtUtc]
            ON [auth].[AdminSetupDrafts] ([ActorUserId], [UpdatedAtUtc]);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
