-- 필터 인덱스 생성에 필요하다. sqlcmd 기본값은 QUOTED_IDENTIFIER OFF이므로 명시한다.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRAN;
 IF EXISTS(SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610010001_IdentityTotpV4')
 BEGIN
  DROP TABLE IF EXISTS [dbo].[AuditLogs]; DROP TABLE IF EXISTS [dbo].[RefreshTokens]; DROP TABLE IF EXISTS [dbo].[AspNetRoleClaims]; DROP TABLE IF EXISTS [dbo].[AspNetUserClaims]; DROP TABLE IF EXISTS [dbo].[AspNetUserLogins]; DROP TABLE IF EXISTS [dbo].[AspNetUserRoles]; DROP TABLE IF EXISTS [dbo].[AspNetUserTokens]; DROP TABLE IF EXISTS [dbo].[AspNetRoles]; DROP TABLE IF EXISTS [dbo].[AspNetUsers];
  DELETE [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610010001_IdentityTotpV4';
 END
 COMMIT;
END TRY
BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
