/* V15 - customer-to-customer referral rewards. Idempotent; no retailer referral model. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF SCHEMA_ID(N'loyalty') IS NULL EXEC(N'CREATE SCHEMA loyalty');
GO

IF COL_LENGTH(N'loyalty.CoinLedger',N'ExpiresAt') IS NULL ALTER TABLE loyalty.CoinLedger ADD ExpiresAt datetimeoffset NULL;
IF COL_LENGTH(N'loyalty.CoinLedger',N'SystemSource') IS NULL ALTER TABLE loyalty.CoinLedger ADD SystemSource nvarchar(50) NULL;
GO
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'loyalty.CoinLedger') AND name=N'CK_CoinLedger_Type') ALTER TABLE loyalty.CoinLedger DROP CONSTRAINT CK_CoinLedger_Type;
ALTER TABLE loyalty.CoinLedger WITH CHECK ADD CONSTRAINT CK_CoinLedger_Type CHECK(TransactionType IN(N'EARN',N'REDEEM',N'REVERSE_EARN',N'RESTORE_REDEEM',N'ADJUSTMENT',N'BONUS',N'REFERRAL',N'CAMPAIGN',N'EXPIRY',N'REVERSAL'));
GO

IF OBJECT_ID(N'loyalty.ReferralConfigurations',N'U') IS NULL
CREATE TABLE loyalty.ReferralConfigurations(
 TenantId uniqueidentifier NOT NULL CONSTRAINT PK_ReferralConfigurations PRIMARY KEY,
 IsEnabled bit NOT NULL CONSTRAINT DF_ReferralConfigurations_Enabled DEFAULT 0,
 QualificationType nvarchar(50) NOT NULL CONSTRAINT DF_ReferralConfigurations_Qualification DEFAULT N'FIRST_COMPLETED_ORDER',
 MinimumQualifyingAmount decimal(18,2) NOT NULL CONSTRAINT DF_ReferralConfigurations_MinAmount DEFAULT 0,
 ReferrerRewardCoins int NOT NULL CONSTRAINT DF_ReferralConfigurations_Referrer DEFAULT 200,
 ReferredRewardCoins int NOT NULL CONSTRAINT DF_ReferralConfigurations_Referred DEFAULT 100,
 CoinValidityDays int NOT NULL CONSTRAINT DF_ReferralConfigurations_Validity DEFAULT 180,
 MaximumRewardedReferralsPerCustomerMonth int NOT NULL CONSTRAINT DF_ReferralConfigurations_MaxReferrals DEFAULT 10,
 MaximumCoinsPerCustomerMonth int NOT NULL CONSTRAINT DF_ReferralConfigurations_MaxCoins DEFAULT 2000,
 ReverseOnRefund bit NOT NULL CONSTRAINT DF_ReferralConfigurations_Reverse DEFAULT 1,
 RedemptionCoins int NOT NULL CONSTRAINT DF_ReferralConfigurations_RedemptionCoins DEFAULT 100,
 RedemptionValue decimal(18,2) NOT NULL CONSTRAINT DF_ReferralConfigurations_RedemptionValue DEFAULT 10,
 MinimumRedemptionCoins int NOT NULL CONSTRAINT DF_ReferralConfigurations_MinRedemption DEFAULT 100,
 MaximumOrderPercentage decimal(5,2) NOT NULL CONSTRAINT DF_ReferralConfigurations_MaxPercent DEFAULT 20,
 AllowWithCoupons bit NOT NULL CONSTRAINT DF_ReferralConfigurations_Coupons DEFAULT 0,
 AllowDiscountedProducts bit NOT NULL CONSTRAINT DF_ReferralConfigurations_Discounted DEFAULT 1,
 AllowTax bit NOT NULL CONSTRAINT DF_ReferralConfigurations_Tax DEFAULT 0,
 AllowDelivery bit NOT NULL CONSTRAINT DF_ReferralConfigurations_Delivery DEFAULT 0,
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_ReferralConfigurations_Created DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL,ModifiedBy nvarchar(256) NULL,RowVersion rowversion NOT NULL,
 CONSTRAINT CK_ReferralConfigurations_Qualification CHECK(QualificationType IN(N'CUSTOMER_REGISTERED',N'FIRST_ORDER_PLACED',N'FIRST_PAID_ORDER',N'FIRST_COMPLETED_ORDER',N'FIRST_COMPLETED_ORDER_MIN_AMOUNT',N'MANUAL_APPROVAL')),
 CONSTRAINT CK_ReferralConfigurations_Values CHECK(MinimumQualifyingAmount>=0 AND ReferrerRewardCoins>=0 AND ReferredRewardCoins>=0 AND CoinValidityDays>0 AND MaximumRewardedReferralsPerCustomerMonth>0 AND MaximumCoinsPerCustomerMonth>0 AND RedemptionCoins>0 AND RedemptionValue>0 AND MinimumRedemptionCoins>=0 AND MaximumOrderPercentage BETWEEN 0 AND 100)
);
GO

IF OBJECT_ID(N'loyalty.CustomerReferralCodes',N'U') IS NULL
CREATE TABLE loyalty.CustomerReferralCodes(
 CustomerReferralCodeId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerReferralCodes PRIMARY KEY,
 TenantId uniqueidentifier NOT NULL,CustomerId uniqueidentifier NOT NULL,ReferralCode varchar(20) COLLATE Latin1_General_100_CI_AS NOT NULL,
 IsActive bit NOT NULL CONSTRAINT DF_CustomerReferralCodes_Active DEFAULT 1,
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_CustomerReferralCodes_Created DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL,ModifiedBy nvarchar(256) NULL,RowVersion rowversion NOT NULL,
 CONSTRAINT FK_CustomerReferralCodes_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
 CONSTRAINT UQ_CustomerReferralCodes_TenantCustomer UNIQUE(TenantId,CustomerId),
 CONSTRAINT UQ_CustomerReferralCodes_Code UNIQUE(ReferralCode),
 CONSTRAINT CK_CustomerReferralCodes_Code CHECK(ReferralCode NOT LIKE '%[^A-Z2-9]%' AND LEN(ReferralCode) BETWEEN 6 AND 20)
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'loyalty.CustomerReferralCodes') AND name=N'IX_CustomerReferralCodes_Resolve') CREATE INDEX IX_CustomerReferralCodes_Resolve ON loyalty.CustomerReferralCodes(ReferralCode,IsActive) INCLUDE(TenantId,CustomerId);
GO

IF OBJECT_ID(N'loyalty.CustomerReferrals',N'U') IS NULL
CREATE TABLE loyalty.CustomerReferrals(
 CustomerReferralId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerReferrals PRIMARY KEY,
 TenantId uniqueidentifier NOT NULL,ReferrerCustomerId uniqueidentifier NOT NULL,ReferredCustomerId uniqueidentifier NOT NULL,
 CustomerReferralCodeId uniqueidentifier NOT NULL,Status nvarchar(20) NOT NULL,QualificationType nvarchar(50) NOT NULL,
 CaptureSource nvarchar(30) NOT NULL CONSTRAINT DF_CustomerReferrals_Source DEFAULT N'WEB',AttributionLockedAt datetimeoffset NOT NULL CONSTRAINT DF_CustomerReferrals_Locked DEFAULT SYSUTCDATETIME(),
 QualifyingOrderId uniqueidentifier NULL,QualifiedAt datetimeoffset NULL,RewardedAt datetimeoffset NULL,ReversedAt datetimeoffset NULL,
 RejectionReason nvarchar(500) NULL,CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_CustomerReferrals_Created DEFAULT SYSUTCDATETIME(),CreatedBy nvarchar(256) NULL,ModifiedOn datetimeoffset NULL,ModifiedBy nvarchar(256) NULL,RowVersion rowversion NOT NULL,
 CONSTRAINT FK_CustomerReferrals_Code FOREIGN KEY(CustomerReferralCodeId) REFERENCES loyalty.CustomerReferralCodes(CustomerReferralCodeId),
 CONSTRAINT FK_CustomerReferrals_Referrer FOREIGN KEY(ReferrerCustomerId) REFERENCES sales.Customers(CustomerId),
 CONSTRAINT FK_CustomerReferrals_Referred FOREIGN KEY(ReferredCustomerId) REFERENCES sales.Customers(CustomerId),
 CONSTRAINT UQ_CustomerReferrals_OneReferrer UNIQUE(TenantId,ReferredCustomerId),
 CONSTRAINT CK_CustomerReferrals_Different CHECK(ReferrerCustomerId<>ReferredCustomerId),
 CONSTRAINT CK_CustomerReferrals_Status CHECK(Status IN(N'PENDING',N'QUALIFIED',N'REWARDED',N'REJECTED',N'CANCELLED',N'REVERSED'))
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'loyalty.CustomerReferrals') AND name=N'IX_CustomerReferrals_Status') CREATE INDEX IX_CustomerReferrals_Status ON loyalty.CustomerReferrals(TenantId,Status,CreatedOn) INCLUDE(ReferrerCustomerId,ReferredCustomerId,QualifyingOrderId,RewardedAt);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'loyalty.CustomerReferrals') AND name=N'IX_CustomerReferrals_Order') CREATE INDEX IX_CustomerReferrals_Order ON loyalty.CustomerReferrals(TenantId,QualifyingOrderId) WHERE QualifyingOrderId IS NOT NULL;
GO

IF OBJECT_ID(N'loyalty.ReferralAuditEvents',N'U') IS NULL
CREATE TABLE loyalty.ReferralAuditEvents(ReferralAuditEventId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReferralAuditEvents PRIMARY KEY,TenantId uniqueidentifier NOT NULL,CustomerReferralId uniqueidentifier NULL,EventType nvarchar(50) NOT NULL,Reason nvarchar(500) NULL,OccurredOn datetimeoffset NOT NULL CONSTRAINT DF_ReferralAuditEvents_Occurred DEFAULT SYSUTCDATETIME(),Actor nvarchar(256) NULL,SystemSource nvarchar(50) NOT NULL,CONSTRAINT FK_ReferralAuditEvents_Referral FOREIGN KEY(CustomerReferralId) REFERENCES loyalty.CustomerReferrals(CustomerReferralId));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'loyalty.ReferralAuditEvents') AND name=N'IX_ReferralAuditEvents_Tenant') CREATE INDEX IX_ReferralAuditEvents_Tenant ON loyalty.ReferralAuditEvents(TenantId,OccurredOn DESC) INCLUDE(CustomerReferralId,EventType,SystemSource);
GO

IF OBJECT_ID(N'loyalty.CustomerRewardLots',N'U') IS NULL
CREATE TABLE loyalty.CustomerRewardLots(
 CustomerRewardLotId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerRewardLots PRIMARY KEY,
 TenantId uniqueidentifier NOT NULL,CustomerId uniqueidentifier NOT NULL,CreditTransactionId uniqueidentifier NOT NULL,
 OriginalCoins int NOT NULL,RemainingCoins int NOT NULL,ExpiresAt datetimeoffset NULL,CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_CustomerRewardLots_Created DEFAULT SYSUTCDATETIME(),
 CONSTRAINT FK_CustomerRewardLots_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),CONSTRAINT FK_CustomerRewardLots_Ledger FOREIGN KEY(CreditTransactionId) REFERENCES loyalty.CoinLedger(CoinTransactionId),
 CONSTRAINT UQ_CustomerRewardLots_Credit UNIQUE(CreditTransactionId),CONSTRAINT CK_CustomerRewardLots_Coins CHECK(OriginalCoins>0 AND RemainingCoins BETWEEN 0 AND OriginalCoins)
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'loyalty.CustomerRewardLots') AND name=N'IX_CustomerRewardLots_FIFO') CREATE INDEX IX_CustomerRewardLots_FIFO ON loyalty.CustomerRewardLots(TenantId,CustomerId,ExpiresAt,CreatedOn) INCLUDE(RemainingCoins) WHERE RemainingCoins>0;
GO
IF OBJECT_ID(N'loyalty.CustomerRewardConsumptions',N'U') IS NULL
CREATE TABLE loyalty.CustomerRewardConsumptions(CustomerRewardConsumptionId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerRewardConsumptions PRIMARY KEY,DebitTransactionId uniqueidentifier NOT NULL,CustomerRewardLotId uniqueidentifier NOT NULL,Coins int NOT NULL,CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_CustomerRewardConsumptions_Created DEFAULT SYSUTCDATETIME(),CONSTRAINT FK_CustomerRewardConsumptions_Debit FOREIGN KEY(DebitTransactionId) REFERENCES loyalty.CoinLedger(CoinTransactionId),CONSTRAINT FK_CustomerRewardConsumptions_Lot FOREIGN KEY(CustomerRewardLotId) REFERENCES loyalty.CustomerRewardLots(CustomerRewardLotId),CONSTRAINT UQ_CustomerRewardConsumptions UNIQUE(DebitTransactionId,CustomerRewardLotId),CONSTRAINT CK_CustomerRewardConsumptions_Coins CHECK(Coins>0));
GO

CREATE OR ALTER VIEW loyalty.CustomerRewardWallets AS
SELECT c.TenantId,c.CustomerId,CONVERT(int,ISNULL(SUM(l.Coins),0)) AvailableBalance,
 CONVERT(int,ISNULL(SUM(CASE WHEN l.Coins>0 AND l.TransactionType NOT IN(N'RESTORE_REDEEM') THEN l.Coins ELSE 0 END),0)) LifetimeEarned,
 CONVERT(int,ISNULL(SUM(CASE WHEN l.TransactionType=N'REDEEM' THEN -l.Coins ELSE 0 END),0)) LifetimeRedeemed,
 MAX(l.CreatedOn) UpdatedAt
FROM sales.Customers c LEFT JOIN loyalty.CoinLedger l ON l.CustomerId=c.CustomerId AND l.TenantId=c.TenantId GROUP BY c.TenantId,c.CustomerId;
GO

CREATE OR ALTER PROCEDURE loyalty.RedeemForOrder
 @TenantId uniqueidentifier,@CustomerId uniqueidentifier,@OrderId uniqueidentifier,@Coins int,@OtherDiscount decimal(18,2)=0,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;IF @Coins<=0 RETURN;DECLARE @lock int,@balance int,@rateCoins int,@minimum int,@maximum int,@combine bit,@rateValue decimal(18,2),@maxPercent decimal(5,2)=100,@discount decimal(18,2),@orderTotal decimal(18,2),@debit uniqueidentifier=NEWID(),@lockResource nvarchar(255);
 SET @lockResource=CONCAT(N'loyalty:',@TenantId,N':',@CustomerId);BEGIN TRAN;EXEC @lock=sys.sp_getapplock @Resource=@lockResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;IF @lock<0 THROW 51131,N'Unable to lock the customer coin balance.',1;
 SELECT @rateCoins=RedemptionCoins,@rateValue=RedemptionValue,@minimum=MinimumRedemptionCoins,@maximum=MaximumRedemptionCoins,@combine=AllowWithOtherDiscounts FROM loyalty.CoinConfigurations WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND IsEnabled=1;
 SELECT @rateCoins=COALESCE(RedemptionCoins,@rateCoins),@rateValue=COALESCE(RedemptionValue,@rateValue),@minimum=COALESCE(MinimumRedemptionCoins,@minimum),@maxPercent=MaximumOrderPercentage,@combine=AllowWithCoupons FROM loyalty.ReferralConfigurations WHERE TenantId=@TenantId AND IsEnabled=1;
 IF @rateCoins IS NULL THROW 51132,N'The coin system is not enabled.',1;IF @Coins<@minimum OR(@maximum IS NOT NULL AND @Coins>@maximum)OR @Coins%@rateCoins<>0 THROW 51133,N'The requested redemption does not meet configured limits.',1;
 SELECT @orderTotal=i.GrandTotal FROM sales.SalesInvoices i JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=@TenantId WHERE i.InvoiceId=@OrderId AND i.CustomerId=@CustomerId;IF @orderTotal IS NULL THROW 51134,N'The order is not available for this tenant and customer.',1;IF @combine=0 AND @OtherDiscount>0 THROW 51135,N'Coins cannot be combined with another discount or coupon.',1;
 SELECT @balance=ISNULL(SUM(Coins),0) FROM loyalty.CoinLedger WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND CustomerId=@CustomerId;IF @balance<@Coins THROW 51136,N'Insufficient coin balance.',1;SET @discount=ROUND(CONVERT(decimal(18,4),@Coins)/@rateCoins*@rateValue,2);IF @discount>@orderTotal*@maxPercent/100 THROW 51137,N'Redemption exceeds the maximum payable order percentage.',1;
 INSERT loyalty.CoinLedger(CoinTransactionId,TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,RupeeValue,Description,SystemSource,CreatedBy) VALUES(@debit,@TenantId,@CustomerId,N'REDEEM',-@Coins,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':REDEEM'),@discount,N'Coins redeemed at checkout',N'CHECKOUT',@CreatedBy);
 DECLARE @remaining int=@Coins,@lot uniqueidentifier,@available int,@use int;DECLARE fifo CURSOR LOCAL FAST_FORWARD FOR SELECT CustomerRewardLotId,RemainingCoins FROM loyalty.CustomerRewardLots WHERE TenantId=@TenantId AND CustomerId=@CustomerId AND RemainingCoins>0 AND(ExpiresAt IS NULL OR ExpiresAt>SYSUTCDATETIME()) ORDER BY CASE WHEN ExpiresAt IS NULL THEN 1 ELSE 0 END,ExpiresAt,CreatedOn,CustomerRewardLotId;OPEN fifo;FETCH NEXT FROM fifo INTO @lot,@available;WHILE @@FETCH_STATUS=0 AND @remaining>0 BEGIN SET @use=CASE WHEN @available>@remaining THEN @remaining ELSE @available END;UPDATE loyalty.CustomerRewardLots SET RemainingCoins=RemainingCoins-@use WHERE CustomerRewardLotId=@lot;INSERT loyalty.CustomerRewardConsumptions(CustomerRewardConsumptionId,DebitTransactionId,CustomerRewardLotId,Coins)VALUES(NEWID(),@debit,@lot,@use);SET @remaining-=@use;FETCH NEXT FROM fifo INTO @lot,@available;END;CLOSE fifo;DEALLOCATE fifo;
 INSERT loyalty.OrderCoins(OrderId,TenantId,CustomerId,RedeemedCoins,RedemptionDiscount,RedeemedOn) VALUES(@OrderId,@TenantId,@CustomerId,@Coins,@discount,SYSUTCDATETIME());COMMIT;
END
GO

CREATE OR ALTER PROCEDURE loyalty.AwardCustomerReferral @TenantId uniqueidentifier,@ReferralId uniqueidentifier,@CreatedBy nvarchar(256)=NULL AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;
 DECLARE @referrer uniqueidentifier,@referred uniqueidentifier,@status nvarchar(20),@a int,@b int,@days int,@maxRefs int,@maxCoins int,@now datetimeoffset=SYSUTCDATETIME(),@expiry datetimeoffset,@lock int,@lockResource nvarchar(255);
 SET @lockResource=CONCAT(N'referral:',@TenantId,N':',@ReferralId);EXEC @lock=sys.sp_getapplock @Resource=@lockResource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;IF @lock<0 THROW 51220,N'Unable to lock referral reward.',1;
 SELECT @referrer=ReferrerCustomerId,@referred=ReferredCustomerId,@status=Status FROM loyalty.CustomerReferrals WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND CustomerReferralId=@ReferralId;
 IF @status=N'REWARDED' BEGIN COMMIT;RETURN;END; IF @status<>N'QUALIFIED' THROW 51221,N'Only a qualified referral can be rewarded.',1;
 SELECT @a=ReferrerRewardCoins,@b=ReferredRewardCoins,@days=CoinValidityDays,@maxRefs=MaximumRewardedReferralsPerCustomerMonth,@maxCoins=MaximumCoinsPerCustomerMonth FROM loyalty.ReferralConfigurations WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND IsEnabled=1;
 IF @a IS NULL THROW 51222,N'Referral rewards are disabled.',1;
 IF (SELECT COUNT(*) FROM loyalty.CustomerReferrals WHERE TenantId=@TenantId AND ReferrerCustomerId=@referrer AND Status IN(N'REWARDED',N'REVERSED') AND RewardedAt>=DATEFROMPARTS(YEAR(@now),MONTH(@now),1))>=@maxRefs
 BEGIN UPDATE loyalty.CustomerReferrals SET Status=N'REJECTED',RejectionReason=N'Monthly rewarded-referral limit reached.',ModifiedOn=@now,ModifiedBy=@CreatedBy WHERE TenantId=@TenantId AND CustomerReferralId=@ReferralId;INSERT loyalty.ReferralAuditEvents(TenantId,CustomerReferralId,EventType,Reason,Actor,SystemSource) VALUES(@TenantId,@ReferralId,N'REJECTED',N'Monthly rewarded-referral limit reached.',@CreatedBy,N'REFERRAL_ENGINE');COMMIT;RETURN;END;
 IF ISNULL((SELECT SUM(Coins) FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND CustomerId=@referrer AND SourceType=N'REFERRAL' AND Coins>0 AND CreatedOn>=DATEFROMPARTS(YEAR(@now),MONTH(@now),1)),0)+@a>@maxCoins
 BEGIN UPDATE loyalty.CustomerReferrals SET Status=N'REJECTED',RejectionReason=N'Monthly referral-coin limit reached.',ModifiedOn=@now,ModifiedBy=@CreatedBy WHERE TenantId=@TenantId AND CustomerReferralId=@ReferralId;INSERT loyalty.ReferralAuditEvents(TenantId,CustomerReferralId,EventType,Reason,Actor,SystemSource) VALUES(@TenantId,@ReferralId,N'REJECTED',N'Monthly referral-coin limit reached.',@CreatedBy,N'REFERRAL_ENGINE');COMMIT;RETURN;END;
 SET @expiry=DATEADD(day,@days,@now);
 DECLARE @credits TABLE(Id uniqueidentifier,CustomerId uniqueidentifier,Coins int);
 IF @a>0 BEGIN DECLARE @aid uniqueidentifier=NEWID();INSERT loyalty.CoinLedger(CoinTransactionId,TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,RupeeValue,Description,ExpiresAt,SystemSource,CreatedBy) VALUES(@aid,@TenantId,@referrer,N'REFERRAL',@a,N'REFERRAL',@ReferralId,CONCAT(N'REFERRAL:',@ReferralId,N':REFERRER'),NULL,N'Referral reward',@expiry,N'REFERRAL_ENGINE',@CreatedBy);INSERT @credits VALUES(@aid,@referrer,@a);END;
 IF @b>0 BEGIN DECLARE @bid uniqueidentifier=NEWID();INSERT loyalty.CoinLedger(CoinTransactionId,TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,RupeeValue,Description,ExpiresAt,SystemSource,CreatedBy) VALUES(@bid,@TenantId,@referred,N'REFERRAL',@b,N'REFERRAL',@ReferralId,CONCAT(N'REFERRAL:',@ReferralId,N':REFERRED'),NULL,N'Welcome referral reward',@expiry,N'REFERRAL_ENGINE',@CreatedBy);INSERT @credits VALUES(@bid,@referred,@b);END;
 INSERT loyalty.CustomerRewardLots(CustomerRewardLotId,TenantId,CustomerId,CreditTransactionId,OriginalCoins,RemainingCoins,ExpiresAt) SELECT NEWID(),@TenantId,CustomerId,Id,Coins,Coins,@expiry FROM @credits;
 UPDATE loyalty.CustomerReferrals SET Status=N'REWARDED',RewardedAt=@now,ModifiedOn=@now,ModifiedBy=@CreatedBy WHERE CustomerReferralId=@ReferralId AND TenantId=@TenantId;
 INSERT loyalty.ReferralAuditEvents(TenantId,CustomerReferralId,EventType,Actor,SystemSource) VALUES(@TenantId,@ReferralId,N'REWARDED',@CreatedBy,N'REFERRAL_ENGINE');
 COMMIT;
END
GO

CREATE OR ALTER PROCEDURE loyalty.CaptureCustomerReferral @TenantId uniqueidentifier,@ReferralCode varchar(20),@ReferredCustomerId uniqueidentifier,@CaptureSource nvarchar(30)=N'WEB',@CreatedBy nvarchar(256)=NULL AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;DECLARE @codeId uniqueidentifier,@referrer uniqueidentifier,@qualification nvarchar(50),@id uniqueidentifier=NEWID();
 SELECT @codeId=rc.CustomerReferralCodeId,@referrer=rc.CustomerId FROM loyalty.CustomerReferralCodes rc JOIN loyalty.ReferralConfigurations cfg ON cfg.TenantId=rc.TenantId AND cfg.IsEnabled=1 WHERE rc.TenantId=@TenantId AND rc.ReferralCode=UPPER(LTRIM(RTRIM(@ReferralCode))) AND rc.IsActive=1;
 IF @codeId IS NULL THROW 51201,N'The referral code is invalid or disabled.',1;
 IF NOT EXISTS(SELECT 1 FROM sales.Customers WHERE TenantId=@TenantId AND CustomerId=@ReferredCustomerId AND IsDeleted=0) THROW 51202,N'The referred customer does not belong to this retailer.',1;
 IF @referrer=@ReferredCustomerId THROW 51203,N'Self-referrals are not allowed.',1;
 IF EXISTS(SELECT 1 FROM sales.Customers a JOIN sales.Customers b ON b.CustomerId=@ReferredCustomerId AND b.TenantId=@TenantId WHERE a.CustomerId=@referrer AND a.TenantId=@TenantId AND NULLIF(REPLACE(REPLACE(REPLACE(a.Mobile,N' ',N''),N'+',N''),N'-',N''),N'')=NULLIF(REPLACE(REPLACE(REPLACE(b.Mobile,N' ',N''),N'+',N''),N'-',N''),N'')) THROW 51204,N'Customers with the same mobile identity cannot refer one another.',1;
 IF EXISTS(SELECT 1 FROM loyalty.CustomerReferrals WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND ReferredCustomerId=@ReferredCustomerId) THROW 51205,N'This customer already has a locked referral attribution.',1;
 SELECT @qualification=QualificationType FROM loyalty.ReferralConfigurations WHERE TenantId=@TenantId;
 INSERT loyalty.CustomerReferrals(CustomerReferralId,TenantId,ReferrerCustomerId,ReferredCustomerId,CustomerReferralCodeId,Status,QualificationType,CaptureSource,QualifiedAt,CreatedBy)
 VALUES(@id,@TenantId,@referrer,@ReferredCustomerId,@codeId,CASE WHEN @qualification=N'CUSTOMER_REGISTERED' THEN N'QUALIFIED' ELSE N'PENDING' END,@qualification,UPPER(@CaptureSource),CASE WHEN @qualification=N'CUSTOMER_REGISTERED' THEN SYSUTCDATETIME() END,@CreatedBy);
 INSERT loyalty.ReferralAuditEvents(TenantId,CustomerReferralId,EventType,Actor,SystemSource) VALUES(@TenantId,@id,N'CAPTURED',@CreatedBy,UPPER(@CaptureSource));
 COMMIT;IF @qualification=N'CUSTOMER_REGISTERED' EXEC loyalty.AwardCustomerReferral @TenantId,@id,@CreatedBy;SELECT @id;
END
GO

CREATE OR ALTER PROCEDURE loyalty.ProcessReferralOrder @TenantId uniqueidentifier,@OrderId uniqueidentifier,@EventStatus nvarchar(20),@CreatedBy nvarchar(256)=NULL AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;DECLARE @customer uniqueidentifier,@total decimal(18,2),@paid decimal(18,2),@referral uniqueidentifier,@rule nvarchar(50),@minimum decimal(18,2),@event nvarchar(20)=UPPER(@EventStatus),@qualifies bit=0;
 SELECT @customer=i.CustomerId,@total=i.GrandTotal,@paid=i.PaidAmount FROM sales.SalesInvoices i JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=@TenantId WHERE i.InvoiceId=@OrderId;
 IF @customer IS NULL RETURN;SELECT @referral=CustomerReferralId,@rule=QualificationType FROM loyalty.CustomerReferrals WHERE TenantId=@TenantId AND ReferredCustomerId=@customer AND Status=N'PENDING';IF @referral IS NULL RETURN;
 SELECT @minimum=MinimumQualifyingAmount FROM loyalty.ReferralConfigurations WHERE TenantId=@TenantId AND IsEnabled=1;IF @minimum IS NULL RETURN;
 IF @event IN(N'CANCELLED',N'VOID',N'RETURNED',N'REFUNDED') RETURN;
 IF @rule=N'FIRST_ORDER_PLACED' SET @qualifies=1;
 IF @rule=N'FIRST_PAID_ORDER' AND (@paid>=@total OR @event IN(N'PAID',N'COMPLETED',N'DELIVERED')) SET @qualifies=1;
 IF @rule IN(N'FIRST_COMPLETED_ORDER',N'FIRST_COMPLETED_ORDER_MIN_AMOUNT') AND @event IN(N'COMPLETED',N'DELIVERED') AND (@rule<>N'FIRST_COMPLETED_ORDER_MIN_AMOUNT' OR @total>=@minimum) SET @qualifies=1;
 IF @qualifies=1 BEGIN UPDATE loyalty.CustomerReferrals SET Status=N'QUALIFIED',QualifyingOrderId=@OrderId,QualifiedAt=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@CreatedBy WHERE TenantId=@TenantId AND CustomerReferralId=@referral AND Status=N'PENDING';IF @@ROWCOUNT=1 BEGIN INSERT loyalty.ReferralAuditEvents(TenantId,CustomerReferralId,EventType,Actor,SystemSource) VALUES(@TenantId,@referral,N'QUALIFIED',@CreatedBy,N'ORDER_LIFECYCLE');EXEC loyalty.AwardCustomerReferral @TenantId,@referral,@CreatedBy;END;END;
END
GO

CREATE OR ALTER PROCEDURE loyalty.ApproveCustomerReferral @TenantId uniqueidentifier,@ReferralId uniqueidentifier,@CreatedBy nvarchar(256) AS
BEGIN SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;UPDATE loyalty.CustomerReferrals SET Status=N'QUALIFIED',QualifiedAt=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@CreatedBy WHERE TenantId=@TenantId AND CustomerReferralId=@ReferralId AND Status=N'PENDING' AND QualificationType=N'MANUAL_APPROVAL';IF @@ROWCOUNT<>1 THROW 51225,N'Only a pending manual-approval referral can be approved.',1;INSERT loyalty.ReferralAuditEvents(TenantId,CustomerReferralId,EventType,Actor,SystemSource) VALUES(@TenantId,@ReferralId,N'QUALIFIED',@CreatedBy,N'MANUAL_APPROVAL');COMMIT;EXEC loyalty.AwardCustomerReferral @TenantId,@ReferralId,@CreatedBy;END
GO

CREATE OR ALTER PROCEDURE loyalty.ReverseReferralOrder @TenantId uniqueidentifier,@OrderId uniqueidentifier,@Reason nvarchar(500),@CreatedBy nvarchar(256)=NULL AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;DECLARE @id uniqueidentifier;SELECT @id=r.CustomerReferralId FROM loyalty.CustomerReferrals r JOIN loyalty.ReferralConfigurations c ON c.TenantId=r.TenantId AND c.ReverseOnRefund=1 WHERE r.TenantId=@TenantId AND r.QualifyingOrderId=@OrderId AND r.Status=N'REWARDED';IF @id IS NULL BEGIN COMMIT;RETURN;END;
 IF EXISTS(SELECT 1 FROM (SELECT CustomerId,SUM(Coins) Reward FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND SourceType=N'REFERRAL' AND SourceId=@id AND Coins>0 GROUP BY CustomerId)x CROSS APPLY(SELECT SUM(Coins) Balance FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND CustomerId=x.CustomerId)b WHERE b.Balance<x.Reward) THROW 51226,N'Referral rewards cannot be reversed after the coins have been spent.',1;
 INSERT loyalty.CoinLedger(TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,ReversesTransactionId,SystemSource,CreatedBy)
 SELECT TenantId,CustomerId,N'REVERSAL',-Coins,N'REFERRAL',@id,CONCAT(EventKey,N':REVERSAL'),@Reason,CoinTransactionId,N'REFERRAL_ENGINE',@CreatedBy FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND SourceType=N'REFERRAL' AND SourceId=@id AND Coins>0;
 UPDATE lot SET RemainingCoins=0 FROM loyalty.CustomerRewardLots lot JOIN loyalty.CoinLedger l ON l.CoinTransactionId=lot.CreditTransactionId WHERE l.TenantId=@TenantId AND l.SourceId=@id;
 UPDATE loyalty.CustomerReferrals SET Status=N'REVERSED',ReversedAt=SYSUTCDATETIME(),RejectionReason=@Reason,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@CreatedBy WHERE TenantId=@TenantId AND CustomerReferralId=@id;
 INSERT loyalty.ReferralAuditEvents(TenantId,CustomerReferralId,EventType,Reason,Actor,SystemSource) VALUES(@TenantId,@id,N'REVERSED',@Reason,@CreatedBy,N'ORDER_LIFECYCLE');COMMIT;
END
GO

CREATE OR ALTER PROCEDURE loyalty.AdjustCustomerReward @TenantId uniqueidentifier,@CustomerId uniqueidentifier,@Coins int,@Reason nvarchar(500),@CreatedBy nvarchar(256) AS
BEGIN SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;IF @Coins=0 OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 51231,N'A non-zero amount and reason are required.',1;IF NOT EXISTS(SELECT 1 FROM sales.Customers WHERE TenantId=@TenantId AND CustomerId=@CustomerId AND IsDeleted=0) THROW 51232,N'Customer was not found.',1;IF @Coins<0 AND ISNULL((SELECT SUM(Coins) FROM loyalty.CoinLedger WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND CustomerId=@CustomerId),0)<-@Coins THROW 51233,N'Adjustment cannot make the balance negative.',1;DECLARE @id uniqueidentifier=NEWID();INSERT loyalty.CoinLedger(CoinTransactionId,TenantId,CustomerId,TransactionType,Coins,SourceType,EventKey,Description,SystemSource,CreatedBy) VALUES(@id,@TenantId,@CustomerId,N'ADJUSTMENT',@Coins,N'ADMIN',CONCAT(N'ADJUSTMENT:',@id),@Reason,N'ADMIN',@CreatedBy);IF @Coins>0 INSERT loyalty.CustomerRewardLots(CustomerRewardLotId,TenantId,CustomerId,CreditTransactionId,OriginalCoins,RemainingCoins) VALUES(NEWID(),@TenantId,@CustomerId,@id,@Coins,@Coins);INSERT loyalty.ReferralAuditEvents(TenantId,EventType,Reason,Actor,SystemSource) VALUES(@TenantId,N'WALLET_ADJUSTED',@Reason,@CreatedBy,N'ADMIN');COMMIT;END
GO

CREATE OR ALTER PROCEDURE loyalty.ExpireCustomerRewards @TenantId uniqueidentifier=NULL,@BatchSize int=1000,@CreatedBy nvarchar(256)=N'SYSTEM' AS
BEGIN SET NOCOUNT ON;SET XACT_ABORT ON;DECLARE @count int=0;DECLARE lots CURSOR LOCAL FAST_FORWARD FOR SELECT TOP(@BatchSize) CustomerRewardLotId,TenantId,CustomerId,RemainingCoins FROM loyalty.CustomerRewardLots WHERE RemainingCoins>0 AND ExpiresAt<=SYSUTCDATETIME() AND(@TenantId IS NULL OR TenantId=@TenantId) ORDER BY ExpiresAt,CreatedOn;DECLARE @lot uniqueidentifier,@tenant uniqueidentifier,@customer uniqueidentifier,@coins int;OPEN lots;FETCH NEXT FROM lots INTO @lot,@tenant,@customer,@coins;WHILE @@FETCH_STATUS=0 BEGIN BEGIN TRAN;UPDATE loyalty.CustomerRewardLots SET RemainingCoins=0 WHERE CustomerRewardLotId=@lot AND RemainingCoins=@coins;IF @@ROWCOUNT=1 BEGIN INSERT loyalty.CoinLedger(TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,SystemSource,CreatedBy) VALUES(@tenant,@customer,N'EXPIRY',-@coins,N'EXPIRY',@lot,CONCAT(N'LOT:',@lot,N':EXPIRY'),N'Reward coins expired',N'EXPIRATION_PROCESSOR',@CreatedBy);INSERT loyalty.ReferralAuditEvents(TenantId,EventType,Reason,Actor,SystemSource) VALUES(@tenant,N'COINS_EXPIRED',CONCAT(@coins,N' coins'),@CreatedBy,N'EXPIRATION_PROCESSOR');SET @count+=1;END;COMMIT;FETCH NEXT FROM lots INTO @lot,@tenant,@customer,@coins;END;CLOSE lots;DEALLOCATE lots;SELECT @count;END
GO

-- Hierarchical feature seed. Plan inclusion is explicit and tenant state remains independently controllable.
DECLARE @feature uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'CUSTOMER_REFERRAL_REWARDS'),@parent uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'CUSTOMERS');
IF @feature IS NULL BEGIN SET @feature=NEWID();INSERT core.Features(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,ParentFeatureId,Version,SortOrder,CreatedBy) VALUES(@feature,N'CUSTOMER_REFERRAL_REWARDS',N'Customer Referral Rewards',N'Tenant-scoped customer referral attribution and reward coins.',N'COMMERCE',N'ACTIVE',1,N'MODULE',@parent,N'V2',75,N'V15 referral rewards');END;
ELSE UPDATE core.Features SET ParentFeatureId=@parent,Version=N'V2',SortOrder=75 WHERE FeatureId=@feature;
MERGE core.PlanFeatures target USING
(SELECT p.PlanId,@feature FeatureId,CONVERT(bit,CASE WHEN p.PlanKey=N'V2_COMMERCE' THEN 1 ELSE 0 END) IsEnabled FROM core.Plans p WHERE p.PlanKey IN(N'V1_DEFAULT',N'V2_COMMERCE')) source
ON target.PlanId=source.PlanId AND target.FeatureId=source.FeatureId
WHEN MATCHED THEN UPDATE SET IsEnabled=source.IsEnabled,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'V15 referral rewards'
WHEN NOT MATCHED THEN INSERT(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy) VALUES(NEWID(),source.PlanId,source.FeatureId,source.IsEnabled,N'V15 referral rewards');
INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy) SELECT NEWID(),t.TenantId,@feature,0,N'Opt-in customer referral program',1,N'V15 referral rewards' FROM core.Tenants t WHERE NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=t.TenantId AND tf.FeatureId=@feature);
GO
