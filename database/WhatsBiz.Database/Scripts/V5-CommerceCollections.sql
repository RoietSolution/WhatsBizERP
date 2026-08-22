SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF SCHEMA_ID(N'commerce') IS NULL EXEC(N'CREATE SCHEMA [commerce]');

IF OBJECT_ID(N'commerce.Collections', N'U') IS NULL
BEGIN
    CREATE TABLE [commerce].[Collections](
        [CollectionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_Collections] PRIMARY KEY,
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [Name] NVARCHAR(200) NOT NULL,
        [Slug] NVARCHAR(220) NOT NULL,
        [Description] NVARCHAR(1000) NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_Collections_IsActive] DEFAULT(1),
        [DisplayOrder] INT NOT NULL CONSTRAINT [DF_Collections_DisplayOrder] DEFAULT(0),
        [StartDate] DATETIMEOFFSET NULL,
        [EndDate] DATETIMEOFFSET NULL,
        [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_Collections_CreatedOn] DEFAULT(SYSUTCDATETIME()),
        [CreatedBy] NVARCHAR(256) NULL,
        [ModifiedOn] DATETIMEOFFSET NULL,
        [ModifiedBy] NVARCHAR(256) NULL,
        [IsDeleted] BIT NOT NULL CONSTRAINT [DF_Collections_IsDeleted] DEFAULT(0),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT [CK_Collections_Dates] CHECK([EndDate] IS NULL OR [StartDate] IS NULL OR [EndDate] >= [StartDate]),
        CONSTRAINT [FK_Collections_Tenants] FOREIGN KEY([TenantId]) REFERENCES [core].[Tenants]([TenantId])
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Collections_TenantName' AND object_id = OBJECT_ID(N'commerce.Collections'))
    CREATE UNIQUE INDEX [UX_Collections_TenantName] ON [commerce].[Collections]([TenantId], [Name]) WHERE [IsDeleted] = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Collections_TenantSlug' AND object_id = OBJECT_ID(N'commerce.Collections'))
    CREATE UNIQUE INDEX [UX_Collections_TenantSlug] ON [commerce].[Collections]([TenantId], [Slug]) WHERE [IsDeleted] = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Collections_TenantActiveOrder' AND object_id = OBJECT_ID(N'commerce.Collections'))
    CREATE INDEX [IX_Collections_TenantActiveOrder] ON [commerce].[Collections]([TenantId], [IsActive], [DisplayOrder], [Name]) WHERE [IsDeleted] = 0;
GO

IF OBJECT_ID(N'commerce.CollectionProducts', N'U') IS NULL
BEGIN
    CREATE TABLE [commerce].[CollectionProducts](
        [CollectionProductId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_CollectionProducts] PRIMARY KEY,
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [CollectionId] UNIQUEIDENTIFIER NOT NULL,
        [ProductId] UNIQUEIDENTIFIER NOT NULL,
        [DisplayOrder] INT NOT NULL CONSTRAINT [DF_CollectionProducts_DisplayOrder] DEFAULT(0),
        [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_CollectionProducts_CreatedOn] DEFAULT(SYSUTCDATETIME()),
        [CreatedBy] NVARCHAR(256) NULL,
        [ModifiedOn] DATETIMEOFFSET NULL,
        [ModifiedBy] NVARCHAR(256) NULL,
        CONSTRAINT [FK_CollectionProducts_Tenants] FOREIGN KEY([TenantId]) REFERENCES [core].[Tenants]([TenantId]),
        CONSTRAINT [FK_CollectionProducts_Collections] FOREIGN KEY([CollectionId]) REFERENCES [commerce].[Collections]([CollectionId]) ON DELETE CASCADE,
        CONSTRAINT [FK_CollectionProducts_Products] FOREIGN KEY([ProductId]) REFERENCES [master].[Products]([ProductId]) ON DELETE NO ACTION
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_CollectionProducts_Membership' AND object_id = OBJECT_ID(N'commerce.CollectionProducts'))
    CREATE UNIQUE INDEX [UX_CollectionProducts_Membership] ON [commerce].[CollectionProducts]([TenantId], [CollectionId], [ProductId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CollectionProducts_CollectionOrder' AND object_id = OBJECT_ID(N'commerce.CollectionProducts'))
    CREATE INDEX [IX_CollectionProducts_CollectionOrder] ON [commerce].[CollectionProducts]([TenantId], [CollectionId], [DisplayOrder], [ProductId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CollectionProducts_Product' AND object_id = OBJECT_ID(N'commerce.CollectionProducts'))
    CREATE INDEX [IX_CollectionProducts_Product] ON [commerce].[CollectionProducts]([TenantId], [ProductId]);
GO
COMMIT TRANSACTION;
