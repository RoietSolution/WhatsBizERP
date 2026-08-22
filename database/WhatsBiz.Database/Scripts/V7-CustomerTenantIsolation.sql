SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'sales.Customers', N'TenantId') IS NULL
    ALTER TABLE [sales].[Customers] ADD [TenantId] UNIQUEIDENTIFIER NULL;
GO

DECLARE @TenantCount INT=(SELECT COUNT(1) FROM [core].[Tenants] WHERE [IsActive]=1);
DECLARE @OnlyTenant UNIQUEIDENTIFIER=(SELECT TOP(1) [TenantId] FROM [core].[Tenants] WHERE [IsActive]=1);
IF @TenantCount=1
    UPDATE [sales].[Customers] SET [TenantId]=@OnlyTenant WHERE [TenantId] IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Customers_Tenant' AND parent_object_id=OBJECT_ID(N'sales.Customers'))
    ALTER TABLE [sales].[Customers] ADD CONSTRAINT [FK_Customers_Tenant] FOREIGN KEY([TenantId]) REFERENCES [core].[Tenants]([TenantId]) ON DELETE NO ACTION;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Customers_TenantCustomer' AND object_id=OBJECT_ID(N'sales.Customers'))
    CREATE INDEX [IX_Customers_TenantCustomer] ON [sales].[Customers]([TenantId],[CustomerId]) WHERE [IsDeleted]=0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Customers_TenantMobile' AND object_id=OBJECT_ID(N'sales.Customers'))
    CREATE INDEX [IX_Customers_TenantMobile] ON [sales].[Customers]([TenantId],[Mobile]) WHERE [IsDeleted]=0 AND [Mobile] IS NOT NULL;
COMMIT TRANSACTION;
