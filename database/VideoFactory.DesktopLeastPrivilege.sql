/*
    Transitional least-privilege role for TOOL-LOCAL.

    Run as database owner, then add the dedicated desktop database user:
      ALTER ROLE [VideoMakerDesktopRole] ADD MEMBER [YourDesktopDatabaseUser];

    Do not add the ASP.NET server database user to this role. The server needs
    write access to auth/ai/provider-request tables.
*/

USE [VideoFactory];
GO

IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NULL
    CREATE ROLE [VideoMakerDesktopRole] AUTHORIZATION [dbo];
GO

GRANT CONNECT TO [VideoMakerDesktopRole];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[vf] TO [VideoMakerDesktopRole];

/* Provider catalog is readable for display, never mutable from Desktop. */
DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[Providers] TO [VideoMakerDesktopRole];
DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[ProviderModels] TO [VideoMakerDesktopRole];
DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[CostRates] TO [VideoMakerDesktopRole];

/* AI gateway request/cost truth is server-owned. */
DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[ProviderRequests] TO [VideoMakerDesktopRole];
DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[UsageCosts] TO [VideoMakerDesktopRole];
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[ProviderCredentials] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[vf].[GeneratedImageOutputs]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedImageOutputs] TO [VideoMakerDesktopRole];

IF OBJECT_ID(N'[vf].[SceneFirstFrames]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[SceneFirstFrames] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[vf].[GeneratedVoiceOutputs]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedVoiceOutputs] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[vf].[GeneratedVideoOutputs]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedVideoOutputs] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[vf].[ProjectAssets]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[ProjectAssets] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[vf].[ProjectAssetVersions]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[ProjectAssetVersions] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[vf].[SceneAssetAssignments]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[SceneAssetAssignments] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[vf].[ProviderRequestAssetVersions]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[ProviderRequestAssetVersions] TO [VideoMakerDesktopRole];

/* Organization, billing, credentials, Identity and data-protection keys are server-only. */
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[ai] TO [VideoMakerDesktopRole];
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[auth] TO [VideoMakerDesktopRole];
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[dbo] TO [VideoMakerDesktopRole];

IF OBJECT_ID(N'[ai].[OrganizationPools]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[ai].[OrganizationPools] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[ai].[OrganizationPoolOrganizations]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[ai].[OrganizationPoolOrganizations] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[ai].[LicensePlanOrganizationPools]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[ai].[LicensePlanOrganizationPools] TO [VideoMakerDesktopRole];
IF OBJECT_ID(N'[ai].[OrganizationSeatAssignments]', N'U') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[ai].[OrganizationSeatAssignments] TO [VideoMakerDesktopRole];
GO

PRINT N'VideoMakerDesktopRole is ready. Add only the dedicated Desktop database user to this role.';
GO
