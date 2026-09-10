SET NOCOUNT ON;
SET LOCK_TIMEOUT 15000;
IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'vs') AND name IN (N'CloudTranslationJobs',N'CloudTranslationBatches',N'CloudTranslationAttempts'))<>3
 THROW 51172, 'Missing Cloud tables.', 1;
IF (SELECT COUNT(*) FROM ai.SchemaVersions WHERE Version=N'4.1.6')<>1
 THROW 51173, 'Unexpected Cloud schema version.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID(N'ai.BudgetReservations'),OBJECT_ID(N'ai.UsageLedger')) AND name IN (N'ProjectId',N'VietsubProjectId') AND is_nullable=1 AND system_type_id=36)<>4
 THROW 51174, 'Unexpected budget project columns.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_BudgetReservations_VietsubProjects',N'FK_UsageLedger_VietsubProjects') AND referenced_object_id=OBJECT_ID(N'vs.Projects') AND is_disabled=0 AND is_not_trusted=0)<>2
 THROW 51175, 'Missing or untrusted Vietsub foreign keys.', 1;
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N'CK_BudgetReservations_ProjectKind',N'CK_UsageLedger_ProjectKind') AND is_disabled=0 AND is_not_trusted=0)<>2
 THROW 51176, 'Missing or untrusted project kind constraints.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_BudgetReservations_Projects',N'FK_UsageLedger_Projects') AND referenced_object_id=OBJECT_ID(N'vf.Projects') AND is_disabled=0 AND is_not_trusted=0)<>2
 THROW 51177, 'Legacy video foreign keys were not preserved.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'vs.CloudTranslationJobs') AND name=N'UX_CloudJobs_ActiveProject' AND is_unique=1 AND has_filter=1 AND is_disabled=0)
 THROW 51178, 'Missing active job filtered index.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'vs.CloudTranslationJobs'),OBJECT_ID(N'vs.CloudTranslationBatches'),OBJECT_ID(N'vs.CloudTranslationAttempts')) AND (is_disabled=1 OR is_not_trusted=1))
 THROW 51179, 'Untrusted Cloud foreign key.', 1;
SELECT DB_NAME() AS DatabaseName, N'PASS' AS SchemaVerification;
SELECT s.name AS SchemaName,t.name AS TableName,p.rows AS RowsCount FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE (s.name=N'vs' AND t.name LIKE N'CloudTranslation%') OR (s.name=N'ai' AND t.name IN (N'BudgetReservations',N'UsageLedger')) ORDER BY s.name,t.name;
SELECT Version,AppliedAtUtc FROM ai.SchemaVersions WHERE Version=N'4.1.6';