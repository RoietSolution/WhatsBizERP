/* Tenant ownership phase 1. Nullable, idempotent, no default assignment. */
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
IF COL_LENGTH('purchase.Suppliers','TenantId') IS NULL EXEC(N'ALTER TABLE purchase.Suppliers ADD TenantId UNIQUEIDENTIFIER NULL;');
IF COL_LENGTH('inventory.Warehouses','TenantId') IS NULL EXEC(N'ALTER TABLE inventory.Warehouses ADD TenantId UNIQUEIDENTIFIER NULL;');
IF COL_LENGTH('sales.SalesInvoices','TenantId') IS NULL EXEC(N'ALTER TABLE sales.SalesInvoices ADD TenantId UNIQUEIDENTIFIER NULL;');
IF COL_LENGTH('purchase.PurchaseInvoices','TenantId') IS NULL EXEC(N'ALTER TABLE purchase.PurchaseInvoices ADD TenantId UNIQUEIDENTIFIER NULL;');
IF COL_LENGTH('finance.JournalEntries','TenantId') IS NULL EXEC(N'ALTER TABLE finance.JournalEntries ADD TenantId UNIQUEIDENTIFIER NULL;');
GO
BEGIN TRY
 BEGIN TRAN;
 ;WITH u AS (SELECT UserName,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),TenantId))) TenantId FROM core.Users WHERE TenantId IS NOT NULL GROUP BY UserName HAVING COUNT(DISTINCT TenantId)=1)
 UPDATE i SET TenantId=u.TenantId FROM sales.SalesInvoices i JOIN u ON u.UserName=i.CreatedBy WHERE i.TenantId IS NULL;
 ;WITH u AS (SELECT UserName,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),TenantId))) TenantId FROM core.Users WHERE TenantId IS NOT NULL GROUP BY UserName HAVING COUNT(DISTINCT TenantId)=1)
 UPDATE i SET TenantId=u.TenantId FROM purchase.PurchaseInvoices i JOIN u ON u.UserName=i.CreatedBy WHERE i.TenantId IS NULL;
 ;WITH u AS (SELECT UserName,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),TenantId))) TenantId FROM core.Users WHERE TenantId IS NOT NULL GROUP BY UserName HAVING COUNT(DISTINCT TenantId)=1)
 UPDATE s SET TenantId=u.TenantId FROM purchase.Suppliers s JOIN u ON u.UserName=s.CreatedBy WHERE s.TenantId IS NULL;
 ;WITH p AS (SELECT SupplierId,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),TenantId))) TenantId FROM purchase.PurchaseInvoices WHERE TenantId IS NOT NULL GROUP BY SupplierId HAVING COUNT(DISTINCT TenantId)=1)
 UPDATE s SET TenantId=p.TenantId FROM purchase.Suppliers s JOIN p ON p.SupplierId=s.SupplierId WHERE s.TenantId IS NULL;
 ;WITH u AS (SELECT UserName,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),TenantId))) TenantId FROM core.Users WHERE TenantId IS NOT NULL GROUP BY UserName HAVING COUNT(DISTINCT TenantId)=1)
 UPDATE w SET TenantId=u.TenantId FROM inventory.Warehouses w JOIN u ON u.UserName=w.CreatedBy WHERE w.TenantId IS NULL;
 ;WITH b AS (SELECT b.WarehouseId,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),p.TenantId))) TenantId FROM inventory.InventoryBalances b JOIN master.Products p ON p.ProductId=b.ProductId WHERE p.TenantId IS NOT NULL GROUP BY b.WarehouseId HAVING COUNT(DISTINCT p.TenantId)=1)
 UPDATE w SET TenantId=b.TenantId FROM inventory.Warehouses w JOIN b ON b.WarehouseId=w.WarehouseId WHERE w.TenantId IS NULL;
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Suppliers_Tenants') ALTER TABLE purchase.Suppliers ADD CONSTRAINT FK_Suppliers_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Warehouses_Tenants') ALTER TABLE inventory.Warehouses ADD CONSTRAINT FK_Warehouses_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_SalesInvoices_Tenants') ALTER TABLE sales.SalesInvoices ADD CONSTRAINT FK_SalesInvoices_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_PurchaseInvoices_Tenants') ALTER TABLE purchase.PurchaseInvoices ADD CONSTRAINT FK_PurchaseInvoices_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_JournalEntries_Tenants') ALTER TABLE finance.JournalEntries ADD CONSTRAINT FK_JournalEntries_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Suppliers_TenantId' AND object_id=OBJECT_ID('purchase.Suppliers')) CREATE INDEX IX_Suppliers_TenantId ON purchase.Suppliers(TenantId,SupplierId);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Warehouses_TenantId' AND object_id=OBJECT_ID('inventory.Warehouses')) CREATE INDEX IX_Warehouses_TenantId ON inventory.Warehouses(TenantId,WarehouseId);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SalesInvoices_TenantId_Date' AND object_id=OBJECT_ID('sales.SalesInvoices')) CREATE INDEX IX_SalesInvoices_TenantId_Date ON sales.SalesInvoices(TenantId,InvoiceDate);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_PurchaseInvoices_TenantId_Date' AND object_id=OBJECT_ID('purchase.PurchaseInvoices')) CREATE INDEX IX_PurchaseInvoices_TenantId_Date ON purchase.PurchaseInvoices(TenantId,InvoiceDate);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_JournalEntries_TenantId_Date' AND object_id=OBJECT_ID('finance.JournalEntries')) CREATE INDEX IX_JournalEntries_TenantId_Date ON finance.JournalEntries(TenantId,EntryDate);
 COMMIT;
END TRY
BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
SELECT TableName,TotalRows,OwnedRows,UnresolvedRows FROM (SELECT 'purchase.Suppliers' TableName,COUNT_BIG(*) TotalRows,COUNT_BIG(TenantId) OwnedRows,COUNT_BIG(*)-COUNT_BIG(TenantId) UnresolvedRows FROM purchase.Suppliers UNION ALL SELECT 'inventory.Warehouses',COUNT_BIG(*),COUNT_BIG(TenantId),COUNT_BIG(*)-COUNT_BIG(TenantId) FROM inventory.Warehouses UNION ALL SELECT 'sales.SalesInvoices',COUNT_BIG(*),COUNT_BIG(TenantId),COUNT_BIG(*)-COUNT_BIG(TenantId) FROM sales.SalesInvoices UNION ALL SELECT 'purchase.PurchaseInvoices',COUNT_BIG(*),COUNT_BIG(TenantId),COUNT_BIG(*)-COUNT_BIG(TenantId) FROM purchase.PurchaseInvoices UNION ALL SELECT 'finance.JournalEntries',COUNT_BIG(*),COUNT_BIG(TenantId),COUNT_BIG(*)-COUNT_BIG(TenantId) FROM finance.JournalEntries) r;
