/*
    Phase 3 finance tenant-ownership audit.
    READ ONLY: this script does not create, update, or delete data.
*/
SET NOCOUNT ON;

DECLARE @Ownership TABLE
(
    JournalEntryId uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    OwnershipPath nvarchar(200) NOT NULL
);

/* Operational document sources. Return journals reference the parent invoice. */
INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT j.JournalEntryId, i.TenantId, N'SALE -> sales.SalesInvoices.TenantId'
FROM finance.JournalEntries j
JOIN sales.SalesInvoices i ON i.InvoiceId = j.ReferenceId
WHERE j.ReferenceType = N'SALE' AND i.TenantId IS NOT NULL;

INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT j.JournalEntryId, i.TenantId, N'SALE_RETURN -> sales.SalesInvoices.TenantId'
FROM finance.JournalEntries j
JOIN sales.SalesInvoices i ON i.InvoiceId = j.ReferenceId
WHERE j.ReferenceType = N'SALE_RETURN' AND i.TenantId IS NOT NULL;

INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT j.JournalEntryId, i.TenantId, N'PURCHASE -> purchase.PurchaseInvoices.TenantId'
FROM finance.JournalEntries j
JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId = j.ReferenceId
WHERE j.ReferenceType = N'PURCHASE' AND i.TenantId IS NOT NULL;

INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT j.JournalEntryId, i.TenantId, N'PURCHASE_RETURN -> purchase.PurchaseInvoices.TenantId'
FROM finance.JournalEntries j
JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId = j.ReferenceId
WHERE j.ReferenceType = N'PURCHASE_RETURN' AND i.TenantId IS NOT NULL;

/* Payment journals reference the operational payment row. */
INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT j.JournalEntryId, i.TenantId, N'SALE_PAYMENT -> sales.SalesPayments -> SalesInvoices.TenantId'
FROM finance.JournalEntries j
JOIN sales.SalesPayments p ON p.PaymentId = j.ReferenceId
JOIN sales.SalesInvoices i ON i.InvoiceId = p.InvoiceId
WHERE j.ReferenceType = N'SALE_PAYMENT' AND i.TenantId IS NOT NULL;

INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT j.JournalEntryId, i.TenantId, N'PURCHASE_PAYMENT -> purchase.PurchasePayments -> PurchaseInvoices.TenantId'
FROM finance.JournalEntries j
JOIN purchase.PurchasePayments p ON p.PurchasePaymentId = j.ReferenceId
JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId = p.PurchaseInvoiceId
WHERE j.ReferenceType = N'PURCHASE_PAYMENT' AND i.TenantId IS NOT NULL;

/* Direct party postings are resolved through their journal-linked party ledger. */
INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT DISTINCT j.JournalEntryId, c.TenantId, N'CUSTOMER -> CustomerLedger -> Customers.TenantId'
FROM finance.JournalEntries j
JOIN finance.CustomerLedger l ON l.JournalEntryId = j.JournalEntryId
JOIN sales.Customers c ON c.CustomerId = l.CustomerId
WHERE j.ReferenceType = N'CUSTOMER' AND c.TenantId IS NOT NULL;

INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT DISTINCT j.JournalEntryId, s.TenantId, N'SUPPLIER -> SupplierLedger -> Suppliers.TenantId'
FROM finance.JournalEntries j
JOIN finance.SupplierLedger l ON l.JournalEntryId = j.JournalEntryId
JOIN purchase.Suppliers s ON s.SupplierId = l.SupplierId
WHERE j.ReferenceType = N'SUPPLIER' AND s.TenantId IS NOT NULL;

INSERT @Ownership (JournalEntryId, TenantId, OwnershipPath)
SELECT j.JournalEntryId, t.TenantId, N'STOCK_ADJUSTMENT -> InventoryTransactions.TenantId'
FROM finance.JournalEntries j
JOIN inventory.InventoryTransactions t ON t.TransactionId = j.ReferenceId
WHERE j.ReferenceType = N'STOCK_ADJUSTMENT' AND t.TenantId IS NOT NULL;

;WITH Evidence AS
(
    SELECT j.JournalEntryId, j.ReferenceType,
           COUNT(DISTINCT o.TenantId) CandidateTenantCount
    FROM finance.JournalEntries j
    LEFT JOIN @Ownership o ON o.JournalEntryId = j.JournalEntryId
    GROUP BY j.JournalEntryId, j.ReferenceType
), Paths AS
(
    SELECT d.ReferenceType, STRING_AGG(d.OwnershipPath, N'; ') OwnershipPath
    FROM (SELECT DISTINCT j.ReferenceType, o.OwnershipPath
          FROM finance.JournalEntries j
          JOIN @Ownership o ON o.JournalEntryId = j.JournalEntryId) d
    GROUP BY d.ReferenceType
)
SELECT e.ReferenceType SourceType,
       COUNT_BIG(*) [RowCount],
       SUM(CASE WHEN e.CandidateTenantCount = 1 THEN 1 ELSE 0 END) DeterministicTenantCount,
       SUM(CASE WHEN e.CandidateTenantCount > 1 THEN 1 ELSE 0 END) AmbiguousCount,
       SUM(CASE WHEN e.CandidateTenantCount = 0 THEN 1 ELSE 0 END) UnresolvedCount,
       COALESCE(p.OwnershipPath, N'No deterministic path implemented') OwnershipPath
FROM Evidence e
LEFT JOIN Paths p ON p.ReferenceType = e.ReferenceType
GROUP BY e.ReferenceType, p.OwnershipPath
ORDER BY e.ReferenceType;

/* Journal-level unresolved and ambiguous evidence. */
SELECT j.JournalEntryId, j.JournalNumber, j.EntryDate, j.TransactionType,
       j.ReferenceType, j.ReferenceId, j.TenantId,
       COUNT(DISTINCT o.TenantId) CandidateTenantCount
FROM finance.JournalEntries j
LEFT JOIN @Ownership o ON o.JournalEntryId = j.JournalEntryId
GROUP BY j.JournalEntryId, j.JournalNumber, j.EntryDate, j.TransactionType,
         j.ReferenceType, j.ReferenceId, j.TenantId
HAVING COUNT(DISTINCT o.TenantId) <> 1
ORDER BY j.EntryDate, j.JournalEntryId;

/* Existing header ownership that conflicts with a single deterministic source. */
;WITH Deterministic AS
(
    SELECT JournalEntryId,
           CONVERT(uniqueidentifier, MIN(CONVERT(char(36), TenantId))) TenantId
    FROM @Ownership
    GROUP BY JournalEntryId
    HAVING COUNT(DISTINCT TenantId) = 1
)
SELECT j.JournalEntryId, j.JournalNumber, j.ReferenceType, j.ReferenceId,
       j.TenantId JournalTenantId, d.TenantId SourceTenantId
FROM finance.JournalEntries j
JOIN Deterministic d ON d.JournalEntryId = j.JournalEntryId
WHERE j.TenantId IS NOT NULL AND j.TenantId <> d.TenantId
ORDER BY j.JournalEntryId;

/* Orphan children and journal/detail/ledger consistency. */
SELECT CheckName, IssueCount
FROM
(
    SELECT N'JournalEntryDetails without JournalEntry' CheckName, COUNT_BIG(*) IssueCount
    FROM finance.JournalEntryDetails d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
    WHERE j.JournalEntryId IS NULL
    UNION ALL
    SELECT N'LedgerEntries without JournalEntry', COUNT_BIG(*)
    FROM finance.LedgerEntries d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
    WHERE j.JournalEntryId IS NULL
    UNION ALL
    SELECT N'CashBook without JournalEntry', COUNT_BIG(*)
    FROM finance.CashBook d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
    WHERE j.JournalEntryId IS NULL
    UNION ALL
    SELECT N'BankBook without JournalEntry', COUNT_BIG(*)
    FROM finance.BankBook d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
    WHERE j.JournalEntryId IS NULL
    UNION ALL
    SELECT N'CustomerOutstanding without Customer or Invoice', COUNT_BIG(*)
    FROM finance.CustomerOutstanding o
    LEFT JOIN sales.Customers c ON c.CustomerId=o.CustomerId
    LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=o.InvoiceId
    WHERE c.CustomerId IS NULL OR i.InvoiceId IS NULL
    UNION ALL
    SELECT N'SupplierOutstanding without Supplier or Invoice', COUNT_BIG(*)
    FROM finance.SupplierOutstanding o
    LEFT JOIN purchase.Suppliers s ON s.SupplierId=o.SupplierId
    LEFT JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=o.PurchaseInvoiceId
    WHERE s.SupplierId IS NULL OR i.PurchaseInvoiceId IS NULL
) q
ORDER BY CheckName;

/* Cross-tenant outstanding relationships. */
SELECT N'CustomerOutstanding customer/invoice mismatch' CheckName, COUNT_BIG(*) IssueCount
FROM finance.CustomerOutstanding o
JOIN sales.Customers c ON c.CustomerId=o.CustomerId
JOIN sales.SalesInvoices i ON i.InvoiceId=o.InvoiceId
WHERE c.TenantId <> i.TenantId
UNION ALL
SELECT N'SupplierOutstanding supplier/invoice mismatch', COUNT_BIG(*)
FROM finance.SupplierOutstanding o
JOIN purchase.Suppliers s ON s.SupplierId=o.SupplierId
JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=o.PurchaseInvoiceId
WHERE s.TenantId <> i.TenantId;

/* Accounting and derivative-table consistency. */
;WITH DetailTotals AS
(
    SELECT JournalEntryId,
           SUM(DebitAmount) DebitTotal,
           SUM(CreditAmount) CreditTotal,
           SUM(CASE WHEN (DebitAmount > 0 AND CreditAmount = 0)
                          OR (CreditAmount > 0 AND DebitAmount = 0) THEN 0 ELSE 1 END) InvalidSideRows
    FROM finance.JournalEntryDetails
    GROUP BY JournalEntryId
), LedgerTotals AS
(
    SELECT JournalEntryId, SUM(DebitAmount) DebitTotal, SUM(CreditAmount) CreditTotal
    FROM finance.LedgerEntries
    GROUP BY JournalEntryId
)
SELECT j.JournalEntryId, j.JournalNumber,
       d.DebitTotal DetailDebit, d.CreditTotal DetailCredit, d.InvalidSideRows,
       l.DebitTotal LedgerDebit, l.CreditTotal LedgerCredit,
       CASE WHEN d.JournalEntryId IS NULL OR d.DebitTotal <> d.CreditTotal
                  OR d.InvalidSideRows <> 0
                  OR l.JournalEntryId IS NULL
                  OR l.DebitTotal <> d.DebitTotal OR l.CreditTotal <> d.CreditTotal
            THEN N'INCONSISTENT' ELSE N'CONSISTENT' END Consistency
FROM finance.JournalEntries j
LEFT JOIN DetailTotals d ON d.JournalEntryId=j.JournalEntryId
LEFT JOIN LedgerTotals l ON l.JournalEntryId=j.JournalEntryId
WHERE d.JournalEntryId IS NULL OR d.DebitTotal <> d.CreditTotal OR d.InvalidSideRows <> 0
   OR l.JournalEntryId IS NULL OR l.DebitTotal <> d.DebitTotal OR l.CreditTotal <> d.CreditTotal
ORDER BY j.EntryDate, j.JournalEntryId;

SELECT N'CashBook invalid direction' CheckName, COUNT_BIG(*) IssueCount
FROM finance.CashBook WHERE NOT ((AmountIn > 0 AND AmountOut = 0) OR (AmountOut > 0 AND AmountIn = 0))
UNION ALL
SELECT N'BankBook invalid direction', COUNT_BIG(*)
FROM finance.BankBook WHERE NOT ((AmountIn > 0 AND AmountOut = 0) OR (AmountOut > 0 AND AmountIn = 0));

/* Fail closed when this audit is used by the automated QA deployment. */
IF EXISTS
(
    SELECT j.JournalEntryId
    FROM finance.JournalEntries j
    LEFT JOIN @Ownership o ON o.JournalEntryId=j.JournalEntryId
    GROUP BY j.JournalEntryId
    HAVING COUNT(DISTINCT o.TenantId)<>1
)
OR EXISTS
(
    SELECT 1
    FROM finance.JournalEntries j
    JOIN
    (
        SELECT JournalEntryId,CONVERT(uniqueidentifier,MIN(CONVERT(char(36),TenantId))) TenantId
        FROM @Ownership GROUP BY JournalEntryId HAVING COUNT(DISTINCT TenantId)=1
    ) d ON d.JournalEntryId=j.JournalEntryId
    WHERE j.TenantId IS NOT NULL AND j.TenantId<>d.TenantId
)
OR EXISTS (SELECT 1 FROM finance.JournalEntryDetails d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId WHERE j.JournalEntryId IS NULL)
OR EXISTS (SELECT 1 FROM finance.LedgerEntries d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId WHERE j.JournalEntryId IS NULL)
OR EXISTS (SELECT 1 FROM finance.CashBook d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId WHERE j.JournalEntryId IS NULL)
OR EXISTS (SELECT 1 FROM finance.BankBook d LEFT JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId WHERE j.JournalEntryId IS NULL)
OR EXISTS (SELECT 1 FROM finance.CustomerOutstanding o LEFT JOIN sales.Customers c ON c.CustomerId=o.CustomerId LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=o.InvoiceId WHERE c.CustomerId IS NULL OR i.InvoiceId IS NULL OR c.TenantId<>i.TenantId)
OR EXISTS (SELECT 1 FROM finance.SupplierOutstanding o LEFT JOIN purchase.Suppliers s ON s.SupplierId=o.SupplierId LEFT JOIN purchase.PurchaseInvoices i ON i.PurchaseInvoiceId=o.PurchaseInvoiceId WHERE s.SupplierId IS NULL OR i.PurchaseInvoiceId IS NULL OR s.TenantId<>i.TenantId)
OR EXISTS
(
    SELECT 1 FROM finance.JournalEntryDetails
    GROUP BY JournalEntryId
    HAVING SUM(DebitAmount)<>SUM(CreditAmount)
       OR SUM(DebitAmount)<=0 OR SUM(CreditAmount)<=0
       OR SUM(CASE WHEN (DebitAmount>0 AND CreditAmount=0) OR (CreditAmount>0 AND DebitAmount=0) THEN 0 ELSE 1 END)<>0
)
OR EXISTS
(
    SELECT 1
    FROM finance.JournalEntries j
    LEFT JOIN (SELECT JournalEntryId,SUM(DebitAmount) DebitTotal,SUM(CreditAmount) CreditTotal FROM finance.JournalEntryDetails GROUP BY JournalEntryId) d ON d.JournalEntryId=j.JournalEntryId
    LEFT JOIN (SELECT JournalEntryId,SUM(DebitAmount) DebitTotal,SUM(CreditAmount) CreditTotal FROM finance.LedgerEntries GROUP BY JournalEntryId) l ON l.JournalEntryId=j.JournalEntryId
    WHERE d.JournalEntryId IS NULL OR l.JournalEntryId IS NULL OR d.DebitTotal<>l.DebitTotal OR d.CreditTotal<>l.CreditTotal
)
OR EXISTS (SELECT 1 FROM finance.CashBook WHERE NOT ((AmountIn>0 AND AmountOut=0) OR (AmountOut>0 AND AmountIn=0)))
OR EXISTS (SELECT 1 FROM finance.BankBook WHERE NOT ((AmountIn>0 AND AmountOut=0) OR (AmountOut>0 AND AmountIn=0)))
    THROW 51611, N'Finance tenant ownership audit failed; V26 was not authorized.', 1;

SELECT N'PASS' Result,N'Finance ownership and accounting preconditions authorize V26.' Detail;
