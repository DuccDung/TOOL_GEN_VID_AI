-- Source images remain local. Only metadata, quotes and lineage are retained here.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'vf.Scenes', N'U') IS NULL
    THROW 51900, 'Apply workflow baseline before short-video outfit migration.', 1;
 IF OBJECT_ID(N'vf.ShortVideoOutfits', N'U') IS NULL
 BEGIN
    CREATE TABLE vf.ShortVideoOutfits (
      SceneId uniqueidentifier NOT NULL PRIMARY KEY,
      ProjectId uniqueidentifier NOT NULL,
      Revision int NOT NULL CHECK (Revision > 0),
      CharacterJson nvarchar(max) NOT NULL CHECK (ISJSON(CharacterJson)=1),
      OutfitJson nvarchar(max) NOT NULL CHECK (ISJSON(OutfitJson)=1),
      Background nvarchar(1500) NOT NULL,
      Motion nvarchar(2000) NOT NULL,
      CompositionId uniqueidentifier NULL,
      FOREIGN KEY (SceneId) REFERENCES vf.Scenes(SceneId),
      FOREIGN KEY (ProjectId) REFERENCES vf.Projects(ProjectId)
    );
 END;
 IF OBJECT_ID(N'vf.ShortVideoOperations', N'U') IS NULL
 BEGIN
    CREATE TABLE vf.ShortVideoOperations (
      OperationId uniqueidentifier NOT NULL PRIMARY KEY,
      SceneId uniqueidentifier NOT NULL,
      ProjectId uniqueidentifier NOT NULL,
      OrganizationId uniqueidentifier NOT NULL,
      UserId nvarchar(450) NOT NULL,
      Kind nvarchar(10) NOT NULL CHECK (Kind IN ('Image','Video')),
      Status nvarchar(30) NOT NULL CHECK (Status IN ('Quoted','Submitting','PendingReview','Approved','Rejected','Unknown','Failed','Completed')),
      Revision int NOT NULL CHECK (Revision > 0),
      CompositionId uniqueidentifier NULL,
      QuoteJson nvarchar(max) NOT NULL CHECK (ISJSON(QuoteJson)=1),
      ResultJson nvarchar(max) NOT NULL,
      ExpiresAtUtc datetime2 NOT NULL,
      CreatedAtUtc datetime2 NOT NULL,
      ApprovedAtUtc datetime2 NULL,
      ApprovedByUserId nvarchar(450) NULL,
      FOREIGN KEY (SceneId) REFERENCES vf.Scenes(SceneId),
      FOREIGN KEY (ProjectId) REFERENCES vf.Projects(ProjectId),
      FOREIGN KEY (OrganizationId) REFERENCES ai.Organizations(OrganizationId),
      CONSTRAINT CK_ShortVideoOperations_Approval CHECK (Status <> 'Approved' OR (ApprovedAtUtc IS NOT NULL AND ApprovedByUserId IS NOT NULL))
    );
    CREATE INDEX IX_ShortVideoOperations_SceneId_Revision_Kind ON vf.ShortVideoOperations(SceneId,Revision,Kind);
 END;
 -- No grant to the transitional desktop principal: these mutations use server APIs.
 IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
 BEGIN
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::vf.ShortVideoOutfits TO VideoMakerDesktopRole;
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::vf.ShortVideoOperations TO VideoMakerDesktopRole;
 END;
 IF NOT EXISTS (SELECT 1 FROM ai.SchemaVersions WHERE Version='4.1.9-short-video-outfit')
    INSERT INTO ai.SchemaVersions(Version,Description) VALUES ('4.1.9-short-video-outfit',N'Short-video character/outfit input, quotes and composition approval lineage.');
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE() <> 0 ROLLBACK;
 THROW;
END CATCH;
