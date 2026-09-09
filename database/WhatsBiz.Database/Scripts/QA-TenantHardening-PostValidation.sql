/* Read-only validation after V18, V24 and V26. Never run against Production. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SELECT @@SERVERNAME AS ServerName,DB_NAME() AS DatabaseName;
IF DB_NAME()<>N'WhatsBizERP_QA' THROW 51620,N'Post-validation requires exactly WhatsBizERP_QA.',1;

DECLARE @Failures TABLE(CheckName nvarchar(200),FailureCount bigint,Detail nvarchar(1000));

DECLARE @Procedures TABLE(ProcedureName sysname);
INSERT @Procedures VALUES
 (N'sales.POS_PostInvoice'),(N'sales.POS_AddPayment'),(N'sales.POS_ReturnInvoice'),
 (N'purchase.Purchase_Post'),(N'purchase.Purchase_AddPayment'),(N'purchase.Purchase_Return'),
 (N'inventory.StockAdjustment_Post'),(N'inventory.StockTransfer_Post'),(N'inventory.PhysicalVerification_Post'),
 (N'finance.PostSource'),(N'finance.PostPayment'),(N'finance.PostPartyTransaction'),
 (N'finance.PostStockAdjustment'),(N'finance.Receipt_Post'),(N'finance.Payment_Post'),
 (N'finance.RefreshOutstanding'),(N'finance.Outstanding_List'),(N'finance.ReceivablePayable_List'),
 (N'dashboard.Finance_Get'),(N'dashboard.Customers_Get'),(N'dashboard.Suppliers_Get'),(N'dashboard.Notifications_Get');
INSERT @Failures
SELECT N'Procedure @TenantId: '+ProcedureName,1,N'Missing procedure or required @TenantId.'
FROM @Procedures
WHERE OBJECT_ID(ProcedureName,N'P') IS NULL
   OR NOT EXISTS(SELECT 1 FROM sys.parameters p WHERE p.object_id=OBJECT_ID(ProcedureName) AND p.name=N'@TenantId');

INSERT @Failures
SELECT N'Operational trigger metadata: '+QUOTENAME(s.name)+N'.'+QUOTENAME(o.name),1,
       N'Trigger must be enabled with ANSI_NULLS/QUOTED_IDENTIFIER and contain tenant context logic.'
FROM sys.triggers t JOIN sys.objects o ON o.object_id=t.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id
LEFT JOIN sys.sql_modules sm ON sm.object_id=o.object_id
WHERE o.name LIKE N'TR[_]%TenantGuard'
AND (t.is_disabled=1 OR ISNULL(OBJECTPROPERTY(o.object_id,'ExecIsQuotedIdentOn'),0)<>1
     OR ISNULL(sm.uses_ansi_nulls,0)<>1 OR sm.definition NOT LIKE N'%SESSION_CONTEXT%' OR sm.definition NOT LIKE N'%TenantId%');
IF (SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'TR[_]%TenantGuard')<12
    INSERT @Failures VALUES(N'V24 trigger count',12-(SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'TR[_]%TenantGuard'),N'Expected at least 12 tenant guard triggers.');

IF COL_LENGTH(N'finance.JournalEntries',N'TenantId') IS NULL
    INSERT @Failures VALUES(N'JournalEntries.TenantId column',1,N'Column is absent.');
ELSE
BEGIN
    DECLARE @NullJournals bigint,@OrphanJournals bigint;
    EXEC sys.sp_executesql N'SELECT @n=SUM(CASE WHEN j.TenantId IS NULL THEN CONVERT(bigint,1) ELSE 0 END),@o=SUM(CASE WHEN j.TenantId IS NOT NULL AND t.TenantId IS NULL THEN CONVERT(bigint,1) ELSE 0 END) FROM finance.JournalEntries j LEFT JOIN core.Tenants t ON t.TenantId=j.TenantId;',N'@n bigint OUTPUT,@o bigint OUTPUT',@n=@NullJournals OUTPUT,@o=@OrphanJournals OUTPUT;
    IF ISNULL(@NullJournals,0)>0 INSERT @Failures VALUES(N'NULL JournalEntries.TenantId',@NullJournals,N'All QA ownership must resolve before promotion.');
    IF ISNULL(@OrphanJournals,0)>0 INSERT @Failures VALUES(N'Orphan JournalEntries.TenantId',@OrphanJournals,N'Tenant FK ownership is invalid.');
END

INSERT @Failures
SELECT N'Journal detail one-side invariant',COUNT_BIG(*),N'Each row must populate exactly one positive debit/credit side.'
FROM finance.JournalEntryDetails
WHERE NOT ((DebitAmount>0 AND CreditAmount=0) OR (DebitAmount=0 AND CreditAmount>0))
HAVING COUNT_BIG(*)>0;
INSERT @Failures
SELECT N'Unbalanced journal',COUNT_BIG(*),N'SUM(debit) must equal SUM(credit) and both must be positive.'
FROM (SELECT JournalEntryId FROM finance.JournalEntryDetails GROUP BY JournalEntryId
      HAVING SUM(DebitAmount)<>SUM(CreditAmount) OR SUM(DebitAmount)<=0 OR SUM(CreditAmount)<=0) x
HAVING COUNT_BIG(*)>0;
INSERT @Failures
SELECT N'Orphan journal details',COUNT_BIG(*),N'Detail has no journal header.'
FROM finance.JournalEntryDetails d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
WHERE j.JournalEntryId IS NULL HAVING COUNT_BIG(*)>0;
INSERT @Failures
SELECT N'Orphan ledger entries',COUNT_BIG(*),N'Ledger entry has no journal header.'
FROM finance.LedgerEntries d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
WHERE j.JournalEntryId IS NULL HAVING COUNT_BIG(*)>0;
INSERT @Failures
SELECT N'Orphan cash book rows',COUNT_BIG(*),N'Cash book row has no journal header.'
FROM finance.CashBook d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
WHERE j.JournalEntryId IS NULL HAVING COUNT_BIG(*)>0;
INSERT @Failures
SELECT N'Orphan bank book rows',COUNT_BIG(*),N'Bank book row has no journal header.'
FROM finance.BankBook d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
WHERE j.JournalEntryId IS NULL HAVING COUNT_BIG(*)>0;

INSERT @Failures
SELECT N'Invalid identity account scope',COUNT_BIG(*),N'Application owners require NULL TenantId; retailer users require a tenant.'
FROM core.Users
WHERE (AccountType=N'APPLICATION_OWNER' AND TenantId IS NOT NULL)
   OR (AccountType=N'RETAILER' AND TenantId IS NULL)
   OR AccountType NOT IN(N'APPLICATION_OWNER',N'RETAILER')
HAVING COUNT_BIG(*)>0;
INSERT @Failures
SELECT N'Invalid ApplicationOwner role assignment',COUNT_BIG(*),N'ApplicationOwner role must only be assigned to platform-owner identities and cannot be combined with retailer roles.'
FROM core.UserRoles ur JOIN core.Users u ON u.Id=ur.UserId JOIN core.Roles r ON r.Id=ur.RoleId
WHERE (r.NormalizedName=N'APPLICATIONOWNER' AND u.AccountType<>N'APPLICATION_OWNER')
   OR (r.NormalizedName<>N'APPLICATIONOWNER' AND u.AccountType=N'APPLICATION_OWNER')
HAVING COUNT_BIG(*)>0;

SELECT CheckName,FailureCount,Detail FROM @Failures ORDER BY CheckName;
IF EXISTS(SELECT 1 FROM @Failures) THROW 51621,N'QA post-migration validation failed.',1;
SELECT N'PASS' AS Result,N'QA tenant ownership, trigger metadata, signatures, and finance integrity passed.' AS Detail;
