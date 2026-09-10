-- Local post-processing policy only. No model, voice sample, transcript or secret is stored here.
-- Does not depend on the experimental cloud LipSync tables in 4.1.6/4.1.7.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'[vf].[Projects]', N'U') IS NULL
        THROW 51800, 'Apply the VideoFactory workflow baseline before 4.1.8.', 1;
    IF COL_LENGTH(N'vf.Projects', N'LocalVoicePolicyVersion') IS NULL
        ALTER TABLE [vf].[Projects] ADD [LocalVoicePolicyVersion] varchar(50) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Projects_LocalVoicePolicyVersion')
        EXEC(N'ALTER TABLE [vf].[Projects] ADD CONSTRAINT [CK_Projects_LocalVoicePolicyVersion]
            CHECK ([LocalVoicePolicyVersion] IS NULL OR [LocalVoicePolicyVersion] = ''veo-local-voice-v1'');');
    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.8-local-voice-consistency')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES ('4.1.8-local-voice-consistency', N'Local Veo voice consistency policy; media and checkpoints stay local.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
