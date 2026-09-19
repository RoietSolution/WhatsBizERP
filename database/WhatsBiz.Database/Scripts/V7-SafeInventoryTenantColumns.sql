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
/* Fresh production already receives the tenant columns and guards from the
   DACPAC, and has no legacy ownership to backfill. Skip these UPDATEs because
   SQL Server fires AFTER triggers even when an UPDATE affects zero rows; the
   tenant guards correctly require SESSION_CONTEXT for any such statement. */
IF N'$(FreshProductionInitialization)' <> N'True'
BEGIN
/* The operational tenant guards are already installed on existing QA
   databases. Backfill one tenant at a time so each guarded write carries
   the matching SESSION_CONTEXT value; never bypass the guard for migration. */
DECLARE @TenantId uniqueidentifier;
DECLARE balance_tenants CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT p.TenantId
    FROM inventory.InventoryBalances b
    JOIN master.Products p ON p.ProductId=b.ProductId
    WHERE b.TenantId IS NULL AND p.TenantId IS NOT NULL;
OPEN balance_tenants;
FETCH NEXT FROM balance_tenants INTO @TenantId;
WHILE @@FETCH_STATUS=0
BEGIN
    EXEC sys.sp_set_session_context @key=N'TenantId', @value=@TenantId;
    UPDATE b SET TenantId=@TenantId
    FROM inventory.InventoryBalances b
    JOIN master.Products p ON p.ProductId=b.ProductId AND p.TenantId=@TenantId
    WHERE b.TenantId IS NULL;
    FETCH NEXT FROM balance_tenants INTO @TenantId;
END;
CLOSE balance_tenants; DEALLOCATE balance_tenants;
EXEC sys.sp_set_session_context @key=N'TenantId', @value=NULL;
IF EXISTS (SELECT 1 FROM inventory.InventoryBalances WHERE TenantId IS NULL) THROW 51010,'Inventory balance ownership could not be deterministically inferred.',1;
DECLARE transaction_tenants CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT x.TenantId
    FROM inventory.InventoryTransactions t
    JOIN (SELECT d.TransactionId,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),p.TenantId))) TenantId
          FROM inventory.InventoryTransactionDetails d
          JOIN master.Products p ON p.ProductId=d.ProductId
          WHERE p.TenantId IS NOT NULL
          GROUP BY d.TransactionId HAVING COUNT(DISTINCT p.TenantId)=1) x ON x.TransactionId=t.TransactionId
    WHERE t.TenantId IS NULL;
OPEN transaction_tenants;
FETCH NEXT FROM transaction_tenants INTO @TenantId;
WHILE @@FETCH_STATUS=0
BEGIN
    EXEC sys.sp_set_session_context @key=N'TenantId', @value=@TenantId;
    UPDATE t SET TenantId=@TenantId
    FROM inventory.InventoryTransactions t
    JOIN (SELECT d.TransactionId
          FROM inventory.InventoryTransactionDetails d
          JOIN master.Products p ON p.ProductId=d.ProductId
          WHERE p.TenantId=@TenantId
          GROUP BY d.TransactionId HAVING COUNT(DISTINCT p.TenantId)=1) x ON x.TransactionId=t.TransactionId
    WHERE t.TenantId IS NULL;
    FETCH NEXT FROM transaction_tenants INTO @TenantId;
END;
CLOSE transaction_tenants; DEALLOCATE transaction_tenants;
EXEC sys.sp_set_session_context @key=N'TenantId', @value=NULL;
IF EXISTS (SELECT 1 FROM inventory.InventoryTransactions WHERE TenantId IS NULL) THROW 51011,'Inventory transaction ownership is ambiguous or orphaned.',1;
END;
GO
