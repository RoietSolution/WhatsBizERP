/* V13 - tenant-scoped, ledger-based loyalty coin foundation. */
IF SCHEMA_ID(N'loyalty') IS NULL EXEC(N'CREATE SCHEMA loyalty');
GO

IF OBJECT_ID(N'loyalty.CoinConfigurations',N'U') IS NULL
CREATE TABLE loyalty.CoinConfigurations(
    TenantId uniqueidentifier NOT NULL CONSTRAINT PK_CoinConfigurations PRIMARY KEY,
    IsEnabled bit NOT NULL CONSTRAINT DF_CoinConfigurations_Enabled DEFAULT 0,
    PurchaseAmount decimal(18,2) NOT NULL CONSTRAINT DF_CoinConfigurations_Amount DEFAULT 100,
    PurchaseCoins int NOT NULL CONSTRAINT DF_CoinConfigurations_Coins DEFAULT 1,
    EarningPriority nvarchar(30) NOT NULL CONSTRAINT DF_CoinConfigurations_Priority DEFAULT N'PRODUCT_FIRST',
    AwardOrderStatus nvarchar(20) NOT NULL CONSTRAINT DF_CoinConfigurations_AwardStatus DEFAULT N'DELIVERED',
    RedemptionCoins int NOT NULL CONSTRAINT DF_CoinConfigurations_RedemptionCoins DEFAULT 100,
    RedemptionValue decimal(18,2) NOT NULL CONSTRAINT DF_CoinConfigurations_RedemptionValue DEFAULT 10,
    MinimumRedemptionCoins int NOT NULL CONSTRAINT DF_CoinConfigurations_Minimum DEFAULT 100,
    MaximumRedemptionCoins int NULL,
    AllowWithOtherDiscounts bit NOT NULL CONSTRAINT DF_CoinConfigurations_Combine DEFAULT 0,
    RestoreRedeemedOnCancel bit NOT NULL CONSTRAINT DF_CoinConfigurations_RestoreCancel DEFAULT 1,
    RestoreRedeemedOnRefund bit NOT NULL CONSTRAINT DF_CoinConfigurations_RestoreRefund DEFAULT 1,
    CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_CoinConfigurations_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(256) NULL, ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT CK_CoinConfigurations_Positive CHECK(PurchaseAmount>0 AND PurchaseCoins>0 AND RedemptionCoins>0 AND RedemptionValue>0 AND MinimumRedemptionCoins>=0 AND (MaximumRedemptionCoins IS NULL OR MaximumRedemptionCoins>=MinimumRedemptionCoins)),
    CONSTRAINT CK_CoinConfigurations_Priority CHECK(EarningPriority IN(N'PRODUCT_FIRST',N'PURCHASE_FIRST')),
    CONSTRAINT CK_CoinConfigurations_Status CHECK(AwardOrderStatus IN(N'COMPLETED',N'DELIVERED'))
);
GO

IF OBJECT_ID(N'loyalty.ProductCoinRules',N'U') IS NULL
CREATE TABLE loyalty.ProductCoinRules(
    ProductCoinRuleId uniqueidentifier NOT NULL CONSTRAINT DF_ProductCoinRules_Id DEFAULT NEWSEQUENTIALID(),
    TenantId uniqueidentifier NOT NULL, ProductId uniqueidentifier NOT NULL,
    IsEnabled bit NOT NULL, CoinsPerUnit int NOT NULL CONSTRAINT DF_ProductCoinRules_Coins DEFAULT 0,
    CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_ProductCoinRules_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(256) NULL, ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL,
    CONSTRAINT PK_ProductCoinRules PRIMARY KEY(ProductCoinRuleId),
    CONSTRAINT UQ_ProductCoinRules UNIQUE(TenantId,ProductId),
    CONSTRAINT FK_ProductCoinRules_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId),
    CONSTRAINT CK_ProductCoinRules_Coins CHECK(CoinsPerUnit>=0)
);
GO

IF OBJECT_ID(N'loyalty.CategoryCoinRules',N'U') IS NULL
CREATE TABLE loyalty.CategoryCoinRules(
    CategoryCoinRuleId uniqueidentifier NOT NULL CONSTRAINT DF_CategoryCoinRules_Id DEFAULT NEWSEQUENTIALID(),
    TenantId uniqueidentifier NOT NULL, ProductCategoryId uniqueidentifier NOT NULL,
    IsEnabled bit NOT NULL, CoinsPerUnit int NOT NULL CONSTRAINT DF_CategoryCoinRules_Coins DEFAULT 0,
    CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_CategoryCoinRules_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(256) NULL, ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL,
    CONSTRAINT PK_CategoryCoinRules PRIMARY KEY(CategoryCoinRuleId),
    CONSTRAINT UQ_CategoryCoinRules UNIQUE(TenantId,ProductCategoryId),
    CONSTRAINT FK_CategoryCoinRules_Category FOREIGN KEY(ProductCategoryId) REFERENCES master.ProductCategories(ProductCategoryId),
    CONSTRAINT CK_CategoryCoinRules_Coins CHECK(CoinsPerUnit>=0)
);
GO

IF OBJECT_ID(N'loyalty.CoinLedger',N'U') IS NULL
CREATE TABLE loyalty.CoinLedger(
    CoinTransactionId uniqueidentifier NOT NULL CONSTRAINT DF_CoinLedger_Id DEFAULT NEWSEQUENTIALID(),
    TenantId uniqueidentifier NOT NULL, CustomerId uniqueidentifier NOT NULL,
    TransactionType nvarchar(30) NOT NULL, Coins int NOT NULL,
    SourceType nvarchar(30) NOT NULL, SourceId uniqueidentifier NULL, EventKey nvarchar(100) NOT NULL,
    RupeeValue decimal(18,2) NULL, Description nvarchar(500) NULL,
    ReversesTransactionId uniqueidentifier NULL,
    CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_CoinLedger_Created DEFAULT SYSUTCDATETIME(), CreatedBy nvarchar(256) NULL,
    CONSTRAINT PK_CoinLedger PRIMARY KEY(CoinTransactionId),
    CONSTRAINT FK_CoinLedger_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
    CONSTRAINT FK_CoinLedger_Reversal FOREIGN KEY(ReversesTransactionId) REFERENCES loyalty.CoinLedger(CoinTransactionId),
    CONSTRAINT CK_CoinLedger_NonZero CHECK(Coins<>0),
    CONSTRAINT CK_CoinLedger_Type CHECK(TransactionType IN(N'EARN',N'REDEEM',N'REVERSE_EARN',N'RESTORE_REDEEM',N'ADJUSTMENT',N'BONUS',N'REFERRAL',N'CAMPAIGN',N'EXPIRY')),
    CONSTRAINT UQ_CoinLedger_Event UNIQUE(TenantId,EventKey)
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'loyalty.CoinLedger') AND name=N'IX_CoinLedger_Customer')
CREATE INDEX IX_CoinLedger_Customer ON loyalty.CoinLedger(TenantId,CustomerId,CreatedOn DESC) INCLUDE(Coins,TransactionType,SourceId,RupeeValue);
GO

IF OBJECT_ID(N'loyalty.OrderCoins',N'U') IS NULL
CREATE TABLE loyalty.OrderCoins(
    OrderId uniqueidentifier NOT NULL, TenantId uniqueidentifier NOT NULL, CustomerId uniqueidentifier NOT NULL,
    EarnedCoins int NOT NULL CONSTRAINT DF_OrderCoins_Earned DEFAULT 0,
    RedeemedCoins int NOT NULL CONSTRAINT DF_OrderCoins_Redeemed DEFAULT 0,
    RedemptionDiscount decimal(18,2) NOT NULL CONSTRAINT DF_OrderCoins_Discount DEFAULT 0,
    EarnedOn datetimeoffset NULL, EarnReversedOn datetimeoffset NULL, RedeemedOn datetimeoffset NULL, RedemptionRestoredOn datetimeoffset NULL,
    CONSTRAINT PK_OrderCoins PRIMARY KEY(OrderId),
    CONSTRAINT FK_OrderCoins_Order FOREIGN KEY(OrderId) REFERENCES sales.SalesInvoices(InvoiceId),
    CONSTRAINT FK_OrderCoins_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
    CONSTRAINT CK_OrderCoins_Values CHECK(EarnedCoins>=0 AND RedeemedCoins>=0 AND RedemptionDiscount>=0)
);
GO

CREATE OR ALTER PROCEDURE loyalty.RedeemForOrder
    @TenantId uniqueidentifier,@CustomerId uniqueidentifier,@OrderId uniqueidentifier,@Coins int,@OtherDiscount decimal(18,2)=0,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @Coins<=0 RETURN;
 DECLARE @lock int,@balance int,@rateCoins int,@rateValue decimal(18,2),@minimum int,@maximum int,@combine bit,@discount decimal(18,2),@lockResource nvarchar(255);
 SET @lockResource=CONCAT(N'loyalty:',@TenantId,N':',@CustomerId);
 EXEC @lock=sys.sp_getapplock @Resource=@lockResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
 IF @lock<0 THROW 51131,N'Unable to lock the customer coin balance.',1;
 SELECT @rateCoins=RedemptionCoins,@rateValue=RedemptionValue,@minimum=MinimumRedemptionCoins,@maximum=MaximumRedemptionCoins,@combine=AllowWithOtherDiscounts FROM loyalty.CoinConfigurations WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND IsEnabled=1;
 IF @rateCoins IS NULL THROW 51132,N'The coin system is not enabled.',1;
 IF @Coins<@minimum OR (@maximum IS NOT NULL AND @Coins>@maximum) OR @Coins%@rateCoins<>0 THROW 51133,N'The requested coin redemption does not meet the configured limits or conversion increment.',1;
 IF NOT EXISTS(SELECT 1 FROM sales.SalesInvoices WHERE InvoiceId=@OrderId AND CustomerId=@CustomerId) THROW 51134,N'The order is not available for coin redemption.',1;
 IF @combine=0 AND @OtherDiscount>0 THROW 51135,N'Coins cannot be combined with another discount.',1;
 SELECT @balance=ISNULL(SUM(Coins),0) FROM loyalty.CoinLedger WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND CustomerId=@CustomerId;
 IF @balance<@Coins THROW 51136,N'Insufficient coin balance.',1;
 SET @discount=ROUND(CONVERT(decimal(18,4),@Coins)/@rateCoins*@rateValue,2);
 INSERT loyalty.CoinLedger(TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,RupeeValue,Description,CreatedBy)
 VALUES(@TenantId,@CustomerId,N'REDEEM',-@Coins,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':REDEEM'),@discount,N'Coins redeemed at checkout',@CreatedBy);
 INSERT loyalty.OrderCoins(OrderId,TenantId,CustomerId,RedeemedCoins,RedemptionDiscount,RedeemedOn)
 VALUES(@OrderId,@TenantId,@CustomerId,@Coins,@discount,SYSUTCDATETIME());
END
GO

CREATE OR ALTER PROCEDURE loyalty.ProcessOrder
 @TenantId uniqueidentifier,@OrderId uniqueidentifier,@EventStatus nvarchar(20),@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @customer uniqueidentifier,@enabled bit,@awardStatus nvarchar(20),@priority nvarchar(30),@amount decimal(18,2),@purchaseCoins int,@earned int=0,@redemption int,@restoreCancel bit,@restoreRefund bit,@event nvarchar(20)=UPPER(@EventStatus);
 SELECT @customer=CustomerId FROM sales.SalesInvoices WHERE InvoiceId=@OrderId;
 IF @customer IS NULL RETURN;
 IF @event=N'CURRENT' SELECT @event=Status FROM sales.SalesInvoices WHERE InvoiceId=@OrderId;
 SELECT @enabled=IsEnabled,@awardStatus=AwardOrderStatus,@priority=EarningPriority,@amount=PurchaseAmount,@purchaseCoins=PurchaseCoins,@restoreCancel=RestoreRedeemedOnCancel,@restoreRefund=RestoreRedeemedOnRefund FROM loyalty.CoinConfigurations WHERE TenantId=@TenantId;
 IF ISNULL(@enabled,0)=0 RETURN;
 IF (@event=@awardStatus OR (@awardStatus=N'COMPLETED' AND @event=N'DELIVERED')) AND NOT EXISTS(SELECT 1 FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':EARN'))
 BEGIN
   ;WITH L AS(SELECT i.Quantity,i.LineTotal,p.CategoryId,pr.ProductId AS HasProduct,pr.IsEnabled AS ProductEnabled,pr.CoinsPerUnit AS ProductCoins,cr.ProductCategoryId AS HasCategory,cr.IsEnabled AS CategoryEnabled,cr.CoinsPerUnit AS CategoryCoins FROM sales.SalesInvoiceItems i JOIN master.Products p ON p.ProductId=i.ProductId LEFT JOIN loyalty.ProductCoinRules pr ON pr.TenantId=@TenantId AND pr.ProductId=i.ProductId LEFT JOIN loyalty.CategoryCoinRules cr ON cr.TenantId=@TenantId AND cr.ProductCategoryId=p.CategoryId WHERE i.InvoiceId=@OrderId)
   SELECT @earned=CONVERT(int,
      ISNULL(SUM(CASE WHEN HasProduct IS NOT NULL AND ProductEnabled=0 THEN 0 WHEN @priority=N'PRODUCT_FIRST' AND HasProduct IS NOT NULL THEN FLOOR(Quantity*ProductCoins) WHEN @priority=N'PRODUCT_FIRST' AND HasCategory IS NOT NULL AND CategoryEnabled=1 THEN FLOOR(Quantity*CategoryCoins) ELSE 0 END),0)
      + FLOOR(ISNULL(SUM(CASE WHEN HasProduct IS NOT NULL AND ProductEnabled=0 THEN 0 WHEN @priority=N'PRODUCT_FIRST' AND (HasProduct IS NOT NULL OR HasCategory IS NOT NULL) THEN 0 ELSE LineTotal END),0)/@amount)*@purchaseCoins)
   FROM L;
   IF @earned>0
   BEGIN
    INSERT loyalty.CoinLedger(TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,CreatedBy) VALUES(@TenantId,@customer,N'EARN',@earned,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':EARN'),N'Coins earned from order',@CreatedBy);
    IF EXISTS(SELECT 1 FROM loyalty.OrderCoins WHERE OrderId=@OrderId) UPDATE loyalty.OrderCoins SET EarnedCoins=@earned,EarnedOn=SYSUTCDATETIME() WHERE OrderId=@OrderId;
    ELSE INSERT loyalty.OrderCoins(OrderId,TenantId,CustomerId,EarnedCoins,EarnedOn) VALUES(@OrderId,@TenantId,@customer,@earned,SYSUTCDATETIME());
   END
 END
 IF @event IN(N'CANCELLED',N'VOID',N'RETURNED',N'REFUNDED')
 BEGIN
   DECLARE @earnId uniqueidentifier,@earnCoins int;
   SELECT @earnId=CoinTransactionId,@earnCoins=Coins FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':EARN');
   IF @earnId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':REVERSE_EARN'))
   BEGIN INSERT loyalty.CoinLedger(TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,ReversesTransactionId,CreatedBy) VALUES(@TenantId,@customer,N'REVERSE_EARN',-@earnCoins,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':REVERSE_EARN'),N'Order earning reversed',@earnId,@CreatedBy); UPDATE loyalty.OrderCoins SET EarnReversedOn=SYSUTCDATETIME() WHERE OrderId=@OrderId; END
   SELECT @redemption=-Coins FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':REDEEM');
   IF @redemption>0 AND ((@event IN(N'CANCELLED',N'VOID') AND @restoreCancel=1) OR (@event IN(N'RETURNED',N'REFUNDED') AND @restoreRefund=1)) AND NOT EXISTS(SELECT 1 FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':RESTORE_REDEEM'))
   BEGIN INSERT loyalty.CoinLedger(TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,CreatedBy) VALUES(@TenantId,@customer,N'RESTORE_REDEEM',@redemption,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':RESTORE_REDEEM'),N'Redeemed coins restored',@CreatedBy); UPDATE loyalty.OrderCoins SET RedemptionRestoredOn=SYSUTCDATETIME() WHERE OrderId=@OrderId; END
 END
END
GO
