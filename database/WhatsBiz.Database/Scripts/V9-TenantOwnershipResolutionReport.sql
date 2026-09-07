/* Read-only Phase 2 evidence report. No rows are changed. */
SET NOCOUNT ON;

SELECT s.SupplierId, s.SupplierName,
       COUNT(DISTINCT p.PurchaseInvoiceId) PurchaseCount,
       STRING_AGG(CONVERT(nvarchar(256), p.CreatedBy), N', ') CreatedByUsers,
       COUNT(DISTINCT pi.TenantId) PurchaseTenantCount,
       STRING_AGG(CONVERT(nvarchar(36), pi.TenantId), N', ') PossibleTenantIds
FROM purchase.Suppliers s
LEFT JOIN purchase.PurchaseInvoices p ON p.SupplierId=s.SupplierId
LEFT JOIN purchase.PurchaseInvoices pi ON pi.SupplierId=s.SupplierId AND pi.TenantId IS NOT NULL
WHERE s.TenantId IS NULL
GROUP BY s.SupplierId, s.SupplierName;

SELECT w.WarehouseId, w.WarehouseName,
       COUNT(DISTINCT b.InventoryBalanceId) BalanceCount,
       COUNT(DISTINCT p.TenantId) ProductTenantCount,
       STRING_AGG(CONVERT(nvarchar(36), p.TenantId), N', ') ProductTenantIds
FROM inventory.Warehouses w
LEFT JOIN inventory.InventoryBalances b ON b.WarehouseId=w.WarehouseId
LEFT JOIN master.Products p ON p.ProductId=b.ProductId
WHERE w.TenantId IS NULL
GROUP BY w.WarehouseId, w.WarehouseName;

/* Finance Phase 3 evidence: source types and whether a tenant can be
   deterministically resolved through the now-owned source headers. */
SELECT j.ReferenceType SourceType,
       COUNT(*) JournalCount,
       SUM(CASE WHEN COALESCE(si.TenantId, pi.TenantId) IS NOT NULL THEN 1 ELSE 0 END) DeterministicCount,
       SUM(CASE WHEN COALESCE(si.TenantId, pi.TenantId) IS NULL THEN 1 ELSE 0 END) UnresolvedCount
FROM finance.JournalEntries j
LEFT JOIN sales.SalesInvoices si ON j.ReferenceId=si.InvoiceId
LEFT JOIN purchase.PurchaseInvoices pi ON j.ReferenceId=pi.PurchaseInvoiceId
GROUP BY j.ReferenceType
ORDER BY j.ReferenceType;

SELECT 'sales.SalesInvoices' TableName, COUNT(*) UnresolvedRows FROM sales.SalesInvoices WHERE TenantId IS NULL
UNION ALL SELECT 'purchase.PurchaseInvoices', COUNT(*) FROM purchase.PurchaseInvoices WHERE TenantId IS NULL
UNION ALL SELECT 'purchase.Suppliers', COUNT(*) FROM purchase.Suppliers WHERE TenantId IS NULL
UNION ALL SELECT 'inventory.Warehouses', COUNT(*) FROM inventory.Warehouses WHERE TenantId IS NULL
UNION ALL SELECT 'finance.JournalEntries', COUNT(*) FROM finance.JournalEntries WHERE TenantId IS NULL;

IF EXISTS (SELECT 1 FROM sales.SalesInvoices WHERE TenantId IS NULL)
   OR EXISTS (SELECT 1 FROM purchase.PurchaseInvoices WHERE TenantId IS NULL)
   OR EXISTS (SELECT 1 FROM purchase.Suppliers WHERE TenantId IS NULL)
   OR EXISTS (SELECT 1 FROM inventory.Warehouses WHERE TenantId IS NULL)
    THROW 51610, N'Tenant ownership audit failed: unresolved operational rows remain.', 1;
