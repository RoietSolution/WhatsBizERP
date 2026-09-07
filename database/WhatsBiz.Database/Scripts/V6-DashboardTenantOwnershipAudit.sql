/* Read-only pre/post migration audit. Never assigns a default tenant. */
SELECT 'Products without tenant' AS CheckName, COUNT_BIG(*) AS RowsRemaining FROM master.Products WHERE TenantId IS NULL
UNION ALL SELECT 'Customers without tenant', COUNT_BIG(*) FROM sales.Customers WHERE TenantId IS NULL
UNION ALL SELECT 'Sales invoices lacking deterministic customer owner', COUNT_BIG(*) FROM sales.SalesInvoices i LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE i.CustomerId IS NULL OR c.TenantId IS NULL
UNION ALL SELECT 'Purchase invoices lacking deterministic supplier owner', COUNT_BIG(*) FROM purchase.PurchaseInvoices i LEFT JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId WHERE s.SupplierId IS NULL;

/* These tables have no safe ownership source in the current schema and must be resolved before enforcement. */
SELECT t.name AS TableName, c.name AS ColumnName
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.name='TenantId'
WHERE (s.name+'.'+t.name) IN ('sales.SalesInvoices','sales.SalesPayments','purchase.PurchaseInvoices','purchase.PurchasePayments','purchase.PurchaseReturns','purchase.Suppliers','inventory.Warehouses','inventory.InventoryBalances','finance.CashBook','finance.BankBook');
