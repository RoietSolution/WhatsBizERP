/*
   Final release-state validation for shared application reference data.
   This is deliberately a validation-only script: the idempotent seeds that
   precede it own creation/reconciliation of these rows.
*/
SET NOCOUNT ON;

IF EXISTS
(
    SELECT 1
    FROM (VALUES
        (N'ASSET'),(N'LIABILITY'),(N'INCOME'),(N'EXPENSE')
    ) AS required(GroupCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM finance.AccountGroups g
        WHERE g.GroupCode = required.GroupCode AND g.IsActive = 1
    )
)
    THROW 51680, N'Required finance account group baseline is incomplete.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES
        (N'CASH'),(N'BANK'),(N'CUSTOMER'),(N'SUPPLIER'),(N'INVENTORY'),
        (N'INPUT_GST'),(N'OUTPUT_GST'),(N'SALES'),(N'PURCHASE_RETURN'),
        (N'SALES_RETURN'),(N'STOCK_ADJUST')
    ) AS required(AccountCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM finance.Accounts a
        WHERE a.AccountCode = required.AccountCode AND a.IsActive = 1
    )
)
    THROW 51681, N'Required finance account baseline is incomplete.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES (N'CASH'),(N'UPI'),(N'CARD'),(N'BANK'),(N'WALLET'),(N'CREDIT')) AS required(ModeCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM finance.PaymentModes m
        WHERE m.ModeCode = required.ModeCode AND m.IsActive = 1
    )
)
    THROW 51682, N'Required finance payment-mode baseline is incomplete.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES (N'CASH'),(N'UPI'),(N'CARD'),(N'BANK'),(N'WALLET'),(N'CREDIT')) AS required(MethodCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM sales.PaymentMethods m
        WHERE m.MethodCode = required.MethodCode AND m.IsActive = 1
    )
)
    THROW 51683, N'Required sales payment-method baseline is incomplete.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES (N'IMMEDIATE'),(N'NET15'),(N'NET30'),(N'NET45'),(N'NET60')) AS required(PaymentTermCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM purchase.SupplierPaymentTerms p
        WHERE p.PaymentTermCode = required.PaymentTermCode AND p.IsActive = 1
    )
)
    THROW 51684, N'Required supplier payment-term baseline is incomplete.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES (N'IMMEDIATE'),(N'NET15'),(N'NET30'),(N'NET45'),(N'NET60')) AS required(PaymentTermCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM sales.CustomerPaymentTerms p
        WHERE p.PaymentTermCode = required.PaymentTermCode AND p.IsActive = 1
    )
)
    THROW 51685, N'Required customer payment-term baseline is incomplete.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES (N'GENERAL'),(N'DISTRIBUTION'),(N'COLD'),(N'BONDED'),(N'TRANSIT')) AS required(TypeCode)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM inventory.WarehouseTypes w
        WHERE w.TypeCode = required.TypeCode AND w.IsActive = 1
    )
)
    THROW 51686, N'Required warehouse-type baseline is incomplete.', 1;

IF NOT EXISTS (SELECT 1 FROM inventory.InventorySettings)
    THROW 51687, N'Required inventory-settings baseline is missing.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sales.InvoiceSeries
    WHERE SeriesCode = N'POS'
      AND FinancialYear = CONCAT(YEAR(GETDATE()), N'-', RIGHT(CONVERT(nvarchar(4), YEAR(GETDATE()) + 1), 2))
      AND IsDefault = 1 AND IsActive = 1
)
    THROW 51688, N'Required POS invoice-series baseline is missing.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM purchase.PurchaseSeries
    WHERE SeriesCode = N'PUR' AND IsDefault = 1 AND IsActive = 1
)
    THROW 51689, N'Required purchase-series baseline is missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES
        (N'V1'),(N'V2'),(N'PRODUCTS'),(N'INVENTORY'),(N'POS'),(N'CUSTOMERS'),
        (N'SUPPLIERS'),(N'PURCHASE'),(N'WAREHOUSES'),(N'FINANCE'),(N'GST'),
        (N'ADMINISTRATION'),(N'WHATSAPP_COMMERCE')
    ) AS required(FeatureKey)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM core.Features f
        WHERE f.FeatureKey = required.FeatureKey AND f.IsActive = 1
    )
)
    THROW 51690, N'Required feature-definition baseline is incomplete.', 1;

IF EXISTS
(
    SELECT 1
    FROM (VALUES (N'V1_DEFAULT'),(N'V2_COMMERCE')) AS required(PlanKey)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM core.Plans p
        WHERE p.PlanKey = required.PlanKey AND p.IsActive = 1
    )
)
    THROW 51691, N'Required plan-definition baseline is incomplete.', 1;
