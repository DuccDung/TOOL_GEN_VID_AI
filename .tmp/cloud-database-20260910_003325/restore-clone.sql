SET NOCOUNT ON;
IF CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) <> N'DUNGDEV' OR DB_ID(N'VideoFactory_CloudRehearsal_20260910_003325') IS NOT NULL
 THROW 51171, 'Unexpected server or existing clone.', 1;
RESTORE DATABASE [VideoFactory_CloudRehearsal_20260910_003325] FROM DISK=N'D:\SQL2019\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\Backup\VideoFactory_before_cloud_20260910_003325.bak'
 WITH MOVE N'VideoFactory' TO N'D:\SQL2019\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\VideoFactory_CloudRehearsal_20260910_003325.mdf', MOVE N'VideoFactory_log' TO N'D:\SQL2019\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\VideoFactory_CloudRehearsal_20260910_003325_log.ldf', RECOVERY, CHECKSUM, STATS=25;