/*
    Read-only verification for organization-seat provisioning after migration 4.0.11.
    This script does not create, update, or delete application data.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

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
    THROW 51210, 'Migration 4.0.11 objects or schema version are missing.', 1;
END;

IF NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE [object_id] = OBJECT_ID(N'[ai].[OrganizationPoolOrganizations]')
         AND [name] = N'UQ_OrganizationPoolOrganizations_AutoOrganization'
         AND [is_unique] = 1
         AND [has_filter] = 1
   ) OR
   NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE [object_id] = OBJECT_ID(N'[ai].[OrganizationSeatAssignments]')
         AND [name] = N'UQ_OrganizationSeatAssignments_Payment'
         AND [is_unique] = 1
   )
BEGIN
    THROW 51211, 'A required organization-seat unique index is missing.', 1;
END;

IF EXISTS
   (
       SELECT 1
       FROM [ai].[OrganizationPoolOrganizations] AS configured
       LEFT JOIN [ai].[OrganizationPools] AS pool
           ON pool.[OrganizationPoolId] = configured.[OrganizationPoolId]
       LEFT JOIN [ai].[Organizations] AS organization
           ON organization.[OrganizationId] = configured.[OrganizationId]
       WHERE pool.[OrganizationPoolId] IS NULL OR organization.[OrganizationId] IS NULL
   ) OR
   EXISTS
   (
       SELECT 1
       FROM [ai].[LicensePlanOrganizationPools] AS mapping
       LEFT JOIN [auth].[LicensePlans] AS planRow
           ON planRow.[LicensePlanId] = mapping.[LicensePlanId]
       LEFT JOIN [ai].[OrganizationPools] AS pool
           ON pool.[OrganizationPoolId] = mapping.[OrganizationPoolId]
       WHERE planRow.[LicensePlanId] IS NULL OR pool.[OrganizationPoolId] IS NULL
   ) OR
   EXISTS
   (
       SELECT 1
       FROM [ai].[OrganizationSeatAssignments] AS assignment
       LEFT JOIN [auth].[LicensePayments] AS payment
           ON payment.[LicensePaymentId] = assignment.[LicensePaymentId]
       LEFT JOIN [dbo].[AspNetUsers] AS appUser
           ON appUser.[Id] = assignment.[UserId]
       WHERE payment.[LicensePaymentId] IS NULL OR appUser.[Id] IS NULL
   )
BEGIN
    THROW 51212, 'Organization-seat provisioning contains orphaned rows.', 1;
END;

IF EXISTS
   (
       SELECT 1
       FROM [ai].[OrganizationPoolOrganizations] AS configured
       WHERE configured.[ActiveSeatCount] < 0 OR
             configured.[ReservedSeatCount] < 0 OR
             configured.[ActiveSeatCount] + configured.[ReservedSeatCount] > configured.[SeatCapacity]
   )
BEGIN
    THROW 51213, 'A pool organization exceeds its configured seat capacity.', 1;
END;

IF EXISTS
   (
       SELECT 1
       FROM [ai].[OrganizationPoolOrganizations] AS configured
       WHERE configured.[ActiveSeatCount] <>
             (
                 SELECT COUNT_BIG(*)
                 FROM [ai].[OrganizationSeatAssignments] AS assignment
                 WHERE assignment.[OrganizationPoolId] = configured.[OrganizationPoolId]
                   AND assignment.[OrganizationId] = configured.[OrganizationId]
                   AND assignment.[ConsumesSeat] = 1
                   AND assignment.[Status] = 'Active'
             ) OR
             configured.[ReservedSeatCount] <>
             (
                 SELECT COUNT_BIG(*)
                 FROM [ai].[OrganizationSeatAssignments] AS assignment
                 WHERE assignment.[OrganizationPoolId] = configured.[OrganizationPoolId]
                   AND assignment.[OrganizationId] = configured.[OrganizationId]
                   AND assignment.[ConsumesSeat] = 1
                   AND assignment.[Status] IN ('Reserved', 'Scheduled')
             )
   )
BEGIN
    THROW 51214, 'Stored seat counters do not match consuming assignments.', 1;
END;

IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL AND
   EXISTS
   (
       SELECT expected.[ObjectId], expected.[PermissionName]
       FROM
       (
           SELECT objectRow.[ObjectId], permissionRow.[PermissionName]
           FROM
           (
               VALUES
                   (OBJECT_ID(N'[ai].[OrganizationPools]')),
                   (OBJECT_ID(N'[ai].[OrganizationPoolOrganizations]')),
                   (OBJECT_ID(N'[ai].[LicensePlanOrganizationPools]')),
                   (OBJECT_ID(N'[ai].[OrganizationSeatAssignments]'))
           ) AS objectRow([ObjectId])
           CROSS JOIN
           (
               VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE')
           ) AS permissionRow([PermissionName])
       ) AS expected
       EXCEPT
       SELECT permissionRow.[major_id], permissionRow.[permission_name]
       FROM sys.database_permissions AS permissionRow
       WHERE permissionRow.[grantee_principal_id] = DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole')
         AND permissionRow.[class] = 1
         AND permissionRow.[state] = 'D'
   )
BEGIN
    THROW 51215, 'VideoMakerDesktopRole is missing an object-level DENY on provisioning tables.', 1;
END;

SELECT
    schemaVersion.[Version],
    schemaVersion.[Description],
    schemaVersion.[AppliedAtUtc]
FROM [ai].[SchemaVersions] AS schemaVersion
WHERE schemaVersion.[Version] IN
(
    '4.0.10-license-sepay-payments',
    '4.0.11-organization-seat-provisioning'
)
ORDER BY schemaVersion.[Version];

SELECT
    pool.[Code] AS [PoolCode],
    organization.[Code] AS [OrganizationCode],
    configured.[SeatCapacity],
    configured.[ActiveSeatCount],
    configured.[ReservedSeatCount],
    configured.[SeatCapacity] - configured.[ActiveSeatCount] - configured.[ReservedSeatCount] AS [AvailableSeatCount],
    configured.[IsAutoAssignmentEnabled],
    configured.[IsReady]
FROM [ai].[OrganizationPoolOrganizations] AS configured
INNER JOIN [ai].[OrganizationPools] AS pool
    ON pool.[OrganizationPoolId] = configured.[OrganizationPoolId]
INNER JOIN [ai].[Organizations] AS organization
    ON organization.[OrganizationId] = configured.[OrganizationId]
ORDER BY pool.[Code], configured.[Priority], organization.[Code];

SELECT
    payment.[LicensePaymentId],
    payment.[OrderCode],
    payment.[Status] AS [PaymentStatus],
    payment.[FailureCode],
    payment.[PaidAtUtc],
    assignment.[OrganizationSeatAssignmentId],
    assignment.[Status] AS [AssignmentStatus],
    assignment.[FailureCode] AS [AssignmentFailureCode]
FROM [auth].[LicensePayments] AS payment
LEFT JOIN [ai].[OrganizationSeatAssignments] AS assignment
    ON assignment.[LicensePaymentId] = payment.[LicensePaymentId]
WHERE payment.[Status] = 'Paid'
ORDER BY payment.[PaidAtUtc], payment.[LicensePaymentId];
