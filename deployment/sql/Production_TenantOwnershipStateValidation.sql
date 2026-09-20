/*
   Read-only current-state ownership validation for production deployment.
   Historical migration numbers are deliberately not used as the decision.
   The result is one machine-readable line consumed by deploy-prod-database.ps1.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'WhatsBizERP_PROD'
    THROW 51920, N'Production ownership validation may run only in WhatsBizERP_PROD.', 1;

DECLARE @Required TABLE (ObjectName sysname NOT NULL PRIMARY KEY);
INSERT @Required(ObjectName) VALUES
 (N'master.Products'),
 (N'sales.Customers'),
 (N'purchase.Suppliers'),
 (N'inventory.Warehouses'),
 (N'sales.SalesInvoices'),
 (N'purchase.PurchaseInvoices'),
 (N'inventory.InventoryBalances'),
 (N'inventory.InventoryTransactions'),
 (N'finance.JournalEntries');

DECLARE @MissingColumns TABLE (ObjectName sysname NOT NULL);
DECLARE @Ownership TABLE
(
    ObjectName sysname NOT NULL,
    UnownedRows bigint NOT NULL,
    OrphanTenantRows bigint NOT NULL
);

DECLARE @ObjectName sysname;
DECLARE ownership_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT ObjectName FROM @Required;
OPEN ownership_cursor;
FETCH NEXT FROM ownership_cursor INTO @ObjectName;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(@ObjectName, N'U') IS NULL OR COL_LENGTH(@ObjectName, N'TenantId') IS NULL
    BEGIN
        INSERT @MissingColumns(ObjectName) VALUES (@ObjectName);
    END
    ELSE
    BEGIN
        DECLARE @SchemaName sysname = PARSENAME(@ObjectName, 2);
        DECLARE @TableName sysname = PARSENAME(@ObjectName, 1);
        DECLARE @Sql nvarchar(max) = N'
            SELECT @Unowned = COUNT_BIG(CASE WHEN x.TenantId IS NULL THEN 1 END),
                   @Orphan = COUNT_BIG(CASE WHEN x.TenantId IS NOT NULL AND t.TenantId IS NULL THEN 1 END)
            FROM ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N' x
            LEFT JOIN core.Tenants t ON t.TenantId = x.TenantId;';
        DECLARE @Unowned bigint = 0, @Orphan bigint = 0;
        EXEC sys.sp_executesql @Sql,
            N'@Unowned bigint OUTPUT, @Orphan bigint OUTPUT',
            @Unowned OUTPUT, @Orphan OUTPUT;
        INSERT @Ownership(ObjectName, UnownedRows, OrphanTenantRows)
            VALUES (@ObjectName, COALESCE(@Unowned, 0), COALESCE(@Orphan, 0));
    END;
    FETCH NEXT FROM ownership_cursor INTO @ObjectName;
END;
CLOSE ownership_cursor;
DEALLOCATE ownership_cursor;

DECLARE @CrossTenantRows bigint = 0;
IF OBJECT_ID(N'purchase.PurchaseInvoices', N'U') IS NOT NULL
   AND OBJECT_ID(N'purchase.Suppliers', N'U') IS NOT NULL
   AND COL_LENGTH(N'purchase.PurchaseInvoices', N'TenantId') IS NOT NULL
   AND COL_LENGTH(N'purchase.Suppliers', N'TenantId') IS NOT NULL
    SELECT @CrossTenantRows += COUNT_BIG(*)
    FROM purchase.PurchaseInvoices i
    JOIN purchase.Suppliers s ON s.SupplierId = i.SupplierId
    WHERE i.TenantId <> s.TenantId;

IF OBJECT_ID(N'sales.SalesInvoices', N'U') IS NOT NULL
   AND OBJECT_ID(N'sales.Customers', N'U') IS NOT NULL
   AND COL_LENGTH(N'sales.SalesInvoices', N'TenantId') IS NOT NULL
   AND COL_LENGTH(N'sales.Customers', N'TenantId') IS NOT NULL
    SELECT @CrossTenantRows += COUNT_BIG(*)
    FROM sales.SalesInvoices i
    JOIN sales.Customers c ON c.CustomerId = i.CustomerId
    WHERE i.CustomerId IS NOT NULL AND i.TenantId <> c.TenantId;

IF OBJECT_ID(N'inventory.InventoryBalances', N'U') IS NOT NULL
   AND OBJECT_ID(N'master.Products', N'U') IS NOT NULL
   AND COL_LENGTH(N'inventory.InventoryBalances', N'TenantId') IS NOT NULL
   AND COL_LENGTH(N'master.Products', N'TenantId') IS NOT NULL
    SELECT @CrossTenantRows += COUNT_BIG(*)
    FROM inventory.InventoryBalances b
    JOIN master.Products p ON p.ProductId = b.ProductId
    WHERE b.TenantId <> p.TenantId;

DECLARE @JournalOwnershipIssues bigint = 0;
IF OBJECT_ID(N'finance.JournalEntries', N'U') IS NOT NULL
   AND COL_LENGTH(N'finance.JournalEntries', N'TenantId') IS NOT NULL
BEGIN
    SELECT @JournalOwnershipIssues = COUNT_BIG(*)
    FROM finance.JournalEntries j
    LEFT JOIN sales.SalesInvoices si ON j.ReferenceType IN (N'SALE', N'SALE_RETURN') AND si.InvoiceId = j.ReferenceId
    LEFT JOIN purchase.PurchaseInvoices pi ON j.ReferenceType IN (N'PURCHASE', N'PURCHASE_RETURN') AND pi.PurchaseInvoiceId = j.ReferenceId
    WHERE j.TenantId IS NULL
       OR (si.InvoiceId IS NOT NULL AND si.TenantId <> j.TenantId)
       OR (pi.PurchaseInvoiceId IS NOT NULL AND pi.TenantId <> j.TenantId);
END;

DECLARE @UnownedRows bigint = COALESCE((SELECT SUM(UnownedRows) FROM @Ownership), 0);
DECLARE @OrphanRows bigint = COALESCE((SELECT SUM(OrphanTenantRows) FROM @Ownership), 0);
DECLARE @MissingCount int = (SELECT COUNT(*) FROM @MissingColumns);
DECLARE @State nvarchar(20) = CASE
    WHEN @MissingCount > 0 OR @UnownedRows > 0 OR @OrphanRows > 0
      OR @CrossTenantRows > 0 OR @JournalOwnershipIssues > 0
    THEN N'UNRESOLVED' ELSE N'COMPLIANT' END;
DECLARE @RepairScope nvarchar(100) = N'';
IF @MissingCount > 0 OR @UnownedRows > 0 OR @OrphanRows > 0
    SET @RepairScope = N'V7/V8';
IF @JournalOwnershipIssues > 0
    SET @RepairScope = CASE WHEN @RepairScope = N'' THEN N'V26' ELSE @RepairScope + N',V26' END;
IF @CrossTenantRows > 0
    SET @RepairScope = CASE WHEN @RepairScope = N'' THEN N'MANUAL_REVIEW' ELSE @RepairScope + N',MANUAL_REVIEW' END;
IF @RepairScope = N'' SET @RepairScope = N'NONE';

SELECT CONCAT(
    @State,
    N'|MissingColumns=', @MissingCount,
    N'|UnownedRows=', @UnownedRows,
    N'|OrphanTenantRows=', @OrphanRows,
    N'|CrossTenantRows=', @CrossTenantRows,
    N'|JournalOwnershipIssues=', @JournalOwnershipIssues,
    N'|RepairScope=', @RepairScope,
    N'|Objects=', COALESCE((SELECT STRING_AGG(ObjectName, N',') FROM @MissingColumns), N'NONE')
) AS OwnershipState;
