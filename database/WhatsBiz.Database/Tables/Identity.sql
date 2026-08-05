CREATE TABLE [core].[Roles] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [Name] NVARCHAR(256) NULL, [NormalizedName] NVARCHAR(256) NULL,
    [ConcurrencyStamp] NVARCHAR(MAX) NULL, [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_Roles_CreatedOn] DEFAULT SYSUTCDATETIME());
GO
CREATE UNIQUE INDEX [UX_Roles_NormalizedName] ON [core].[Roles]([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO
CREATE TABLE [core].[Users] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [UserName] NVARCHAR(256) NULL, [NormalizedUserName] NVARCHAR(256) NULL, [Email] NVARCHAR(256) NULL, [NormalizedEmail] NVARCHAR(256) NULL, [EmailConfirmed] BIT NOT NULL, [PasswordHash] NVARCHAR(MAX) NULL, [SecurityStamp] NVARCHAR(MAX) NULL, [ConcurrencyStamp] NVARCHAR(MAX) NULL, [PhoneNumber] NVARCHAR(MAX) NULL, [PhoneNumberConfirmed] BIT NOT NULL, [TwoFactorEnabled] BIT NOT NULL, [LockoutEnd] DATETIMEOFFSET NULL, [LockoutEnabled] BIT NOT NULL, [AccessFailedCount] INT NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL);
GO
CREATE UNIQUE INDEX [UX_Users_NormalizedUserName] ON [core].[Users]([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO
CREATE TABLE [core].[UserRoles] ([UserId] UNIQUEIDENTIFIER NOT NULL, [RoleId] UNIQUEIDENTIFIER NOT NULL, CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId],[RoleId]), CONSTRAINT [FK_UserRoles_Users] FOREIGN KEY ([UserId]) REFERENCES [core].[Users]([Id]), CONSTRAINT [FK_UserRoles_Roles] FOREIGN KEY ([RoleId]) REFERENCES [core].[Roles]([Id]));
GO
CREATE TABLE [core].[RoleClaims] ([Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [RoleId] UNIQUEIDENTIFIER NOT NULL, [ClaimType] NVARCHAR(MAX) NULL, [ClaimValue] NVARCHAR(MAX) NULL, CONSTRAINT [FK_RoleClaims_Roles] FOREIGN KEY ([RoleId]) REFERENCES [core].[Roles]([Id]));
GO
CREATE TABLE [core].[UserClaims] ([Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [UserId] UNIQUEIDENTIFIER NOT NULL, [ClaimType] NVARCHAR(MAX) NULL, [ClaimValue] NVARCHAR(MAX) NULL, CONSTRAINT [FK_UserClaims_Users] FOREIGN KEY ([UserId]) REFERENCES [core].[Users]([Id]));
GO
CREATE TABLE [core].[UserLogins] ([LoginProvider] NVARCHAR(450) NOT NULL, [ProviderKey] NVARCHAR(450) NOT NULL, [ProviderDisplayName] NVARCHAR(MAX) NULL, [UserId] UNIQUEIDENTIFIER NOT NULL, CONSTRAINT [PK_UserLogins] PRIMARY KEY ([LoginProvider],[ProviderKey]), CONSTRAINT [FK_UserLogins_Users] FOREIGN KEY ([UserId]) REFERENCES [core].[Users]([Id]));
GO
CREATE TABLE [core].[UserTokens] ([UserId] UNIQUEIDENTIFIER NOT NULL, [LoginProvider] NVARCHAR(450) NOT NULL, [Name] NVARCHAR(450) NOT NULL, [Value] NVARCHAR(MAX) NULL, CONSTRAINT [PK_UserTokens] PRIMARY KEY ([UserId],[LoginProvider],[Name]), CONSTRAINT [FK_UserTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [core].[Users]([Id]));
GO
CREATE TABLE [core].[RefreshTokens] ([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [UserId] UNIQUEIDENTIFIER NOT NULL, [TokenHash] NVARCHAR(128) NOT NULL, [ExpiresOn] DATETIMEOFFSET NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RevokedOn] DATETIMEOFFSET NULL, [ReplacedByTokenHash] NVARCHAR(128) NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [core].[Users]([Id]));
GO
CREATE UNIQUE INDEX [UX_RefreshTokens_TokenHash] ON [core].[RefreshTokens]([TokenHash]);
