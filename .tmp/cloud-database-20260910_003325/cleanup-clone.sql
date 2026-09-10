SET NOCOUNT ON;
IF CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) <> N'DUNGDEV' OR DB_NAME() <> N'master'
 THROW 51183, 'Unexpected cleanup target.', 1;
IF (SELECT COUNT(*) FROM sys.master_files WHERE database_id=DB_ID(N'VideoFactory_CloudRehearsal_20260910_003325') AND physical_name IN (N'D:\SQL2019\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\VideoFactory_CloudRehearsal_20260910_003325.mdf',N'D:\SQL2019\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\VideoFactory_CloudRehearsal_20260910_003325_log.ldf'))<>2
 THROW 51184, 'Clone file paths do not match the task manifest.', 1;
IF EXISTS (SELECT 1 FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'VideoFactory_CloudRehearsal_20260910_003325') AND is_user_process=1)
 THROW 51185, 'Clone has active users; preserve it.', 1;
DROP DATABASE [VideoFactory_CloudRehearsal_20260910_003325];
SELECT N'Only task-created rehearsal clone removed; original database and backup retained.' AS Result;