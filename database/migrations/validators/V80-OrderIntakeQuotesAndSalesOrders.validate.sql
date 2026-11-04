SET NOCOUNT ON;
IF DB_NAME() IS NULL THROW 80010,N'Unable to identify the validation database.',1;
IF OBJECT_ID(N'commerce.OrderIntakeRequests',N'U') IS NULL
   OR OBJECT_ID(N'commerce.OrderIntakeItems',N'U') IS NULL
   OR OBJECT_ID(N'commerce.OrderIntakeEvents',N'U') IS NULL
    THROW 80011,N'V79 Order Intake prerequisite is missing.',1;
IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConfirmedQuoteRevision') IS NULL
   OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConfirmationEvidenceReference') IS NULL
   OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConfirmationOperator') IS NULL
    THROW 80012,N'V80 confirmation evidence columns are missing.',1;
IF OBJECT_ID(N'commerce.OrderIntakeQuotes',N'U') IS NULL
   OR OBJECT_ID(N'commerce.OrderIntakeQuoteItems',N'U') IS NULL
   OR OBJECT_ID(N'commerce.IntakeSalesOrders',N'U') IS NULL
   OR OBJECT_ID(N'commerce.IntakeSalesOrderItems',N'U') IS NULL
   OR OBJECT_ID(N'commerce.IntakeSalesOrderEvents',N'U') IS NULL
    THROW 80013,N'V80 quote or Sales Order tables are missing.',1;
IF COL_LENGTH(N'commerce.OrderIntakeQuoteItems',N'UnitName') IS NULL
   OR COL_LENGTH(N'commerce.IntakeSalesOrderItems',N'UnitName') IS NULL
    THROW 80017,N'V80 immutable quote/order unit snapshots are missing.',1;
IF COL_LENGTH(N'commerce.OrderIntakeItems',N'ProposedSubstitution') IS NULL
    THROW 80018,N'V80 proposed-substitution review state is missing.',1;
IF COL_LENGTH(N'commerce.OrderIntakeQuoteItems',N'IsSubstitution') IS NULL
   OR COL_LENGTH(N'commerce.IntakeSalesOrderItems',N'IsSubstitution') IS NULL
    THROW 80019,N'V80 substitution review snapshots are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_OrderIntakeRequests_ConvertedSalesOrder')
   OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_IntakeSalesOrders_Request')
   OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_OrderIntakeQuotes_Request')
    THROW 80014,N'V80 tenant-scoped foreign keys are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'UQ_IntakeSalesOrders_Request' AND is_unique=1)
    THROW 80015,N'V80 idempotent one-order-per-request constraint is missing.',1;
IF EXISTS(SELECT 1 FROM commerce.OrderIntakeRequests WHERE ConvertedOrderId IS NOT NULL
    AND NOT EXISTS(SELECT 1 FROM commerce.IntakeSalesOrders o WHERE o.TenantId=OrderIntakeRequests.TenantId AND o.SalesOrderId=OrderIntakeRequests.ConvertedOrderId))
    THROW 80016,N'V80 found a dangling converted Sales Order reference.',1;
SELECT N'V80 intake quote and non-posting Sales Order schema validated.' AS ValidationResult;
