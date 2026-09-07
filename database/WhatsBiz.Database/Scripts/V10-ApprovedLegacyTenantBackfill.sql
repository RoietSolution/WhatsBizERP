/* Phase 2 controlled backfill. Approved legacy tenant only. Rerunnable. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
DECLARE @LegacyTenant UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
IF NOT EXISTS (SELECT 1 FROM core.Tenants WHERE TenantId=@LegacyTenant)
    THROW 51020, 'Approved legacy tenant does not exist; no backfill was performed.', 1;

DECLARE @Before TABLE(TableName sysname, NullRows bigint);
INSERT @Before SELECT 'purchase.Suppliers', COUNT_BIG(*) FROM purchase.Suppliers WHERE TenantId IS NULL;
INSERT @Before SELECT 'inventory.Warehouses', COUNT_BIG(*) FROM inventory.Warehouses WHERE TenantId IS NULL;
INSERT @Before SELECT 'sales.SalesInvoices', COUNT_BIG(*) FROM sales.SalesInvoices WHERE TenantId IS NULL;
INSERT @Before SELECT 'purchase.PurchaseInvoices', COUNT_BIG(*) FROM purchase.PurchaseInvoices WHERE TenantId IS NULL;
INSERT @Before SELECT 'inventory.InventoryBalances', COUNT_BIG(*) FROM inventory.InventoryBalances WHERE TenantId IS NULL;
INSERT @Before SELECT 'inventory.InventoryTransactions', COUNT_BIG(*) FROM inventory.InventoryTransactions WHERE TenantId IS NULL;
INSERT @Before SELECT 'finance.JournalEntries', COUNT_BIG(*) FROM finance.JournalEntries WHERE TenantId IS NULL;

BEGIN TRY
 BEGIN TRAN;
 /* Deterministic ownership first. */
 ;WITH u AS (SELECT UserName, CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),TenantId))) TenantId FROM core.Users WHERE TenantId IS NOT NULL GROUP BY UserName HAVING COUNT(DISTINCT TenantId)=1)
 UPDATE i SET TenantId=u.TenantId FROM sales.SalesInvoices i JOIN u ON u.UserName=i.CreatedBy WHERE i.TenantId IS NULL;
 ;WITH u AS (SELECT UserName, CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),TenantId))) TenantId FROM core.Users WHERE TenantId IS NOT NULL GROUP BY UserName HAVING COUNT(DISTINCT TenantId)=1)
 UPDATE i SET TenantId=u.TenantId FROM purchase.PurchaseInvoices i JOIN u ON u.UserName=i.CreatedBy WHERE i.TenantId IS NULL;
 UPDATE b SET TenantId=p.TenantId FROM inventory.InventoryBalances b JOIN master.Products p ON p.ProductId=b.ProductId WHERE b.TenantId IS NULL AND p.TenantId IS NOT NULL;
 UPDATE t SET TenantId=x.TenantId FROM inventory.InventoryTransactions t JOIN (SELECT d.TransactionId,CONVERT(uniqueidentifier,MAX(CONVERT(varchar(36),p.TenantId))) TenantId FROM inventory.InventoryTransactionDetails d JOIN master.Products p ON p.ProductId=d.ProductId GROUP BY d.TransactionId HAVING COUNT(DISTINCT p.TenantId)=1) x ON x.TransactionId=t.TransactionId WHERE t.TenantId IS NULL;
 /* Approved legacy fallback applies only to rows still unowned. */
 UPDATE purchase.Suppliers SET TenantId=@LegacyTenant WHERE TenantId IS NULL;
 UPDATE inventory.Warehouses SET TenantId=@LegacyTenant WHERE TenantId IS NULL;
 UPDATE sales.SalesInvoices SET TenantId=@LegacyTenant WHERE TenantId IS NULL;
 UPDATE purchase.PurchaseInvoices SET TenantId=@LegacyTenant WHERE TenantId IS NULL;
 UPDATE inventory.InventoryBalances SET TenantId=@LegacyTenant WHERE TenantId IS NULL;
 UPDATE inventory.InventoryTransactions SET TenantId=@LegacyTenant WHERE TenantId IS NULL;
 /* Finance remains audit-only until source ownership is approved. */
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;

SELECT b.TableName,b.NullRows BeforeNullRows,COUNT_BIG(s.TenantId) * 0 + SUM(CASE WHEN s.TenantId IS NULL THEN 1 ELSE 0 END) AfterNullRows,b.NullRows-SUM(CASE WHEN s.TenantId IS NULL THEN 1 ELSE 0 END) AssignedRows FROM @Before b CROSS JOIN purchase.Suppliers s WHERE b.TableName='purchase.Suppliers' GROUP BY b.TableName,b.NullRows
UNION ALL SELECT b.TableName,b.NullRows, SUM(CASE WHEN w.TenantId IS NULL THEN 1 ELSE 0 END), b.NullRows-SUM(CASE WHEN w.TenantId IS NULL THEN 1 ELSE 0 END) FROM @Before b CROSS JOIN inventory.Warehouses w WHERE b.TableName='inventory.Warehouses' GROUP BY b.TableName,b.NullRows
UNION ALL SELECT b.TableName,b.NullRows, SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END), b.NullRows-SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END) FROM @Before b CROSS JOIN sales.SalesInvoices i WHERE b.TableName='sales.SalesInvoices' GROUP BY b.TableName,b.NullRows
UNION ALL SELECT b.TableName,b.NullRows, SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END), b.NullRows-SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END) FROM @Before b CROSS JOIN purchase.PurchaseInvoices i WHERE b.TableName='purchase.PurchaseInvoices' GROUP BY b.TableName,b.NullRows
UNION ALL SELECT b.TableName,b.NullRows, SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END), b.NullRows-SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END) FROM @Before b CROSS JOIN inventory.InventoryBalances i WHERE b.TableName='inventory.InventoryBalances' GROUP BY b.TableName,b.NullRows
UNION ALL SELECT b.TableName,b.NullRows, SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END), b.NullRows-SUM(CASE WHEN i.TenantId IS NULL THEN 1 ELSE 0 END) FROM @Before b CROSS JOIN inventory.InventoryTransactions i WHERE b.TableName='inventory.InventoryTransactions' GROUP BY b.TableName,b.NullRows;
