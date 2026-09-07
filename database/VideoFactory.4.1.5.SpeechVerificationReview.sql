/*
    Audited human override for speech verification reports that need review.

    This migration is idempotent. Run only after a verified backup and after
    VideoFactory.4.1.4.VoiceProfileApproval.sql. It does not call an AI provider.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[SpeechVerificationReports]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
        THROW 51150, 'Speech synchronization schema 4.1.3 is required before speech review migration.', 1;

    IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewApproved') IS NULL
        ALTER TABLE [vf].[SpeechVerificationReports] ADD
            [ReviewApproved] bit NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_ReviewApproved] DEFAULT (0) WITH VALUES;

    IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewReason') IS NULL
        ALTER TABLE [vf].[SpeechVerificationReports] ADD [ReviewReason] nvarchar(1000) NULL;

    IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewedByUserId') IS NULL
        ALTER TABLE [vf].[SpeechVerificationReports] ADD [ReviewedByUserId] nvarchar(450) NULL;

    IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewedAtUtc') IS NULL
        ALTER TABLE [vf].[SpeechVerificationReports] ADD [ReviewedAtUtc] datetime2(3) NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[SpeechVerificationReports]')
          AND [name] = N'CK_SpeechVerificationReports_Review'
    )
        EXEC(N'ALTER TABLE [vf].[SpeechVerificationReports] WITH CHECK
               ADD CONSTRAINT [CK_SpeechVerificationReports_Review]
               CHECK (([ReviewApproved] = 0 AND [ReviewReason] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL) OR
                      ([ReviewApproved] = 1 AND [Status] = ''NeedsReview'' AND
                       LEN(LTRIM(RTRIM([ReviewReason]))) BETWEEN 10 AND 1000 AND
                       [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL));');

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.5-speech-verification-review'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.5-speech-verification-review',
            N'Audited user approval and reason for ASR results that require manual review.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewApproved') IS NULL OR
   COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewReason') IS NULL OR
   COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewedByUserId') IS NULL OR
   COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewedAtUtc') IS NULL OR
   NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.5-speech-verification-review')
    THROW 51151, 'Speech verification review schema verification failed.', 1;

PRINT N'VideoFactory speech verification review schema 4.1.5 is ready.';
GO
