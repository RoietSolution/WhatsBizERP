/*
  Read-only QA preflight for the V7-V26 tenant-hardening sequence.
  Run with sqlcmd -b against the intended QA database before any migration.
  This script creates no persistent objects and changes no data.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;
IF DB_NAME() <> N'WhatsBizERP_QA'
    THROW 51600, N'QA preflight stopped: database must be exactly WhatsBizERP_QA.', 1;

IF OBJECT_ID(N'core.Users',N'U') IS NOT NULL AND OBJECT_ID(N'core.UserRoles',N'U') IS NOT NULL AND OBJECT_ID(N'core.Roles',N'U') IS NOT NULL
AND EXISTS
(
    SELECT 1 FROM core.Users u
    JOIN core.UserRoles ownerUr ON ownerUr.UserId=u.Id
    JOIN core.Roles ownerRole ON ownerRole.Id=ownerUr.RoleId AND ownerRole.NormalizedName=N'APPLICATIONOWNER'
    JOIN core.UserRoles otherUr ON otherUr.UserId=u.Id AND otherUr.RoleId<>ownerUr.RoleId
)
    THROW 51602, N'QA preflight stopped: an ApplicationOwner account also has a retailer role.', 1;

DECLARE @Checks TABLE
(
    CheckGroup nvarchar(40) NOT NULL,
    ObjectName nvarchar(256) NOT NULL,
    Result nvarchar(20) NOT NULL,
    Detail nvarchar(1000) NULL
);

DECLARE @RequiredObjects TABLE(ObjectName sysname NOT NULL, ObjectType char(2) NOT NULL);
INSERT @RequiredObjects(ObjectName,ObjectType) VALUES
 (N'core.Tenants','U'),(N'master.Products','U'),(N'sales.Customers','U'),
 (N'purchase.Suppliers','U'),(N'inventory.Warehouses','U'),
 (N'sales.SalesInvoices','U'),(N'purchase.PurchaseInvoices','U'),
 (N'inventory.InventoryBalances','U'),(N'inventory.InventoryTransactions','U'),
 (N'finance.JournalEntries','U'),(N'finance.JournalEntryDetails','U'),
 (N'finance.LedgerEntries','U'),(N'finance.CashBook','U'),(N'finance.BankBook','U'),
 (N'finance.CustomerOutstanding','U'),(N'finance.SupplierOutstanding','U');

INSERT @Checks
SELECT N'PREREQUISITE',ObjectName,
       CASE WHEN OBJECT_ID(ObjectName,ObjectType) IS NULL THEN N'BLOCK' ELSE N'PASS' END,
       CASE WHEN OBJECT_ID(ObjectName,ObjectType) IS NULL THEN N'Required object is absent.' ELSE N'Object exists.' END
FROM @RequiredObjects;

DECLARE @TenantColumns TABLE(ObjectName sysname NOT NULL);
INSERT @TenantColumns VALUES
 (N'master.Products'),(N'sales.Customers'),(N'purchase.Suppliers'),
 (N'inventory.Warehouses'),(N'sales.SalesInvoices'),(N'purchase.PurchaseInvoices'),
 (N'inventory.InventoryBalances'),(N'inventory.InventoryTransactions'),(N'finance.JournalEntries');

INSERT @Checks
SELECT N'TENANT COLUMN',ObjectName,
       CASE WHEN COL_LENGTH(ObjectName,N'TenantId') IS NULL THEN N'MISSING' ELSE N'PRESENT' END,
       N'Presence is informational before migration; V7/V8/V26 may add the column.'
FROM @TenantColumns;

DECLARE @Procedures TABLE(ProcedureName sysname NOT NULL, TenantRequired bit NOT NULL);
INSERT @Procedures VALUES
 (N'sales.POS_PostInvoice',1),(N'sales.POS_AddPayment',1),(N'sales.POS_ReturnInvoice',1),
 (N'purchase.Purchase_Post',1),(N'purchase.Purchase_AddPayment',1),(N'purchase.Purchase_Return',1),
 (N'inventory.StockAdjustment_Post',1),(N'inventory.StockTransfer_Post',1),
 (N'inventory.PhysicalVerification_Post',1),
 (N'finance.PostSource',1),(N'finance.PostPayment',1),(N'finance.PostPartyTransaction',1),
 (N'finance.PostStockAdjustment',1),(N'finance.Receipt_Post',1),(N'finance.Payment_Post',1),
 (N'finance.RefreshOutstanding',1),(N'finance.Outstanding_List',1),
 (N'finance.ReceivablePayable_List',1),(N'dashboard.Finance_Get',1),
 (N'dashboard.Customers_Get',1),(N'dashboard.Suppliers_Get',1),(N'dashboard.Notifications_Get',1);

INSERT @Checks
SELECT N'PROCEDURE',p.ProcedureName,
       CASE WHEN OBJECT_ID(p.ProcedureName,N'P') IS NULL THEN N'BLOCK'
            WHEN p.TenantRequired=1 AND NOT EXISTS
                 (SELECT 1 FROM sys.parameters x WHERE x.object_id=OBJECT_ID(p.ProcedureName) AND x.name=N'@TenantId')
                 THEN N'NEEDS MIGRATION' ELSE N'PASS' END,
       CASE WHEN OBJECT_ID(p.ProcedureName,N'P') IS NULL THEN N'Required procedure is absent.'
            WHEN p.TenantRequired=1 AND NOT EXISTS
                 (SELECT 1 FROM sys.parameters x WHERE x.object_id=OBJECT_ID(p.ProcedureName) AND x.name=N'@TenantId')
                 THEN N'Procedure does not yet expose @TenantId.' ELSE N'Procedure signature includes @TenantId.' END
FROM @Procedures p;

SELECT s.name AS SchemaName,o.name AS TriggerName,
       CASE WHEN OBJECTPROPERTY(o.object_id,'ExecIsQuotedIdentOn')=1 THEN N'YES' ELSE N'NO' END AS QuotedIdentifierOn,
       CASE WHEN sm.uses_ansi_nulls=1 THEN N'YES' ELSE N'NO' END AS AnsiNullsOn,
       CASE WHEN t.is_disabled=0 THEN N'YES' ELSE N'NO' END AS Enabled,
       CASE WHEN sm.definition LIKE N'%SESSION_CONTEXT%' AND sm.definition LIKE N'%TenantId%' THEN N'YES' ELSE N'NO' END AS TenantGuardPresent
FROM sys.triggers t
JOIN sys.objects o ON o.object_id=t.object_id
JOIN sys.schemas s ON s.schema_id=o.schema_id
LEFT JOIN sys.sql_modules sm ON sm.object_id=o.object_id
WHERE o.name IN
 (N'TR_Suppliers_TenantGuard',N'TR_Warehouses_TenantGuard',N'TR_SalesInvoices_TenantGuard',
  N'TR_PurchaseInvoices_TenantGuard',N'TR_InventoryBalances_TenantGuard',
  N'TR_InventoryTransactions_TenantGuard',N'TR_SalesPayments_TenantGuard',
  N'TR_PurchasePayments_TenantGuard',N'TR_SalesReturns_TenantGuard',
  N'TR_PurchaseReturns_TenantGuard',N'TR_InventoryTransactionDetails_TenantGuard',
  N'TR_PurchaseInvoiceItems_TenantGuard')
ORDER BY s.name,o.name;

SELECT s.name AS SchemaName,t.name AS TableName,cc.name AS ConstraintName,cc.definition,
       cc.is_disabled,cc.is_not_trusted
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id=cc.parent_object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE cc.name=N'CK_JournalDetails_OneSide';

/* Counts use dynamic SQL so a pre-migration missing TenantId column is reported, not compiled as an error. */
DECLARE @Ownership TABLE(ObjectName sysname,[RowCount] bigint,NullTenantCount bigint,OrphanTenantCount bigint);
DECLARE @Object sysname,@Sql nvarchar(max);
DECLARE ownership_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT ObjectName FROM @TenantColumns;
OPEN ownership_cursor; FETCH NEXT FROM ownership_cursor INTO @Object;
WHILE @@FETCH_STATUS=0
BEGIN
    IF OBJECT_ID(@Object,N'U') IS NOT NULL AND COL_LENGTH(@Object,N'TenantId') IS NOT NULL
    BEGIN
        SET @Sql=N'SELECT N'''+REPLACE(@Object,'''','''''')+N''',COUNT_BIG(*),SUM(CASE WHEN x.TenantId IS NULL THEN CONVERT(bigint,1) ELSE 0 END),SUM(CASE WHEN x.TenantId IS NOT NULL AND t.TenantId IS NULL THEN CONVERT(bigint,1) ELSE 0 END) FROM '+QUOTENAME(PARSENAME(@Object,2))+N'.'+QUOTENAME(PARSENAME(@Object,1))+N' x LEFT JOIN core.Tenants t ON t.TenantId=x.TenantId;';
        INSERT @Ownership EXEC sys.sp_executesql @Sql;
    END
    FETCH NEXT FROM ownership_cursor INTO @Object;
END
CLOSE ownership_cursor; DEALLOCATE ownership_cursor;
SELECT * FROM @Ownership ORDER BY ObjectName;

IF OBJECT_ID(N'finance.JournalEntries',N'U') IS NOT NULL
BEGIN
    SELECT ReferenceType AS SourceType,COUNT_BIG(*) AS [RowCount],
           SUM(CASE WHEN COL_LENGTH(N'finance.JournalEntries',N'TenantId') IS NOT NULL THEN 0 ELSE 1 END) AS PreMigrationMarker
    FROM finance.JournalEntries GROUP BY ReferenceType ORDER BY ReferenceType;
END

SELECT CheckGroup,ObjectName,Result,Detail FROM @Checks ORDER BY CheckGroup,ObjectName;
IF EXISTS(SELECT 1 FROM @Checks WHERE Result=N'BLOCK')
    THROW 51601, N'QA preflight failed: one or more schema prerequisites are absent.', 1;
