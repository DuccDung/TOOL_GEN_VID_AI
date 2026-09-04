/*
    Organization pool and customer-seat provisioning for VideoMaker 4.0.11.
    This migration is idempotent. It does not create public offers, enable
    organizations, configure credentials, guess AI rates, or migrate users.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[ai].[Organizations]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[OrganizationMembers]', N'U') IS NULL OR
   OBJECT_ID(N'[auth].[LicensePlans]', N'U') IS NULL OR
   OBJECT_ID(N'[auth].[LicensePayments]', N'U') IS NULL OR
   OBJECT_ID(N'[auth].[UserLicenses]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51110, 'Organization, license payment and ai.SchemaVersions tables are required before seat provisioning migration.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'ai.OrganizationMembers', N'IsProvisioningManaged') IS NULL
    BEGIN
        ALTER TABLE [ai].[OrganizationMembers]
            ADD [IsProvisioningManaged] bit NOT NULL
                CONSTRAINT [DF_OrganizationMembers_IsProvisioningManaged] DEFAULT (0);
    END;

    IF OBJECT_ID(N'[ai].[OrganizationPools]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationPools]
        (
            [OrganizationPoolId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_OrganizationPools_Id] DEFAULT NEWSEQUENTIALID(),
            [Code] varchar(50) NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [AllocationStrategy] varchar(30) NOT NULL
                CONSTRAINT [DF_OrganizationPools_AllocationStrategy] DEFAULT ('PriorityBalanced'),
            [Status] varchar(20) NOT NULL
                CONSTRAINT [DF_OrganizationPools_Status] DEFAULT ('Active'),
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationPools_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationPools_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_OrganizationPools] PRIMARY KEY CLUSTERED ([OrganizationPoolId]),
            CONSTRAINT [UQ_OrganizationPools_Code] UNIQUE ([Code]),
            CONSTRAINT [CK_OrganizationPools_AllocationStrategy]
                CHECK ([AllocationStrategy] IN ('PriorityBalanced')),
            CONSTRAINT [CK_OrganizationPools_Status]
                CHECK ([Status] IN ('Active', 'Inactive'))
        );
    END;

    IF OBJECT_ID(N'[ai].[OrganizationPoolOrganizations]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationPoolOrganizations]
        (
            [OrganizationPoolId] uniqueidentifier NOT NULL,
            [OrganizationId] uniqueidentifier NOT NULL,
            [SeatCapacity] int NOT NULL,
            [ActiveSeatCount] int NOT NULL
                CONSTRAINT [DF_OrganizationPoolOrganizations_ActiveSeats] DEFAULT (0),
            [ReservedSeatCount] int NOT NULL
                CONSTRAINT [DF_OrganizationPoolOrganizations_ReservedSeats] DEFAULT (0),
            [Priority] int NOT NULL
                CONSTRAINT [DF_OrganizationPoolOrganizations_Priority] DEFAULT (100),
            [IsAutoAssignmentEnabled] bit NOT NULL
                CONSTRAINT [DF_OrganizationPoolOrganizations_AutoAssignment] DEFAULT (0),
            [IsReady] bit NOT NULL
                CONSTRAINT [DF_OrganizationPoolOrganizations_IsReady] DEFAULT (0),
            [ReadinessMessage] nvarchar(500) NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationPoolOrganizations_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationPoolOrganizations_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_OrganizationPoolOrganizations]
                PRIMARY KEY CLUSTERED ([OrganizationPoolId], [OrganizationId]),
            CONSTRAINT [FK_OrganizationPoolOrganizations_Pools]
                FOREIGN KEY ([OrganizationPoolId]) REFERENCES [ai].[OrganizationPools]([OrganizationPoolId]),
            CONSTRAINT [FK_OrganizationPoolOrganizations_Organizations]
                FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]),
            CONSTRAINT [CK_OrganizationPoolOrganizations_Capacity]
                CHECK ([SeatCapacity] BETWEEN 1 AND 100000),
            CONSTRAINT [CK_OrganizationPoolOrganizations_Counts]
                CHECK ([ActiveSeatCount] >= 0 AND [ReservedSeatCount] >= 0 AND
                       [ActiveSeatCount] + [ReservedSeatCount] <= [SeatCapacity]),
            CONSTRAINT [CK_OrganizationPoolOrganizations_Priority]
                CHECK ([Priority] BETWEEN 0 AND 100000)
        );

        CREATE UNIQUE INDEX [UQ_OrganizationPoolOrganizations_AutoOrganization]
            ON [ai].[OrganizationPoolOrganizations]([OrganizationId])
            WHERE [IsAutoAssignmentEnabled] = 1;

        CREATE INDEX [IX_OrganizationPoolOrganizations_Allocation]
            ON [ai].[OrganizationPoolOrganizations]
                ([OrganizationPoolId], [IsAutoAssignmentEnabled], [IsReady], [Priority], [OrganizationId]);
    END;

    IF OBJECT_ID(N'[ai].[LicensePlanOrganizationPools]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[LicensePlanOrganizationPools]
        (
            [LicensePlanId] uniqueidentifier NOT NULL,
            [OrganizationPoolId] uniqueidentifier NOT NULL,
            [DefaultMemberMonthlyBudgetLimit] decimal(19,6) NULL,
            [IsActive] bit NOT NULL
                CONSTRAINT [DF_LicensePlanOrganizationPools_IsActive] DEFAULT (1),
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_LicensePlanOrganizationPools_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_LicensePlanOrganizationPools_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_LicensePlanOrganizationPools] PRIMARY KEY CLUSTERED ([LicensePlanId]),
            CONSTRAINT [FK_LicensePlanOrganizationPools_Plans]
                FOREIGN KEY ([LicensePlanId]) REFERENCES [auth].[LicensePlans]([LicensePlanId]),
            CONSTRAINT [FK_LicensePlanOrganizationPools_Pools]
                FOREIGN KEY ([OrganizationPoolId]) REFERENCES [ai].[OrganizationPools]([OrganizationPoolId]),
            CONSTRAINT [CK_LicensePlanOrganizationPools_MemberBudget]
                CHECK ([DefaultMemberMonthlyBudgetLimit] IS NULL OR [DefaultMemberMonthlyBudgetLimit] >= 0)
        );

        CREATE INDEX [IX_LicensePlanOrganizationPools_Pool]
            ON [ai].[LicensePlanOrganizationPools]([OrganizationPoolId], [IsActive]);
    END;

    IF OBJECT_ID(N'[ai].[OrganizationSeatAssignments]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationSeatAssignments]
        (
            [OrganizationSeatAssignmentId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_OrganizationSeatAssignments_Id] DEFAULT NEWSEQUENTIALID(),
            [OrganizationPoolId] uniqueidentifier NOT NULL,
            [OrganizationId] uniqueidentifier NOT NULL,
            [UserId] nvarchar(450) NOT NULL,
            [LicensePlanId] uniqueidentifier NOT NULL,
            [LicensePaymentId] uniqueidentifier NOT NULL,
            [UserLicenseId] uniqueidentifier NULL,
            [Status] varchar(20) NOT NULL
                CONSTRAINT [DF_OrganizationSeatAssignments_Status] DEFAULT ('Reserved'),
            [ConsumesSeat] bit NOT NULL
                CONSTRAINT [DF_OrganizationSeatAssignments_ConsumesSeat] DEFAULT (1),
            [MembershipManaged] bit NOT NULL
                CONSTRAINT [DF_OrganizationSeatAssignments_MembershipManaged] DEFAULT (1),
            [ReservedAtUtc] datetime2(3) NOT NULL,
            [ReservationExpiresAtUtc] datetime2(3) NOT NULL,
            [StartsAtUtc] datetime2(3) NULL,
            [EndsAtUtc] datetime2(3) NULL,
            [ActivatedAtUtc] datetime2(3) NULL,
            [ReleasedAtUtc] datetime2(3) NULL,
            [ReleaseReason] nvarchar(500) NULL,
            [FailureCode] varchar(100) NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationSeatAssignments_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationSeatAssignments_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_OrganizationSeatAssignments]
                PRIMARY KEY CLUSTERED ([OrganizationSeatAssignmentId]),
            CONSTRAINT [UQ_OrganizationSeatAssignments_Payment] UNIQUE ([LicensePaymentId]),
            CONSTRAINT [FK_OrganizationSeatAssignments_PoolOrganizations]
                FOREIGN KEY ([OrganizationPoolId], [OrganizationId])
                REFERENCES [ai].[OrganizationPoolOrganizations]([OrganizationPoolId], [OrganizationId]),
            CONSTRAINT [FK_OrganizationSeatAssignments_Users]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_OrganizationSeatAssignments_Plans]
                FOREIGN KEY ([LicensePlanId]) REFERENCES [auth].[LicensePlans]([LicensePlanId]),
            CONSTRAINT [FK_OrganizationSeatAssignments_Payments]
                FOREIGN KEY ([LicensePaymentId]) REFERENCES [auth].[LicensePayments]([LicensePaymentId]),
            CONSTRAINT [FK_OrganizationSeatAssignments_UserLicenses]
                FOREIGN KEY ([UserLicenseId]) REFERENCES [auth].[UserLicenses]([UserLicenseId]),
            CONSTRAINT [CK_OrganizationSeatAssignments_Status]
                CHECK ([Status] IN ('Reserved', 'Scheduled', 'Active', 'Released', 'Failed')),
            CONSTRAINT [CK_OrganizationSeatAssignments_Reservation]
                CHECK ([ReservationExpiresAtUtc] > [ReservedAtUtc]),
            CONSTRAINT [CK_OrganizationSeatAssignments_Period]
                CHECK ([EndsAtUtc] IS NULL OR [StartsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]),
            CONSTRAINT [CK_OrganizationSeatAssignments_License]
                CHECK ([Status] NOT IN ('Scheduled', 'Active') OR [UserLicenseId] IS NOT NULL),
            CONSTRAINT [CK_OrganizationSeatAssignments_Release]
                CHECK ([Status] NOT IN ('Released', 'Failed') OR [ReleasedAtUtc] IS NOT NULL)
        );

        CREATE INDEX [IX_OrganizationSeatAssignments_OrganizationStatus]
            ON [ai].[OrganizationSeatAssignments]
                ([OrganizationPoolId], [OrganizationId], [Status], [ReservationExpiresAtUtc]);

        CREATE INDEX [IX_OrganizationSeatAssignments_UserStatus]
            ON [ai].[OrganizationSeatAssignments]([UserId], [Status], [EndsAtUtc]);

        CREATE INDEX [IX_OrganizationSeatAssignments_Lifecycle]
            ON [ai].[OrganizationSeatAssignments]([Status], [ReservationExpiresAtUtc], [StartsAtUtc], [EndsAtUtc]);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.11-organization-seat-provisioning'
    )
    BEGIN
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.11-organization-seat-provisioning',
            N'Thêm pool tổ chức, sức chứa và reservation/assignment để tự cấp membership khi mua gói.'
        );
    END;

    IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
    BEGIN
        DENY SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::[ai].[OrganizationPools]
            TO [VideoMakerDesktopRole];
        DENY SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::[ai].[OrganizationPoolOrganizations]
            TO [VideoMakerDesktopRole];
        DENY SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::[ai].[LicensePlanOrganizationPools]
            TO [VideoMakerDesktopRole];
        DENY SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::[ai].[OrganizationSeatAssignments]
            TO [VideoMakerDesktopRole];
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

IF OBJECT_ID(N'[ai].[OrganizationPools]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[OrganizationPoolOrganizations]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[LicensePlanOrganizationPools]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[OrganizationSeatAssignments]', N'U') IS NULL OR
   COL_LENGTH(N'ai.OrganizationMembers', N'IsProvisioningManaged') IS NULL OR
   NOT EXISTS
   (
       SELECT 1
       FROM [ai].[SchemaVersions]
       WHERE [Version] = '4.0.11-organization-seat-provisioning'
   )
BEGIN
    THROW 51111, 'Organization seat provisioning migration verification failed.', 1;
END;
