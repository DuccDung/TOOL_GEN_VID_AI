/*
    VideoFactory 4.0.1 - Repair mojibake in the built-in license plan text.

    This file is intentionally ASCII-only. The original UTF-8 seed text can be
    misread by sqlcmd when no input code page is specified. UTF-16LE byte values
    below make this repair independent of the SQL client's input encoding.

    Safety:
      - Only the three built-in plan codes are considered.
      - A field is changed only when its bytes exactly match the known corrupt value.
      - Administrator-customized plan text is never overwritten.
      - The script is transactional and idempotent.
*/

USE [VideoFactory];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[auth].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51020, 'Base auth schema is missing. Run VideoFactory.Initial.sql first.', 1;
END;

IF OBJECT_ID(N'[auth].[LicensePlans]', N'U') IS NULL
BEGIN
    THROW 51021, 'auth.LicensePlans is missing. Run VideoFactory.Initial.sql first.', 1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Repairs table
    (
        [PlanCode] varchar(50) NOT NULL PRIMARY KEY,
        [CorruptName] varbinary(max) NOT NULL,
        [CorrectName] varbinary(max) NOT NULL,
        [CorruptDescription] varbinary(max) NOT NULL,
        [CorrectDescription] varbinary(max) NOT NULL
    );

    INSERT INTO @Repairs
        ([PlanCode], [CorruptName], [CorrectName], [CorruptDescription], [CorrectDescription])
    VALUES
        (
            'trial-7',
            0x4400C300B9006E006700200074006800E100BB00AD002000370020006E006700C300A0007900,
            0x4400F9006E006700200074006800ED1E2000370020006E006700E0007900,
            0x510075007900E100BB0081006E0020007300E100BB00AD0020006400E100BB00A5006E006700200056006900640065006F004D0061006B00650072002000740072006F006E0067002000370020006E006700C300A00079002E00,
            0x510075007900C11E6E0020007300ED1E20006400E51E6E006700200056006900640065006F004D0061006B00650072002000740072006F006E0067002000370020006E006700E00079002E00
        ),
        (
            'monthly-30',
            0x4700C300B300690020003300300020006E006700C300A0007900,
            0x4700F300690020003300300020006E006700E0007900,
            0x510075007900E100BB0081006E0020007300E100BB00AD0020006400E100BB00A5006E006700200056006900640065006F004D0061006B00650072002000740072006F006E00670020003300300020006E006700C300A00079002E00,
            0x510075007900C11E6E0020007300ED1E20006400E51E6E006700200056006900640065006F004D0061006B00650072002000740072006F006E00670020003300300020006E006700E00079002E00
        ),
        (
            'half-year-180',
            0x4700C300B3006900200031003800300020006E006700C300A0007900,
            0x4700F3006900200031003800300020006E006700E0007900,
            0x510075007900E100BB0081006E0020007300E100BB00AD0020006400E100BB00A5006E006700200056006900640065006F004D0061006B00650072002000740072006F006E006700200031003800300020006E006700C300A00079002E00,
            0x510075007900C11E6E0020007300ED1E20006400E51E6E006700200056006900640065006F004D0061006B00650072002000740072006F006E006700200031003800300020006E006700E00079002E00
        );

    UPDATE p
    SET
        [Name] = CASE
            WHEN CONVERT(varbinary(max), p.[Name]) = r.[CorruptName]
                THEN CONVERT(nvarchar(200), r.[CorrectName])
            ELSE p.[Name]
        END,
        [Description] = CASE
            WHEN CONVERT(varbinary(max), p.[Description]) = r.[CorruptDescription]
                THEN CONVERT(nvarchar(1000), r.[CorrectDescription])
            ELSE p.[Description]
        END,
        [UpdatedAtUtc] = SYSUTCDATETIME()
    FROM [auth].[LicensePlans] p
    INNER JOIN @Repairs r ON r.[PlanCode] = p.[PlanCode]
    WHERE CONVERT(varbinary(max), p.[Name]) = r.[CorruptName]
       OR CONVERT(varbinary(max), p.[Description]) = r.[CorruptDescription];

    DECLARE @UpdatedRows int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM [auth].[LicensePlans] p
        INNER JOIN @Repairs r ON r.[PlanCode] = p.[PlanCode]
        WHERE CONVERT(varbinary(max), p.[Name]) = r.[CorruptName]
           OR CONVERT(varbinary(max), p.[Description]) = r.[CorruptDescription]
    )
    BEGIN
        THROW 51022, 'One or more built-in license plan values could not be repaired.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [auth].[SchemaVersions]
        WHERE [Version] = '4.0.1-vietnamese-seed-text-repair'
    )
    BEGIN
        INSERT INTO [auth].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.1-vietnamese-seed-text-repair',
            N'Repair UTF-8 mojibake in built-in Vietnamese license plan names and descriptions.'
        );
    END;

    COMMIT TRANSACTION;

    PRINT 'VideoFactory license plan text repair 4.0.1 is ready.';
    PRINT 'Updated rows: ' + CONVERT(varchar(20), @UpdatedRows);

    SELECT [PlanCode], [Name], [Description]
    FROM [auth].[LicensePlans]
    WHERE [PlanCode] IN ('trial-7', 'monthly-30', 'half-year-180')
    ORDER BY [PlanCode];
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
