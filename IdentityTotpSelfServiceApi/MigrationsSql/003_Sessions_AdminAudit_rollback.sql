-- 003 롤백: 인덱스와 컬럼을 제거한다. 세션 기기 이름/User-Agent와 감사 로그의 수행자(ActorUserId) 값이 삭제된다.
-- 롤백 전에 API를 003 이전 버전으로 되돌려야 한다(새 버전 API는 이 컬럼을 사용한다).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRAN;
 IF EXISTS(SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610020003_SessionsAdminAudit')
 BEGIN
  DROP INDEX IF EXISTS [IX_RefreshTokens_ExpiresAt] ON [dbo].[RefreshTokens];
  IF COL_LENGTH(N'dbo.RefreshTokens',N'DeviceName') IS NOT NULL ALTER TABLE [dbo].[RefreshTokens] DROP COLUMN [DeviceName];
  IF COL_LENGTH(N'dbo.RefreshTokens',N'UserAgent') IS NOT NULL ALTER TABLE [dbo].[RefreshTokens] DROP COLUMN [UserAgent];
  IF COL_LENGTH(N'dbo.AuditLogs',N'ActorUserId') IS NOT NULL ALTER TABLE [dbo].[AuditLogs] DROP COLUMN [ActorUserId];
  DELETE [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610020003_SessionsAdminAudit';
 END
 COMMIT;
END TRY
BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
