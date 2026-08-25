/* V16 - configurable expiry for purchase-earned coins and complete FIFO lot accounting. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'loyalty.CoinConfigurations',N'PurchaseCoinValidityDays') IS NULL
    ALTER TABLE loyalty.CoinConfigurations ADD PurchaseCoinValidityDays int NOT NULL
        CONSTRAINT DF_CoinConfigurations_PurchaseValidity DEFAULT 365 WITH VALUES;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'loyalty.CoinConfigurations') AND name=N'CK_CoinConfigurations_PurchaseValidity')
    ALTER TABLE loyalty.CoinConfigurations WITH CHECK ADD CONSTRAINT CK_CoinConfigurations_PurchaseValidity CHECK(PurchaseCoinValidityDays BETWEEN 1 AND 3650);
GO

/* Grandfather existing positive ledger balances as non-expiring lots. New purchase awards use configured expiry. */
;WITH ExistingLots AS
(
    SELECT TenantId,CustomerId,SUM(RemainingCoins) RemainingCoins
    FROM loyalty.CustomerRewardLots GROUP BY TenantId,CustomerId
),
Wallets AS
(
    SELECT l.TenantId,l.CustomerId,CASE WHEN SUM(l.Coins)>ISNULL(MAX(e.RemainingCoins),0) THEN SUM(l.Coins)-ISNULL(MAX(e.RemainingCoins),0) ELSE 0 END UntrackedBalance
    FROM loyalty.CoinLedger l LEFT JOIN ExistingLots e ON e.TenantId=l.TenantId AND e.CustomerId=l.CustomerId
    GROUP BY l.TenantId,l.CustomerId
),
Credits AS
(
    SELECT l.CoinTransactionId,l.TenantId,l.CustomerId,l.Coins,w.UntrackedBalance,
           ISNULL(SUM(l.Coins) OVER(PARTITION BY l.TenantId,l.CustomerId ORDER BY l.CreatedOn DESC,l.CoinTransactionId DESC ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0) NewerCredits
    FROM loyalty.CoinLedger l
    JOIN Wallets w ON w.TenantId=l.TenantId AND w.CustomerId=l.CustomerId
    LEFT JOIN loyalty.CustomerRewardLots lot ON lot.CreditTransactionId=l.CoinTransactionId
    WHERE l.Coins>0 AND lot.CustomerRewardLotId IS NULL
)
INSERT loyalty.CustomerRewardLots(CustomerRewardLotId,TenantId,CustomerId,CreditTransactionId,OriginalCoins,RemainingCoins,ExpiresAt)
SELECT NEWID(),TenantId,CustomerId,CoinTransactionId,Coins,
       CASE WHEN UntrackedBalance<=NewerCredits THEN 0 WHEN UntrackedBalance>=NewerCredits+Coins THEN Coins ELSE CONVERT(int,UntrackedBalance-NewerCredits) END,NULL
FROM Credits;
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
 DECLARE @remaining int=@Coins,@lot uniqueidentifier,@available int,@use int;DECLARE fifo CURSOR LOCAL FAST_FORWARD FOR SELECT CustomerRewardLotId,RemainingCoins FROM loyalty.CustomerRewardLots WITH(UPDLOCK) WHERE TenantId=@TenantId AND CustomerId=@CustomerId AND RemainingCoins>0 AND(ExpiresAt IS NULL OR ExpiresAt>SYSUTCDATETIME()) ORDER BY CASE WHEN ExpiresAt IS NULL THEN 1 ELSE 0 END,ExpiresAt,CreatedOn,CustomerRewardLotId;OPEN fifo;FETCH NEXT FROM fifo INTO @lot,@available;WHILE @@FETCH_STATUS=0 AND @remaining>0 BEGIN SET @use=CASE WHEN @available>@remaining THEN @remaining ELSE @available END;UPDATE loyalty.CustomerRewardLots SET RemainingCoins=RemainingCoins-@use WHERE CustomerRewardLotId=@lot;INSERT loyalty.CustomerRewardConsumptions(CustomerRewardConsumptionId,DebitTransactionId,CustomerRewardLotId,Coins)VALUES(NEWID(),@debit,@lot,@use);SET @remaining-=@use;FETCH NEXT FROM fifo INTO @lot,@available;END;CLOSE fifo;DEALLOCATE fifo;IF @remaining>0 THROW 51138,N'Available coin lots are insufficient; run coin expiration and retry.',1;
 INSERT loyalty.OrderCoins(OrderId,TenantId,CustomerId,RedeemedCoins,RedemptionDiscount,RedeemedOn) VALUES(@OrderId,@TenantId,@CustomerId,@Coins,@discount,SYSUTCDATETIME());COMMIT;
END
GO

CREATE OR ALTER PROCEDURE loyalty.ProcessOrder
 @TenantId uniqueidentifier,@OrderId uniqueidentifier,@EventStatus nvarchar(20),@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 DECLARE @customer uniqueidentifier,@enabled bit,@awardStatus nvarchar(20),@priority nvarchar(30),@amount decimal(18,2),@purchaseCoins int,@validity int,@earned int=0,@redemption int,@restoreCancel bit,@restoreRefund bit,@event nvarchar(20)=UPPER(@EventStatus);
 SELECT @customer=i.CustomerId FROM sales.SalesInvoices i JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=@TenantId WHERE i.InvoiceId=@OrderId;IF @customer IS NULL RETURN;
 IF @event=N'CURRENT' SELECT @event=i.Status FROM sales.SalesInvoices i JOIN sales.Customers c ON c.CustomerId=i.CustomerId AND c.TenantId=@TenantId WHERE i.InvoiceId=@OrderId;
 SELECT @enabled=IsEnabled,@awardStatus=AwardOrderStatus,@priority=EarningPriority,@amount=PurchaseAmount,@purchaseCoins=PurchaseCoins,@validity=PurchaseCoinValidityDays,@restoreCancel=RestoreRedeemedOnCancel,@restoreRefund=RestoreRedeemedOnRefund FROM loyalty.CoinConfigurations WHERE TenantId=@TenantId;
 IF ISNULL(@enabled,0)=0 RETURN;
 IF (@event=@awardStatus OR(@awardStatus=N'COMPLETED' AND @event=N'DELIVERED')) AND NOT EXISTS(SELECT 1 FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':EARN'))
 BEGIN
   ;WITH L AS(SELECT i.Quantity,i.LineTotal,p.CategoryId,pr.ProductId AS HasProduct,pr.IsEnabled AS ProductEnabled,pr.CoinsPerUnit AS ProductCoins,cr.ProductCategoryId AS HasCategory,cr.IsEnabled AS CategoryEnabled,cr.CoinsPerUnit AS CategoryCoins FROM sales.SalesInvoiceItems i JOIN master.Products p ON p.ProductId=i.ProductId LEFT JOIN loyalty.ProductCoinRules pr ON pr.TenantId=@TenantId AND pr.ProductId=i.ProductId LEFT JOIN loyalty.CategoryCoinRules cr ON cr.TenantId=@TenantId AND cr.ProductCategoryId=p.CategoryId WHERE i.InvoiceId=@OrderId)
   SELECT @earned=CONVERT(int,ISNULL(SUM(CASE WHEN HasProduct IS NOT NULL AND ProductEnabled=0 THEN 0 WHEN @priority=N'PRODUCT_FIRST' AND HasProduct IS NOT NULL THEN FLOOR(Quantity*ProductCoins) WHEN @priority=N'PRODUCT_FIRST' AND HasCategory IS NOT NULL AND CategoryEnabled=1 THEN FLOOR(Quantity*CategoryCoins) ELSE 0 END),0)+FLOOR(ISNULL(SUM(CASE WHEN HasProduct IS NOT NULL AND ProductEnabled=0 THEN 0 WHEN @priority=N'PRODUCT_FIRST' AND(HasProduct IS NOT NULL OR HasCategory IS NOT NULL) THEN 0 ELSE LineTotal END),0)/@amount)*@purchaseCoins) FROM L;
   IF @earned>0 BEGIN DECLARE @earnId uniqueidentifier=NEWID(),@expiry datetimeoffset=DATEADD(day,@validity,SYSUTCDATETIME());INSERT loyalty.CoinLedger(CoinTransactionId,TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,ExpiresAt,SystemSource,CreatedBy) VALUES(@earnId,@TenantId,@customer,N'EARN',@earned,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':EARN'),N'Coins earned from purchase',@expiry,N'PURCHASE_LOYALTY',@CreatedBy);INSERT loyalty.CustomerRewardLots(CustomerRewardLotId,TenantId,CustomerId,CreditTransactionId,OriginalCoins,RemainingCoins,ExpiresAt) VALUES(NEWID(),@TenantId,@customer,@earnId,@earned,@earned,@expiry);IF EXISTS(SELECT 1 FROM loyalty.OrderCoins WHERE OrderId=@OrderId) UPDATE loyalty.OrderCoins SET EarnedCoins=@earned,EarnedOn=SYSUTCDATETIME() WHERE OrderId=@OrderId;ELSE INSERT loyalty.OrderCoins(OrderId,TenantId,CustomerId,EarnedCoins,EarnedOn) VALUES(@OrderId,@TenantId,@customer,@earned,SYSUTCDATETIME());END;
 END;
 IF @event IN(N'CANCELLED',N'VOID',N'RETURNED',N'REFUNDED')
 BEGIN
   DECLARE @earnedId uniqueidentifier,@earnCoins int;SELECT @earnedId=CoinTransactionId,@earnCoins=Coins FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':EARN');
   IF @earnedId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':REVERSE_EARN')) BEGIN INSERT loyalty.CoinLedger(TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,ReversesTransactionId,SystemSource,CreatedBy) VALUES(@TenantId,@customer,N'REVERSE_EARN',-@earnCoins,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':REVERSE_EARN'),N'Purchase earning reversed',@earnedId,N'PURCHASE_LOYALTY',@CreatedBy);UPDATE loyalty.CustomerRewardLots SET RemainingCoins=0 WHERE CreditTransactionId=@earnedId;UPDATE loyalty.OrderCoins SET EarnReversedOn=SYSUTCDATETIME() WHERE OrderId=@OrderId;END;
   DECLARE @redeemId uniqueidentifier;SELECT @redeemId=CoinTransactionId,@redemption=-Coins FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':REDEEM');
   IF @redemption>0 AND((@event IN(N'CANCELLED',N'VOID') AND @restoreCancel=1)OR(@event IN(N'RETURNED',N'REFUNDED') AND @restoreRefund=1))AND NOT EXISTS(SELECT 1 FROM loyalty.CoinLedger WHERE TenantId=@TenantId AND EventKey=CONCAT(N'ORDER:',@OrderId,N':RESTORE_REDEEM'))
   BEGIN DECLARE @restoreId uniqueidentifier=NEWID(),@restoredFromLots int;INSERT loyalty.CoinLedger(CoinTransactionId,TenantId,CustomerId,TransactionType,Coins,SourceType,SourceId,EventKey,Description,SystemSource,CreatedBy) VALUES(@restoreId,@TenantId,@customer,N'RESTORE_REDEEM',@redemption,N'ORDER',@OrderId,CONCAT(N'ORDER:',@OrderId,N':RESTORE_REDEEM'),N'Redeemed coins restored',N'PURCHASE_LOYALTY',@CreatedBy);UPDATE lot SET RemainingCoins=lot.RemainingCoins+c.Coins FROM loyalty.CustomerRewardLots lot JOIN loyalty.CustomerRewardConsumptions c ON c.CustomerRewardLotId=lot.CustomerRewardLotId WHERE c.DebitTransactionId=@redeemId;SELECT @restoredFromLots=ISNULL(SUM(Coins),0) FROM loyalty.CustomerRewardConsumptions WHERE DebitTransactionId=@redeemId;IF @restoredFromLots<@redemption INSERT loyalty.CustomerRewardLots(CustomerRewardLotId,TenantId,CustomerId,CreditTransactionId,OriginalCoins,RemainingCoins) VALUES(NEWID(),@TenantId,@customer,@restoreId,@redemption-@restoredFromLots,@redemption-@restoredFromLots);UPDATE loyalty.OrderCoins SET RedemptionRestoredOn=SYSUTCDATETIME() WHERE OrderId=@OrderId;END;
 END;
END
GO

CREATE OR ALTER PROCEDURE loyalty.AdjustCustomerReward @TenantId uniqueidentifier,@CustomerId uniqueidentifier,@Coins int,@Reason nvarchar(500),@CreatedBy nvarchar(256) AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;IF @Coins=0 OR NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 51231,N'A non-zero amount and reason are required.',1;IF NOT EXISTS(SELECT 1 FROM sales.Customers WHERE TenantId=@TenantId AND CustomerId=@CustomerId AND IsDeleted=0) THROW 51232,N'Customer was not found.',1;IF @Coins<0 AND ISNULL((SELECT SUM(Coins) FROM loyalty.CoinLedger WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND CustomerId=@CustomerId),0)<-@Coins THROW 51233,N'Adjustment cannot make the balance negative.',1;
 DECLARE @id uniqueidentifier=NEWID();INSERT loyalty.CoinLedger(CoinTransactionId,TenantId,CustomerId,TransactionType,Coins,SourceType,EventKey,Description,SystemSource,CreatedBy) VALUES(@id,@TenantId,@CustomerId,N'ADJUSTMENT',@Coins,N'ADMIN',CONCAT(N'ADJUSTMENT:',@id),@Reason,N'ADMIN',@CreatedBy);
 IF @Coins>0 INSERT loyalty.CustomerRewardLots(CustomerRewardLotId,TenantId,CustomerId,CreditTransactionId,OriginalCoins,RemainingCoins) VALUES(NEWID(),@TenantId,@CustomerId,@id,@Coins,@Coins);
 IF @Coins<0 BEGIN DECLARE @remaining int=-@Coins,@lot uniqueidentifier,@available int,@use int;DECLARE fifo CURSOR LOCAL FAST_FORWARD FOR SELECT CustomerRewardLotId,RemainingCoins FROM loyalty.CustomerRewardLots WITH(UPDLOCK) WHERE TenantId=@TenantId AND CustomerId=@CustomerId AND RemainingCoins>0 AND(ExpiresAt IS NULL OR ExpiresAt>SYSUTCDATETIME()) ORDER BY CASE WHEN ExpiresAt IS NULL THEN 1 ELSE 0 END,ExpiresAt,CreatedOn,CustomerRewardLotId;OPEN fifo;FETCH NEXT FROM fifo INTO @lot,@available;WHILE @@FETCH_STATUS=0 AND @remaining>0 BEGIN SET @use=CASE WHEN @available>@remaining THEN @remaining ELSE @available END;UPDATE loyalty.CustomerRewardLots SET RemainingCoins=RemainingCoins-@use WHERE CustomerRewardLotId=@lot;INSERT loyalty.CustomerRewardConsumptions(CustomerRewardConsumptionId,DebitTransactionId,CustomerRewardLotId,Coins)VALUES(NEWID(),@id,@lot,@use);SET @remaining-=@use;FETCH NEXT FROM fifo INTO @lot,@available;END;CLOSE fifo;DEALLOCATE fifo;IF @remaining>0 THROW 51234,N'Available coin lots are insufficient; run coin expiration and retry.',1;END;
 INSERT loyalty.ReferralAuditEvents(TenantId,EventType,Reason,Actor,SystemSource) VALUES(@TenantId,N'WALLET_ADJUSTED',@Reason,@CreatedBy,N'ADMIN');COMMIT;
END
GO
