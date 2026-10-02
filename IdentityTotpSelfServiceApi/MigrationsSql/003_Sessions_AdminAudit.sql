-- 세션(기기) 관리와 관리자 감사 강화. 002 적용 후 실행한다. 재실행해도 안전하다.
--   RefreshTokens.DeviceName / UserAgent : 세션 목록 표시용 (Data/ApplicationDbContext.cs와 길이 일치)
--   IX_RefreshTokens_ExpiresAt          : 만료 토큰 정리 작업(RefreshTokenCleanupService)용
--   AuditLogs.ActorUserId               : 작업 수행자(관리자 작업이면 관리자 ID)
-- 컬럼 추가는 NULL 허용이라 기존 행과 실행 중인 이전 버전 API에 영향이 없다.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRAN;
 IF NOT EXISTS(SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610020003_SessionsAdminAudit')
 BEGIN
  IF COL_LENGTH(N'dbo.RefreshTokens',N'DeviceName') IS NULL
   ALTER TABLE [dbo].[RefreshTokens] ADD [DeviceName] nvarchar(128) NULL;
  IF COL_LENGTH(N'dbo.RefreshTokens',N'UserAgent') IS NULL
   ALTER TABLE [dbo].[RefreshTokens] ADD [UserAgent] nvarchar(512) NULL;
  IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE [name]=N'IX_RefreshTokens_ExpiresAt' AND [object_id]=OBJECT_ID(N'[dbo].[RefreshTokens]'))
   CREATE INDEX [IX_RefreshTokens_ExpiresAt] ON [dbo].[RefreshTokens]([ExpiresAt]);
  IF COL_LENGTH(N'dbo.AuditLogs',N'ActorUserId') IS NULL
   ALTER TABLE [dbo].[AuditLogs] ADD [ActorUserId] nvarchar(450) NULL;
  INSERT [dbo].[__EFMigrationsHistory]([MigrationId],[ProductVersion]) VALUES(N'202610020003_SessionsAdminAudit',N'10.0.0');
 END
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
