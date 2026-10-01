-- 필터 인덱스 생성에 필요하다. sqlcmd 기본값은 QUOTED_IDENTIFIER OFF이므로 명시한다.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRAN;
 IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]',N'U') IS NULL
 CREATE TABLE [dbo].[__EFMigrationsHistory]([MigrationId] nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY,[ProductVersion] nvarchar(32) NOT NULL);
 IF NOT EXISTS(SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610010001_IdentityTotpV4')
 BEGIN
  CREATE TABLE [dbo].[AspNetRoles]([Id] nvarchar(450) NOT NULL,[Name] nvarchar(256) NULL,[NormalizedName] nvarchar(256) NULL,[ConcurrencyStamp] nvarchar(max) NULL,CONSTRAINT [PK_AspNetRoles] PRIMARY KEY([Id]));
  CREATE TABLE [dbo].[AspNetUsers]([Id] nvarchar(450) NOT NULL,[UserName] nvarchar(256) NULL,[NormalizedUserName] nvarchar(256) NULL,[Email] nvarchar(256) NULL,[NormalizedEmail] nvarchar(256) NULL,[EmailConfirmed] bit NOT NULL,[PasswordHash] nvarchar(max) NULL,[SecurityStamp] nvarchar(max) NULL,[ConcurrencyStamp] nvarchar(max) NULL,[PhoneNumber] nvarchar(max) NULL,[PhoneNumberConfirmed] bit NOT NULL,[TwoFactorEnabled] bit NOT NULL,[LockoutEnd] datetimeoffset NULL,[LockoutEnabled] bit NOT NULL,[AccessFailedCount] int NOT NULL,CONSTRAINT [PK_AspNetUsers] PRIMARY KEY([Id]));
  CREATE TABLE [dbo].[AspNetRoleClaims]([Id] int IDENTITY NOT NULL,[RoleId] nvarchar(450) NOT NULL,[ClaimType] nvarchar(max) NULL,[ClaimValue] nvarchar(max) NULL,CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY([Id]),CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY([RoleId]) REFERENCES [dbo].[AspNetRoles]([Id]) ON DELETE CASCADE);
  CREATE TABLE [dbo].[AspNetUserClaims]([Id] int IDENTITY NOT NULL,[UserId] nvarchar(450) NOT NULL,[ClaimType] nvarchar(max) NULL,[ClaimValue] nvarchar(max) NULL,CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY([Id]),CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE);
  CREATE TABLE [dbo].[AspNetUserLogins]([LoginProvider] nvarchar(450) NOT NULL,[ProviderKey] nvarchar(450) NOT NULL,[ProviderDisplayName] nvarchar(max) NULL,[UserId] nvarchar(450) NOT NULL,CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY([LoginProvider],[ProviderKey]),CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE);
  CREATE TABLE [dbo].[AspNetUserRoles]([UserId] nvarchar(450) NOT NULL,[RoleId] nvarchar(450) NOT NULL,CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY([UserId],[RoleId]),CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY([RoleId]) REFERENCES [dbo].[AspNetRoles]([Id]) ON DELETE CASCADE,CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE);
  CREATE TABLE [dbo].[AspNetUserTokens]([UserId] nvarchar(450) NOT NULL,[LoginProvider] nvarchar(450) NOT NULL,[Name] nvarchar(450) NOT NULL,[Value] nvarchar(max) NULL,CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY([UserId],[LoginProvider],[Name]),CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE);
  CREATE TABLE [dbo].[RefreshTokens]([Id] bigint IDENTITY NOT NULL,[UserId] nvarchar(450) NOT NULL,[TokenHash] nvarchar(64) NOT NULL,[FamilyId] nvarchar(32) NOT NULL,[CreatedAt] datetimeoffset NOT NULL,[ExpiresAt] datetimeoffset NOT NULL,[RevokedAt] datetimeoffset NULL,[ReplacedByTokenHash] nvarchar(64) NULL,[CreatedByIp] nvarchar(64) NULL,[RevokedByIp] nvarchar(64) NULL,[RevokeReason] nvarchar(128) NULL,[Version] nvarchar(32) NOT NULL CONSTRAINT [DF_RefreshTokens_Version] DEFAULT(REPLACE(CONVERT(nvarchar(36),NEWID()),'-','')),CONSTRAINT [PK_RefreshTokens] PRIMARY KEY([Id]),CONSTRAINT [FK_RefreshTokens_AspNetUsers_UserId] FOREIGN KEY([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE);
  CREATE TABLE [dbo].[AuditLogs]([Id] bigint IDENTITY NOT NULL,[OccurredAt] datetimeoffset NOT NULL,[UserId] nvarchar(450) NULL,[EventType] nvarchar(128) NOT NULL,[Success] bit NOT NULL,[IpAddress] nvarchar(64) NULL,[UserAgent] nvarchar(512) NULL,[Detail] nvarchar(2000) NULL,CONSTRAINT [PK_AuditLogs] PRIMARY KEY([Id]));
  CREATE UNIQUE INDEX [RoleNameIndex] ON [dbo].[AspNetRoles]([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
  CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [dbo].[AspNetRoleClaims]([RoleId]);
  CREATE INDEX [EmailIndex] ON [dbo].[AspNetUsers]([NormalizedEmail]);
  CREATE UNIQUE INDEX [UserNameIndex] ON [dbo].[AspNetUsers]([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
  CREATE INDEX [IX_AspNetUserClaims_UserId] ON [dbo].[AspNetUserClaims]([UserId]);
  CREATE INDEX [IX_AspNetUserLogins_UserId] ON [dbo].[AspNetUserLogins]([UserId]);
  CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [dbo].[AspNetUserRoles]([RoleId]);
  CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [dbo].[RefreshTokens]([TokenHash]);
  CREATE INDEX [IX_RefreshTokens_UserId_FamilyId] ON [dbo].[RefreshTokens]([UserId],[FamilyId]);
  CREATE INDEX [IX_AuditLogs_OccurredAt] ON [dbo].[AuditLogs]([OccurredAt]);
  INSERT [dbo].[__EFMigrationsHistory]([MigrationId],[ProductVersion]) VALUES(N'202610010001_IdentityTotpV4',N'10.0.0');
 END
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
