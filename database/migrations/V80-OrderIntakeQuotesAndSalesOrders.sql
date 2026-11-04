/* V80: immutable retailer-scoped intake quotations and a non-posting Sales Order lifecycle.
   Depends on V79 OrderIntakeRequests/Items/Events. No invoice, payment or stock writes are performed. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'commerce.OrderIntakeRequests',N'U') IS NULL
   OR OBJECT_ID(N'commerce.OrderIntakeItems',N'U') IS NULL
   THROW 80000,N'V80 requires V79 Order Intake tables.',1;

BEGIN TRANSACTION;

IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConfirmedQuoteRevision') IS NULL
    ALTER TABLE commerce.OrderIntakeRequests ADD ConfirmedQuoteRevision int NULL;
IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConfirmationVerificationMethod') IS NULL
    ALTER TABLE commerce.OrderIntakeRequests ADD ConfirmationVerificationMethod varchar(32) NULL;
IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConfirmationEvidenceReference') IS NULL
    ALTER TABLE commerce.OrderIntakeRequests ADD ConfirmationEvidenceReference nvarchar(500) NULL;
IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConfirmationOperator') IS NULL
    ALTER TABLE commerce.OrderIntakeRequests ADD ConfirmationOperator nvarchar(256) NULL;
IF COL_LENGTH(N'commerce.OrderIntakeItems',N'ProposedSubstitution') IS NULL
    ALTER TABLE commerce.OrderIntakeItems ADD ProposedSubstitution bit NOT NULL CONSTRAINT DF_OrderIntakeItem_ProposedSubstitution DEFAULT(0) WITH VALUES;

IF OBJECT_ID(N'commerce.OrderIntakeQuotes',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.OrderIntakeQuotes
    (
        TenantId uniqueidentifier NOT NULL,
        QuoteId uniqueidentifier NOT NULL,
        OrderIntakeRequestId uniqueidentifier NOT NULL,
        QuoteRevision int NOT NULL,
        QuoteStatus varchar(16) NOT NULL,
        FulfilmentPreference varchar(16) NOT NULL,
        Pincode char(6) NULL,
        PricingSnapshotJson nvarchar(max) NOT NULL,
        MerchandiseSubtotal decimal(18,2) NOT NULL,
        MerchandiseAmount decimal(18,2) NOT NULL,
        MerchandiseTaxAmount decimal(18,2) NOT NULL,
        IncludedTaxAmount decimal(18,2) NOT NULL,
        AdditionalTaxAmount decimal(18,2) NOT NULL,
        ProductSavings decimal(18,2) NOT NULL,
        PromotionDiscount decimal(18,2) NOT NULL,
        PromotionId uniqueidentifier NULL,
        DeliveryCharge decimal(18,2) NOT NULL,
        PayableAmount decimal(18,2) NOT NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_OrderIntakeQuote_Created DEFAULT(SYSUTCDATETIME()),
        ExpiresAtUtc datetime2(0) NOT NULL,
        CreatedBy nvarchar(256) NULL,
        CONSTRAINT PK_OrderIntakeQuotes PRIMARY KEY(TenantId,QuoteId),
        CONSTRAINT UQ_OrderIntakeQuotes_Revision UNIQUE(TenantId,OrderIntakeRequestId,QuoteRevision),
        CONSTRAINT FK_OrderIntakeQuotes_Request FOREIGN KEY(TenantId,OrderIntakeRequestId)
            REFERENCES commerce.OrderIntakeRequests(TenantId,OrderIntakeRequestId),
        CONSTRAINT CK_OrderIntakeQuotes_Revision CHECK(QuoteRevision>0),
        CONSTRAINT CK_OrderIntakeQuotes_Status CHECK(QuoteStatus IN('CURRENT','SUPERSEDED','EXPIRED')),
        CONSTRAINT CK_OrderIntakeQuotes_Fulfilment CHECK(FulfilmentPreference IN('PICKUP','DELIVERY')),
        CONSTRAINT CK_OrderIntakeQuotes_Snapshot CHECK(ISJSON(PricingSnapshotJson)=1)
    );
END;

IF OBJECT_ID(N'commerce.OrderIntakeQuoteItems',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.OrderIntakeQuoteItems
    (
        TenantId uniqueidentifier NOT NULL,
        QuoteId uniqueidentifier NOT NULL,
        QuoteItemId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        ProductCode nvarchar(100) NOT NULL,
        ProductName nvarchar(250) NOT NULL,
        RequestedText nvarchar(300) NOT NULL,
        IsSubstitution bit NOT NULL,
        UnitName nvarchar(100) NOT NULL,
        UnitShortName nvarchar(32) NULL,
        Quantity decimal(18,4) NOT NULL,
        UnitPrice decimal(18,4) NOT NULL,
        TaxPercentage decimal(9,4) NOT NULL,
        CompareAtPrice decimal(18,4) NOT NULL,
        GstPricingType varchar(16) NOT NULL,
        CONSTRAINT PK_OrderIntakeQuoteItems PRIMARY KEY(TenantId,QuoteItemId),
        CONSTRAINT FK_OrderIntakeQuoteItems_Quote FOREIGN KEY(TenantId,QuoteId)
            REFERENCES commerce.OrderIntakeQuotes(TenantId,QuoteId),
        CONSTRAINT CK_OrderIntakeQuoteItems_Values CHECK(Quantity>0 AND UnitPrice>=0 AND TaxPercentage>=0 AND CompareAtPrice>=0),
        CONSTRAINT CK_OrderIntakeQuoteItems_GstType CHECK(GstPricingType IN('INCLUSIVE','EXCLUSIVE'))
    );
END;

IF OBJECT_ID(N'commerce.IntakeSalesOrders',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.IntakeSalesOrders
    (
        TenantId uniqueidentifier NOT NULL,
        SalesOrderId uniqueidentifier NOT NULL,
        OrderNumber nvarchar(32) NOT NULL,
        OrderIntakeRequestId uniqueidentifier NOT NULL,
        QuoteId uniqueidentifier NOT NULL,
        QuoteRevision int NOT NULL,
        OrderStatus varchar(24) NOT NULL,
        FulfilmentPreference varchar(16) NOT NULL,
        CustomerName nvarchar(200) NULL,
        CustomerMobile nvarchar(30) NULL,
        Pincode char(6) NULL,
        PricingSnapshotJson nvarchar(max) NOT NULL,
        PayableAmount decimal(18,2) NOT NULL,
        ConfirmedAtUtc datetime2(0) NOT NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_IntakeSalesOrders_Created DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(256) NULL,
        Version rowversion NOT NULL,
        CONSTRAINT PK_IntakeSalesOrders PRIMARY KEY(TenantId,SalesOrderId),
        CONSTRAINT UQ_IntakeSalesOrders_Number UNIQUE(TenantId,OrderNumber),
        CONSTRAINT UQ_IntakeSalesOrders_Request UNIQUE(TenantId,OrderIntakeRequestId),
        CONSTRAINT FK_IntakeSalesOrders_Request FOREIGN KEY(TenantId,OrderIntakeRequestId)
            REFERENCES commerce.OrderIntakeRequests(TenantId,OrderIntakeRequestId),
        CONSTRAINT FK_IntakeSalesOrders_Quote FOREIGN KEY(TenantId,QuoteId)
            REFERENCES commerce.OrderIntakeQuotes(TenantId,QuoteId),
        CONSTRAINT CK_IntakeSalesOrders_Status CHECK(OrderStatus IN('DRAFT','AWAITING_CONFIRMATION','CONFIRMED','PROCESSING','READY','COMPLETED','CANCELLED','FAILED')),
        CONSTRAINT CK_IntakeSalesOrders_Fulfilment CHECK(FulfilmentPreference IN('PICKUP','DELIVERY')),
        CONSTRAINT CK_IntakeSalesOrders_Snapshot CHECK(ISJSON(PricingSnapshotJson)=1)
    );
END;

IF OBJECT_ID(N'commerce.IntakeSalesOrderItems',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.IntakeSalesOrderItems
    (
        TenantId uniqueidentifier NOT NULL,
        SalesOrderId uniqueidentifier NOT NULL,
        SalesOrderItemId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        ProductCode nvarchar(100) NOT NULL,
        ProductName nvarchar(250) NOT NULL,
        RequestedText nvarchar(300) NOT NULL,
        IsSubstitution bit NOT NULL,
        UnitName nvarchar(100) NOT NULL,
        UnitShortName nvarchar(32) NULL,
        Quantity decimal(18,4) NOT NULL,
        UnitPrice decimal(18,4) NOT NULL,
        TaxPercentage decimal(9,4) NOT NULL,
        CompareAtPrice decimal(18,4) NOT NULL,
        GstPricingType varchar(16) NOT NULL,
        CONSTRAINT PK_IntakeSalesOrderItems PRIMARY KEY(TenantId,SalesOrderItemId),
        CONSTRAINT FK_IntakeSalesOrderItems_Order FOREIGN KEY(TenantId,SalesOrderId)
            REFERENCES commerce.IntakeSalesOrders(TenantId,SalesOrderId),
        CONSTRAINT CK_IntakeSalesOrderItems_Values CHECK(Quantity>0 AND UnitPrice>=0 AND TaxPercentage>=0 AND CompareAtPrice>=0)
    );
END;

IF OBJECT_ID(N'commerce.IntakeSalesOrderEvents',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.IntakeSalesOrderEvents
    (
        TenantId uniqueidentifier NOT NULL,
        EventId uniqueidentifier NOT NULL,
        SalesOrderId uniqueidentifier NOT NULL,
        PreviousStatus varchar(24) NULL,
        NewStatus varchar(24) NOT NULL,
        Actor nvarchar(256) NULL,
        Note nvarchar(1000) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_IntakeSalesOrderEvents_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_IntakeSalesOrderEvents PRIMARY KEY(TenantId,EventId),
        CONSTRAINT FK_IntakeSalesOrderEvents_Order FOREIGN KEY(TenantId,SalesOrderId)
            REFERENCES commerce.IntakeSalesOrders(TenantId,SalesOrderId)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_OrderIntakeRequests_ConvertedSalesOrder')
    ALTER TABLE commerce.OrderIntakeRequests ADD CONSTRAINT FK_OrderIntakeRequests_ConvertedSalesOrder
        FOREIGN KEY(TenantId,ConvertedOrderId) REFERENCES commerce.IntakeSalesOrders(TenantId,SalesOrderId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeQuotes') AND name=N'IX_OrderIntakeQuotes_RequestCurrent')
    CREATE INDEX IX_OrderIntakeQuotes_RequestCurrent ON commerce.OrderIntakeQuotes(TenantId,OrderIntakeRequestId,QuoteRevision DESC) INCLUDE(QuoteStatus,ExpiresAtUtc,PayableAmount);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'IX_IntakeSalesOrders_StatusDate')
    CREATE INDEX IX_IntakeSalesOrders_StatusDate ON commerce.IntakeSalesOrders(TenantId,OrderStatus,CreatedAtUtc DESC) INCLUDE(OrderNumber,OrderIntakeRequestId,PayableAmount);

COMMIT TRANSACTION;
