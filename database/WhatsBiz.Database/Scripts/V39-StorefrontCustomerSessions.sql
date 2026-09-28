SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'master.Products',N'PackSize') IS NULL
    ALTER TABLE master.Products ADD PackSize nvarchar(50) NULL;
GO
IF COL_LENGTH(N'sales.Customers',N'MobileNormalized') IS NULL
    ALTER TABLE sales.Customers ADD MobileNormalized varchar(10) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'sales.Customers') AND name=N'UX_Customers_TenantMobileNormalized')
    CREATE UNIQUE INDEX UX_Customers_TenantMobileNormalized ON sales.Customers(TenantId,MobileNormalized)
    WHERE TenantId IS NOT NULL AND MobileNormalized IS NOT NULL AND IsDeleted=0;
GO
IF OBJECT_ID(N'commerce.StorefrontOtpChallenges',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontOtpChallenges
    (
        ChallengeId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        MobileNumberNormalized varchar(10) NOT NULL,
        OtpHash varbinary(32) NOT NULL,
        OtpSalt varbinary(16) NOT NULL,
        CreatedAt datetimeoffset NOT NULL,
        ExpiresAt datetimeoffset NOT NULL,
        AttemptCount int NOT NULL CONSTRAINT DF_StorefrontOtpChallenges_AttemptCount DEFAULT(0),
        ConsumedAt datetimeoffset NULL,
        CONSTRAINT PK_StorefrontOtpChallenges PRIMARY KEY(ChallengeId),
        CONSTRAINT FK_StorefrontOtpChallenges_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_StorefrontOtpChallenges_Attempts CHECK(AttemptCount>=0 AND AttemptCount<=5),
        CONSTRAINT CK_StorefrontOtpChallenges_Mobile CHECK(MobileNumberNormalized NOT LIKE '%[^0-9]%' AND LEN(MobileNumberNormalized)=10),
        CONSTRAINT CK_StorefrontOtpChallenges_Expiry CHECK(ExpiresAt>CreatedAt)
    );
    CREATE INDEX IX_StorefrontOtpChallenges_Lookup ON commerce.StorefrontOtpChallenges(TenantId,MobileNumberNormalized,CreatedAt DESC);
    CREATE INDEX IX_StorefrontOtpChallenges_Expiry ON commerce.StorefrontOtpChallenges(ExpiresAt) WHERE ConsumedAt IS NULL;
END;
GO
IF OBJECT_ID(N'commerce.StorefrontWishlistItems', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontWishlistItems
    (
        TenantId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontWishlistItems_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_StorefrontWishlistItems PRIMARY KEY(TenantId,CustomerId,ProductId),
        CONSTRAINT FK_StorefrontWishlistItems_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontWishlistItems_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId) ON DELETE CASCADE,
        CONSTRAINT FK_StorefrontWishlistItems_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId) ON DELETE CASCADE
    );
    CREATE INDEX IX_StorefrontWishlistItems_CustomerCreated ON commerce.StorefrontWishlistItems(TenantId,CustomerId,CreatedAt DESC);
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontWishlistItems_TenantGuard
ON commerce.StorefrontWishlistItems
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1 FROM inserted i
        LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=i.TenantId AND c.IsDeleted=0
        LEFT JOIN master.Products p ON p.ProductId=i.ProductId AND p.TenantId=i.TenantId AND p.IsDeleted=0
        WHERE c.CustomerId IS NULL OR p.ProductId IS NULL
    )
    BEGIN
        THROW 51000, 'Wishlist customer and product must belong to the same tenant.', 1;
    END
END;
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'FreeDeliveryThreshold') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD FreeDeliveryThreshold decimal(18,2) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_StorefrontConfigurations_FreeDeliveryThreshold')
    ALTER TABLE commerce.StorefrontConfigurations ADD CONSTRAINT CK_StorefrontConfigurations_FreeDeliveryThreshold
        CHECK(FreeDeliveryThreshold IS NULL OR FreeDeliveryThreshold > 0);
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'ShowProductRatings') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD ShowProductRatings bit NOT NULL CONSTRAINT DF_StorefrontConfigurations_ShowProductRatings DEFAULT(1);
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'ShowProductReviews') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD ShowProductReviews bit NOT NULL CONSTRAINT DF_StorefrontConfigurations_ShowProductReviews DEFAULT(1);
GO
IF OBJECT_ID(N'commerce.StorefrontProductReviews',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontProductReviews
    (
        ReviewId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        Rating int NOT NULL,
        ReviewText nvarchar(1000) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_StorefrontProductReviews_Status DEFAULT('PUBLISHED'),
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontProductReviews_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontProductReviews_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_StorefrontProductReviews PRIMARY KEY(ReviewId),
        CONSTRAINT FK_StorefrontProductReviews_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontProductReviews_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId),
        CONSTRAINT FK_StorefrontProductReviews_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
        CONSTRAINT CK_StorefrontProductReviews_Rating CHECK(Rating BETWEEN 1 AND 5),
        CONSTRAINT CK_StorefrontProductReviews_Status CHECK(Status IN('PUBLISHED','HIDDEN')),
        CONSTRAINT UQ_StorefrontProductReviews_CustomerProduct UNIQUE(TenantId,CustomerId,ProductId)
    );
    CREATE INDEX IX_StorefrontProductReviews_ProductStatus ON commerce.StorefrontProductReviews(TenantId,ProductId,Status) INCLUDE(Rating,UpdatedAt);
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontProductReviews_TenantGuard
ON commerce.StorefrontProductReviews
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1 FROM inserted i
        LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=i.TenantId AND c.IsActive=1 AND c.IsDeleted=0
        LEFT JOIN master.Products p ON p.ProductId=i.ProductId AND p.TenantId=i.TenantId AND p.IsDeleted=0
        WHERE c.CustomerId IS NULL OR p.ProductId IS NULL
    )
        THROW 51000, 'Review customer and product must belong to the same tenant.', 1;
END;
GO
-- Storefront delivery and offer pricing. Defaults require explicit retailer configuration before paid delivery.
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'DeliveryEnabled') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD DeliveryEnabled bit NOT NULL CONSTRAINT DF_StorefrontConfigurations_DeliveryEnabled DEFAULT(0);
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'StandardDeliveryCharge') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD StandardDeliveryCharge decimal(18,2) NOT NULL CONSTRAINT DF_StorefrontConfigurations_StandardDeliveryCharge DEFAULT(0);
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'FreeDeliveryEnabled') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD FreeDeliveryEnabled bit NOT NULL CONSTRAINT DF_StorefrontConfigurations_FreeDeliveryEnabled DEFAULT(0);
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'DeliveryChargeTaxEnabled') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD DeliveryChargeTaxEnabled bit NOT NULL CONSTRAINT DF_StorefrontConfigurations_DeliveryChargeTaxEnabled DEFAULT(0);
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'DeliveryChargeIncomePostingEnabled') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD DeliveryChargeIncomePostingEnabled bit NOT NULL CONSTRAINT DF_StorefrontConfigurations_DeliveryChargeIncomePostingEnabled DEFAULT(0);
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_StorefrontConfigurations_DeliveryPricing')
    ALTER TABLE commerce.StorefrontConfigurations ADD CONSTRAINT CK_StorefrontConfigurations_DeliveryPricing
        CHECK(StandardDeliveryCharge>=0 AND DeliveryChargeTaxEnabled=0 AND DeliveryChargeIncomePostingEnabled=0
          AND (FreeDeliveryEnabled=0 OR FreeDeliveryThreshold>0));
GO
IF OBJECT_ID(N'commerce.StorefrontServiceablePincodes',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontServiceablePincodes(
        TenantId uniqueidentifier NOT NULL,
        Pincode varchar(6) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_StorefrontServiceablePincodes_Active DEFAULT(1),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontServiceablePincodes_Updated DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_StorefrontServiceablePincodes PRIMARY KEY(TenantId,Pincode),
        CONSTRAINT FK_StorefrontServiceablePincodes_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_StorefrontServiceablePincodes_Pincode CHECK(LEN(Pincode)=6 AND Pincode NOT LIKE '%[^0-9]%')
    );
END;
GO
IF OBJECT_ID(N'commerce.StorefrontPromotions',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontPromotions(
        PromotionId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OfferName nvarchar(150) NOT NULL,
        OfferType varchar(20) NOT NULL,
        MinimumPurchaseAmount decimal(18,2) NOT NULL,
        DiscountType varchar(12) NOT NULL,
        DiscountValue decimal(18,2) NOT NULL,
        MaximumDiscount decimal(18,2) NULL,
        StartsAt datetimeoffset NULL,
        EndsAt datetimeoffset NULL,
        IsActive bit NOT NULL CONSTRAINT DF_StorefrontPromotions_Active DEFAULT(1),
        IsDeleted bit NOT NULL CONSTRAINT DF_StorefrontPromotions_Deleted DEFAULT(0),
        UsageLimitPerCustomer int NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontPromotions_Created DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontPromotions_Updated DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_StorefrontPromotions PRIMARY KEY(PromotionId),
        CONSTRAINT UQ_StorefrontPromotions_TenantId UNIQUE(TenantId,PromotionId),
        CONSTRAINT FK_StorefrontPromotions_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_StorefrontPromotions_Type CHECK(OfferType IN('MINIMUM_PURCHASE','FIRST_ORDER')),
        CONSTRAINT CK_StorefrontPromotions_Discount CHECK(DiscountType IN('FLAT','PERCENTAGE') AND DiscountValue>0 AND (DiscountType<>'PERCENTAGE' OR DiscountValue<=100)),
        CONSTRAINT CK_StorefrontPromotions_Amounts CHECK(MinimumPurchaseAmount>=0 AND (MaximumDiscount IS NULL OR MaximumDiscount>0)),
        CONSTRAINT CK_StorefrontPromotions_Dates CHECK(StartsAt IS NULL OR EndsAt IS NULL OR StartsAt<EndsAt),
        CONSTRAINT CK_StorefrontPromotions_Usage CHECK(UsageLimitPerCustomer IS NULL OR UsageLimitPerCustomer>0)
    );
    CREATE INDEX IX_StorefrontPromotions_Active ON commerce.StorefrontPromotions(TenantId,IsActive,StartsAt,EndsAt) INCLUDE(OfferType,MinimumPurchaseAmount,DiscountType,DiscountValue,MaximumDiscount,UsageLimitPerCustomer) WHERE IsDeleted=0;
END;
GO
IF OBJECT_ID(N'commerce.StorefrontPromotionUses',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontPromotionUses(
        TenantId uniqueidentifier NOT NULL,
        PromotionId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontPromotionUses_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_StorefrontPromotionUses PRIMARY KEY(TenantId,PromotionId,InvoiceId),
        CONSTRAINT UQ_StorefrontPromotionUses_Invoice UNIQUE(InvoiceId),
        CONSTRAINT FK_StorefrontPromotionUses_Promotion FOREIGN KEY(TenantId,PromotionId) REFERENCES commerce.StorefrontPromotions(TenantId,PromotionId),
        CONSTRAINT FK_StorefrontPromotionUses_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
        CONSTRAINT FK_StorefrontPromotionUses_Invoice FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId)
    );
    CREATE INDEX IX_StorefrontPromotionUses_Customer ON commerce.StorefrontPromotionUses(TenantId,PromotionId,CustomerId);
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontPromotionUses_TenantGuard ON commerce.StorefrontPromotionUses AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted u LEFT JOIN sales.Customers c ON c.CustomerId=u.CustomerId AND c.TenantId=u.TenantId
      LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=u.InvoiceId AND i.TenantId=u.TenantId
      WHERE c.CustomerId IS NULL OR i.InvoiceId IS NULL)
        THROW 51000,'Promotion usage must belong to the same tenant.',1;
END;
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'DeliveryCharge') IS NULL
    ALTER TABLE sales.SalesInvoices ADD DeliveryCharge decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_DeliveryCharge DEFAULT(0);
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'DeliveryTaxAmount') IS NULL
    ALTER TABLE sales.SalesInvoices ADD DeliveryTaxAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_DeliveryTaxAmount DEFAULT(0);
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'PromotionDiscountAmount') IS NULL
    ALTER TABLE sales.SalesInvoices ADD PromotionDiscountAmount decimal(18,2) NOT NULL CONSTRAINT DF_SalesInvoices_PromotionDiscountAmount DEFAULT(0);
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'AppliedPromotionId') IS NULL
    ALTER TABLE sales.SalesInvoices ADD AppliedPromotionId uniqueidentifier NULL;
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'AppliedPromotionName') IS NULL
    ALTER TABLE sales.SalesInvoices ADD AppliedPromotionName nvarchar(150) NULL;
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'FreeDeliveryApplied') IS NULL
    ALTER TABLE sales.SalesInvoices ADD FreeDeliveryApplied bit NOT NULL CONSTRAINT DF_SalesInvoices_FreeDeliveryApplied DEFAULT(0);
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'FreeDeliveryThresholdSnapshot') IS NULL
    ALTER TABLE sales.SalesInvoices ADD FreeDeliveryThresholdSnapshot decimal(18,2) NULL;
GO
IF COL_LENGTH(N'sales.SalesInvoices',N'ServicePincode') IS NULL
    ALTER TABLE sales.SalesInvoices ADD ServicePincode varchar(6) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_SalesInvoices_StorefrontAmounts')
    ALTER TABLE sales.SalesInvoices ADD CONSTRAINT CK_SalesInvoices_StorefrontAmounts CHECK(DeliveryCharge>=0 AND DeliveryTaxAmount=0 AND PromotionDiscountAmount>=0 AND (FreeDeliveryThresholdSnapshot IS NULL OR FreeDeliveryThresholdSnapshot>0));
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SalesInvoices_AppliedPromotion')
    ALTER TABLE sales.SalesInvoices ADD CONSTRAINT FK_SalesInvoices_AppliedPromotion FOREIGN KEY(TenantId,AppliedPromotionId) REFERENCES commerce.StorefrontPromotions(TenantId,PromotionId);
GO
-- Storefront cancellation requests and separately auditable payment refunds.
-- Refund rows are never evidence that money moved until their status is REFUNDED.
IF OBJECT_ID(N'commerce.StorefrontCancellationRequests',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontCancellationRequests(
        CancellationRequestId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontCancellationRequests PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        Reason nvarchar(250) NOT NULL,
        Status nvarchar(20) NOT NULL CONSTRAINT DF_StorefrontCancellationRequests_Status DEFAULT(N'REQUESTED'),
        RequestedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontCancellationRequests_Requested DEFAULT(SYSUTCDATETIME()),
        DecidedAt datetimeoffset NULL,
        DecidedBy uniqueidentifier NULL,
        DecisionNote nvarchar(500) NULL,
        CONSTRAINT FK_StorefrontCancellationRequests_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontCancellationRequests_Invoice FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId),
        CONSTRAINT FK_StorefrontCancellationRequests_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
        CONSTRAINT CK_StorefrontCancellationRequests_Status CHECK(Status IN(N'REQUESTED',N'APPROVING',N'REJECTED',N'APPROVED')),
        CONSTRAINT CK_StorefrontCancellationRequests_Reason CHECK(LEN(LTRIM(RTRIM(Reason))) BETWEEN 3 AND 250),
        CONSTRAINT CK_StorefrontCancellationRequests_Decision CHECK((Status IN(N'REQUESTED',N'APPROVING') AND DecidedAt IS NULL) OR (Status IN(N'REJECTED',N'APPROVED') AND DecidedAt IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_StorefrontCancellationRequests_Active ON commerce.StorefrontCancellationRequests(TenantId,InvoiceId) WHERE Status IN(N'REQUESTED',N'APPROVING');
    CREATE INDEX IX_StorefrontCancellationRequests_Order ON commerce.StorefrontCancellationRequests(TenantId,InvoiceId,RequestedAt DESC);
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontCancellationRequests_TenantGuard ON commerce.StorefrontCancellationRequests AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted r
      LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=r.InvoiceId AND i.TenantId=r.TenantId AND i.CustomerId=r.CustomerId
      LEFT JOIN sales.Customers c ON c.CustomerId=r.CustomerId AND c.TenantId=r.TenantId
      LEFT JOIN core.Users u ON u.Id=r.DecidedBy AND u.TenantId=r.TenantId
      WHERE i.InvoiceId IS NULL OR c.CustomerId IS NULL OR (r.DecidedBy IS NOT NULL AND u.Id IS NULL))
        THROW 51000,'Cancellation request tenant ownership is invalid.',1;
END;
GO
IF OBJECT_ID(N'commerce.StorefrontRefunds',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontRefunds(
        RefundId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontRefunds PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        PaymentId uniqueidentifier NOT NULL,
        CancellationRequestId uniqueidentifier NULL,
        Provider nvarchar(30) NOT NULL,
        RefundType nvarchar(30) NOT NULL,
        RefundAmount decimal(18,2) NOT NULL,
        RefundStatus nvarchar(30) NOT NULL,
        Reason nvarchar(250) NOT NULL,
        ProviderRefundId nvarchar(100) NULL,
        ProviderReference nvarchar(100) NULL,
        RequestedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontRefunds_Requested DEFAULT(SYSUTCDATETIME()),
        RequestedBy uniqueidentifier NULL,
        ProcessingStartedAt datetimeoffset NULL,
        RefundedAt datetimeoffset NULL,
        FailedAt datetimeoffset NULL,
        FailureCode nvarchar(100) NULL,
        SettlementJournalId uniqueidentifier NULL,
        SettlementMode nvarchar(30) NULL,
        SettlementReference nvarchar(100) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontRefunds_Created DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontRefunds_Updated DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_StorefrontRefunds_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontRefunds_Invoice FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId),
        CONSTRAINT FK_StorefrontRefunds_Payment FOREIGN KEY(PaymentId) REFERENCES commerce.CommercePayments(PaymentId),
        CONSTRAINT FK_StorefrontRefunds_Cancellation FOREIGN KEY(CancellationRequestId) REFERENCES commerce.StorefrontCancellationRequests(CancellationRequestId),
        CONSTRAINT FK_StorefrontRefunds_SettlementJournal FOREIGN KEY(SettlementJournalId) REFERENCES finance.JournalEntries(JournalEntryId),
        CONSTRAINT CK_StorefrontRefunds_Provider CHECK(Provider IN(N'RAZORPAY',N'DIRECT_UPI',N'COD')),
        CONSTRAINT CK_StorefrontRefunds_Type CHECK(RefundType IN(N'FULL_CANCEL',N'PARTIAL_RETURN')),
        CONSTRAINT CK_StorefrontRefunds_Status CHECK(RefundStatus IN(N'REFUND_REQUIRED',N'REFUND_PENDING',N'REFUND_FAILED',N'REFUNDED')),
        CONSTRAINT CK_StorefrontRefunds_Amount CHECK(RefundAmount>0),
        CONSTRAINT CK_StorefrontRefunds_Final CHECK(
            (RefundStatus=N'REFUNDED' AND RefundedAt IS NOT NULL AND SettlementJournalId IS NOT NULL
              AND SettlementMode IS NOT NULL AND SettlementReference IS NOT NULL)
            OR (RefundStatus<>N'REFUNDED' AND SettlementJournalId IS NULL))
    );
    CREATE UNIQUE INDEX UX_StorefrontRefunds_FullCancelPayment ON commerce.StorefrontRefunds(TenantId,PaymentId) WHERE RefundType=N'FULL_CANCEL';
    CREATE UNIQUE INDEX UX_StorefrontRefunds_ProviderId ON commerce.StorefrontRefunds(Provider,ProviderRefundId) WHERE ProviderRefundId IS NOT NULL;
    CREATE UNIQUE INDEX UX_StorefrontRefunds_SettlementJournal ON commerce.StorefrontRefunds(SettlementJournalId) WHERE SettlementJournalId IS NOT NULL;
    CREATE INDEX IX_StorefrontRefunds_Order ON commerce.StorefrontRefunds(TenantId,InvoiceId,RequestedAt DESC);
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontRefunds_TenantGuard ON commerce.StorefrontRefunds AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted r
      LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=r.InvoiceId AND i.TenantId=r.TenantId
      LEFT JOIN commerce.CommercePayments p ON p.PaymentId=r.PaymentId AND p.TenantId=r.TenantId AND p.InvoiceId=r.InvoiceId AND p.Provider=r.Provider
      LEFT JOIN commerce.StorefrontCancellationRequests x ON x.CancellationRequestId=r.CancellationRequestId AND x.TenantId=r.TenantId AND x.InvoiceId=r.InvoiceId
      LEFT JOIN core.Users u ON u.Id=r.RequestedBy AND u.TenantId=r.TenantId
      WHERE i.InvoiceId IS NULL OR p.PaymentId IS NULL OR (r.CancellationRequestId IS NOT NULL AND x.CancellationRequestId IS NULL)
        OR (r.RequestedBy IS NOT NULL AND u.Id IS NULL))
        THROW 51000,'Refund tenant ownership is invalid.',1;
END;
GO
IF OBJECT_ID(N'commerce.StorefrontRefundAttempts',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontRefundAttempts(
        RefundAttemptId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontRefundAttempts PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        RefundId uniqueidentifier NOT NULL,
        AttemptNumber int NOT NULL,
        Status nvarchar(20) NOT NULL,
        ProviderRefundId nvarchar(100) NULL,
        SafeFailureCode nvarchar(100) NULL,
        SettlementMode nvarchar(30) NULL,
        ExternalReference nvarchar(100) NULL,
        SettledAt datetimeoffset NULL,
        StartedAt datetimeoffset NOT NULL CONSTRAINT DF_StorefrontRefundAttempts_Started DEFAULT(SYSUTCDATETIME()),
        FinishedAt datetimeoffset NULL,
        CONSTRAINT FK_StorefrontRefundAttempts_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontRefundAttempts_Refund FOREIGN KEY(RefundId) REFERENCES commerce.StorefrontRefunds(RefundId),
        CONSTRAINT UQ_StorefrontRefundAttempts_Number UNIQUE(RefundId,AttemptNumber),
        CONSTRAINT CK_StorefrontRefundAttempts_Status CHECK(Status IN(N'STARTED',N'ACCEPTED',N'UNKNOWN',N'FAILED',N'CONFIRMED')),
        CONSTRAINT CK_StorefrontRefundAttempts_Number CHECK(AttemptNumber>0)
    );
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontRefundAttempts_TenantGuard ON commerce.StorefrontRefundAttempts AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted a LEFT JOIN commerce.StorefrontRefunds r ON r.RefundId=a.RefundId AND r.TenantId=a.TenantId
      WHERE r.RefundId IS NULL) THROW 51000,'Refund attempt tenant ownership is invalid.',1;
END;
GO

-- Internal ERP business reversal. This is not a provider refund or evidence of money movement.
IF OBJECT_ID(N'sales.FullSaleReversals',N'U') IS NULL
BEGIN
    CREATE TABLE sales.FullSaleReversals(
        ReversalId uniqueidentifier NOT NULL CONSTRAINT PK_FullSaleReversals PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        OriginalSaleJournalId uniqueidentifier NOT NULL,
        ReversalJournalId uniqueidentifier NOT NULL,
        OriginalInventoryTransactionId uniqueidentifier NOT NULL,
        InventoryRestorationId uniqueidentifier NOT NULL,
        OriginalGrandTotal decimal(18,2) NOT NULL,
        CollectedAmount decimal(18,2) NOT NULL,
        RefundRequiredAmount decimal(18,2) NOT NULL,
        RefundStatus nvarchar(30) NOT NULL,
        Reason nvarchar(250) NOT NULL,
        CreatedBy nvarchar(256) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_FullSaleReversals_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_FullSaleReversals_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_FullSaleReversals_Invoice FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId),
        CONSTRAINT FK_FullSaleReversals_OriginalSaleJournal FOREIGN KEY(OriginalSaleJournalId) REFERENCES finance.JournalEntries(JournalEntryId),
        CONSTRAINT FK_FullSaleReversals_ReversalJournal FOREIGN KEY(ReversalJournalId) REFERENCES finance.JournalEntries(JournalEntryId),
        CONSTRAINT FK_FullSaleReversals_OriginalStock FOREIGN KEY(OriginalInventoryTransactionId) REFERENCES inventory.InventoryTransactions(TransactionId),
        CONSTRAINT FK_FullSaleReversals_RestoredStock FOREIGN KEY(InventoryRestorationId) REFERENCES inventory.InventoryTransactions(TransactionId),
        CONSTRAINT UQ_FullSaleReversals_Invoice UNIQUE(InvoiceId),
        CONSTRAINT UQ_FullSaleReversals_ReversalJournal UNIQUE(ReversalJournalId),
        CONSTRAINT UQ_FullSaleReversals_RestoredStock UNIQUE(InventoryRestorationId),
        CONSTRAINT CK_FullSaleReversals_Amounts CHECK(OriginalGrandTotal>0 AND CollectedAmount>=0
            AND CollectedAmount<=OriginalGrandTotal AND RefundRequiredAmount=CollectedAmount),
        CONSTRAINT CK_FullSaleReversals_RefundStatus CHECK(
            (CollectedAmount=0 AND RefundStatus=N'NONE') OR
            (CollectedAmount>0 AND RefundStatus IN(N'REFUND_REQUIRED',N'REFUND_PENDING',N'REFUNDED'))),
        CONSTRAINT CK_FullSaleReversals_Reason CHECK(LEN(LTRIM(RTRIM(Reason))) BETWEEN 3 AND 250)
    );
    CREATE INDEX IX_FullSaleReversals_TenantCreated ON sales.FullSaleReversals(TenantId,CreatedAt DESC);
END;
GO
CREATE OR ALTER TRIGGER sales.TR_FullSaleReversals_TenantGuard ON sales.FullSaleReversals AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted r
        LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=r.InvoiceId AND i.TenantId=r.TenantId
        LEFT JOIN finance.JournalEntries original ON original.JournalEntryId=r.OriginalSaleJournalId
            AND original.ReferenceType=N'SALE' AND original.ReferenceId=r.InvoiceId
        LEFT JOIN finance.JournalEntries reversal ON reversal.JournalEntryId=r.ReversalJournalId
            AND reversal.ReferenceType=N'SALE_REVERSAL' AND reversal.ReferenceId=r.ReversalId
        LEFT JOIN inventory.InventoryTransactions stock ON stock.TransactionId=r.OriginalInventoryTransactionId
            AND stock.ReferenceType=N'SALES_INVOICE' AND stock.ReferenceId=r.InvoiceId AND stock.WarehouseId=i.WarehouseId
            AND (stock.TenantId=r.TenantId OR stock.TenantId IS NULL)
        LEFT JOIN inventory.InventoryTransactions restoration ON restoration.TransactionId=r.InventoryRestorationId
            AND restoration.TenantId=r.TenantId AND restoration.ReferenceType=N'FULL_SALE_REVERSAL'
            AND restoration.ReferenceId=r.ReversalId AND restoration.WarehouseId=i.WarehouseId
        WHERE i.InvoiceId IS NULL OR original.JournalEntryId IS NULL OR reversal.JournalEntryId IS NULL
            OR stock.TransactionId IS NULL OR restoration.TransactionId IS NULL)
        THROW 51930,N'Full sale reversal tenant or source ownership is invalid.',1;
END;
GO
