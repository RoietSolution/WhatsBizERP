SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF SCHEMA_ID(N'commerce') IS NULL EXEC(N'CREATE SCHEMA [commerce]');
IF OBJECT_ID(N'commerce.ProductChannelMappings', N'U') IS NULL
BEGIN
    CREATE TABLE [commerce].[ProductChannelMappings](
        [MappingId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_ProductChannelMappings] PRIMARY KEY,
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [ProductId] UNIQUEIDENTIFIER NOT NULL,
        [Provider] NVARCHAR(30) NOT NULL,
        [CatalogId] NVARCHAR(200) NULL,
        [ExternalProductId] NVARCHAR(200) NULL,
        [SyncStatus] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ProductChannelMappings_Status] DEFAULT(N'NOT_MAPPED'),
        [LastSyncedAt] DATETIMEOFFSET NULL,
        [LastError] NVARCHAR(1000) NULL,
        [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_ProductChannelMappings_CreatedOn] DEFAULT(SYSUTCDATETIME()),
        [CreatedBy] NVARCHAR(256) NULL,
        [ModifiedOn] DATETIMEOFFSET NULL,
        [ModifiedBy] NVARCHAR(256) NULL,
        CONSTRAINT [CK_ProductChannelMappings_Status] CHECK([SyncStatus] IN (N'NOT_MAPPED',N'MAPPED',N'PENDING',N'FAILED')),
        CONSTRAINT [FK_ProductChannelMappings_Tenants] FOREIGN KEY([TenantId]) REFERENCES [core].[Tenants]([TenantId]),
        CONSTRAINT [FK_ProductChannelMappings_Products] FOREIGN KEY([ProductId]) REFERENCES [master].[Products]([ProductId]) ON DELETE NO ACTION
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProductChannelMappings_Product' AND object_id=OBJECT_ID(N'commerce.ProductChannelMappings'))
    CREATE UNIQUE INDEX [UX_ProductChannelMappings_Product] ON [commerce].[ProductChannelMappings]([TenantId],[Provider],[ProductId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProductChannelMappings_External' AND object_id=OBJECT_ID(N'commerce.ProductChannelMappings'))
    CREATE UNIQUE INDEX [UX_ProductChannelMappings_External] ON [commerce].[ProductChannelMappings]([TenantId],[Provider],[CatalogId],[ExternalProductId]) WHERE [ExternalProductId] IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductChannelMappings_Status' AND object_id=OBJECT_ID(N'commerce.ProductChannelMappings'))
    CREATE INDEX [IX_ProductChannelMappings_Status] ON [commerce].[ProductChannelMappings]([TenantId],[Provider],[SyncStatus]);
COMMIT TRANSACTION;
