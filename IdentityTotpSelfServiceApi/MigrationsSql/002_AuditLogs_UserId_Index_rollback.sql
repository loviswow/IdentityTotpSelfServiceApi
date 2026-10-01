-- 002 롤백: 인덱스만 제거한다. 데이터는 삭제되지 않는다.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRAN;
 IF EXISTS(SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610010002_AuditLogsUserIdIndex')
 BEGIN
  DROP INDEX IF EXISTS [IX_AuditLogs_UserId_OccurredAt] ON [dbo].[AuditLogs];
  DELETE [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610010002_AuditLogsUserIdIndex';
 END
 COMMIT;
END TRY
BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
