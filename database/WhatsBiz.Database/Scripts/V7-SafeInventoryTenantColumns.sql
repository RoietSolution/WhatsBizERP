/* Phase 1 inventory ownership. Nullable during rollout; deterministic only. */
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
IF COL_LENGTH('inventory.InventoryBalances','TenantId') IS NULL
BEGIN
    EXEC(N'ALTER TABLE inventory.InventoryBalances ADD TenantId UNIQUEIDENTIFIER NULL;');
    EXEC(N'ALTER TABLE inventory.InventoryBalances ADD CONSTRAINT FK_InventoryBalances_Tenant FOREIGN KEY (TenantId) REFERENCES core.Tenants(TenantId);');
    EXEC(N'CREATE INDEX IX_InventoryBalances_Tenant_Product ON inventory.InventoryBalances(TenantId,ProductId,WarehouseId);');
END;
IF COL_LENGTH('inventory.InventoryTransactions','TenantId') IS NULL
BEGIN
    EXEC(N'ALTER TABLE inventory.InventoryTransactions ADD TenantId UNIQUEIDENTIFIER NULL;');
    EXEC(N'ALTER TABLE inventory.InventoryTransactions ADD CONSTRAINT FK_InventoryTransactions_Tenant FOREIGN KEY (TenantId) REFERENCES core.Tenants(TenantId);');
    EXEC(N'CREATE INDEX IX_InventoryTransactions_Tenant_Date ON inventory.InventoryTransactions(TenantId,CreatedOn);');
END;
GO
UPDATE b SET TenantId=p.TenantId FROM inventory.InventoryBalances b JOIN master.Products p ON p.ProductId=b.ProductId WHERE b.TenantId IS NULL;
IF EXISTS (SELECT 1 FROM inventory.InventoryBalances WHERE TenantId IS NULL) THROW 51010,'Inventory balance ownership could not be deterministically inferred.',1;
UPDATE t SET TenantId=x.TenantId FROM inventory.InventoryTransactions t JOIN (SELECT d.TransactionId,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),p.TenantId))) TenantId FROM inventory.InventoryTransactionDetails d JOIN master.Products p ON p.ProductId=d.ProductId GROUP BY d.TransactionId HAVING COUNT(DISTINCT p.TenantId)=1) x ON x.TransactionId=t.TransactionId WHERE t.TenantId IS NULL;
IF EXISTS (SELECT 1 FROM inventory.InventoryTransactions WHERE TenantId IS NULL) THROW 51011,'Inventory transaction ownership is ambiguous or orphaned.',1;
