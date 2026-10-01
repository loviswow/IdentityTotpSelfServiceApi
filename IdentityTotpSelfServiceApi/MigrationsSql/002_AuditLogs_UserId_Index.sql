-- 사용자별 감사 로그 조회(사고 조사)용 인덱스. 001 적용 후 실행한다. 재실행해도 안전하다.
-- sqlcmd 기본값(QUOTED_IDENTIFIER OFF)과 무관하게 동일하게 동작하도록 명시한다.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610010002_AuditLogsUserIdIndex')
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE [name]=N'IX_AuditLogs_UserId_OccurredAt' AND [object_id]=OBJECT_ID(N'[dbo].[AuditLogs]'))
   CREATE INDEX [IX_AuditLogs_UserId_OccurredAt] ON [dbo].[AuditLogs]([UserId],[OccurredAt]);
  INSERT [dbo].[__EFMigrationsHistory]([MigrationId],[ProductVersion]) VALUES(N'202610010002_AuditLogsUserIdIndex',N'10.0.0');
 END
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
