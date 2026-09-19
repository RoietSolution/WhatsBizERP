CREATE PROCEDURE finance.PostStockAdjustment
 @TenantId uniqueidentifier=NULL,@TransactionId uniqueidentifier,@CreatedBy nvarchar(256)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @SessionTenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 /* Resolve legacy internal calls through the tenant-owned inventory transaction. */
 IF @TenantId IS NULL SELECT @TenantId=TenantId FROM inventory.InventoryTransactions WHERE TransactionId=@TransactionId;
 IF @TenantId IS NULL OR @SessionTenant IS NULL OR @TenantId<>@SessionTenant THROW 51500,N'Tenant context is missing or mismatched.',1;
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=N'STOCK_ADJUSTMENT' AND ReferenceId=@TransactionId AND TenantId<>@TenantId) THROW 51502,N'Finance source is owned by another tenant.',1;
 IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=N'STOCK_ADJUSTMENT' AND ReferenceId=@TransactionId AND TenantId=@TenantId) RETURN;
 DECLARE @J uniqueidentifier=NEWID(),@No nvarchar(50)=CONCAT(N'JV-',UPPER(LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20))),@Date datetimeoffset,@Type nvarchar(10),@Ref nvarchar(50),@Amount decimal(18,2),@Narr nvarchar(500),@Inv uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'INVENTORY'),@Adj uniqueidentifier=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'STOCK_ADJUST');
 SELECT @Date=t.TransactionDate,@Type=a.AdjustmentType,@Ref=a.AdjustmentNo,@Narr=t.Remarks,@Amount=SUM(d.Quantity*d.UnitCost) FROM inventory.StockAdjustments a JOIN inventory.InventoryTransactions t ON t.TransactionId=a.TransactionId AND t.TenantId=@TenantId JOIN inventory.Warehouses w ON w.WarehouseId=t.WarehouseId AND w.TenantId=@TenantId JOIN inventory.InventoryTransactionDetails d ON d.TransactionId=t.TransactionId JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId WHERE a.TransactionId=@TransactionId GROUP BY t.TransactionDate,a.AdjustmentType,a.AdjustmentNo,t.Remarks;
 IF @Amount IS NULL THROW 51515,N'Stock adjustment source was not found for this tenant.',1; IF @Amount<=0 THROW 51516,N'Stock adjustment journal total must be positive.',1; IF @Inv IS NULL OR @Adj IS NULL THROW 51503,N'Finance account configuration is incomplete.',1;
 INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,ReferenceType,ReferenceId,Narration,CreatedBy,TenantId) VALUES(@J,@No,@Date,N'STOCK_ADJUSTMENT',N'STOCK_ADJUSTMENT',@TransactionId,@Narr,@CreatedBy,@TenantId);
 IF @Type=N'INCREASE' INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@Inv,@Amount,0),(@J,@Adj,0,@Amount); ELSE INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,@Adj,@Amount,0),(@J,@Inv,0,@Amount);
 INSERT finance.LedgerEntries(JournalEntryId,AccountId,EntryDate,ReferenceType,ReferenceId,DebitAmount,CreditAmount,Narration) SELECT @J,AccountId,@Date,N'STOCK_ADJUSTMENT',@TransactionId,DebitAmount,CreditAmount,@Narr FROM finance.JournalEntryDetails WHERE JournalEntryId=@J;
 INSERT finance.DayBook(JournalEntryId,EntryDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,DebitTotal,CreditTotal,Narration) VALUES(@J,@Date,N'STOCK_ADJUSTMENT',N'STOCK_ADJUSTMENT',@TransactionId,@Ref,@Amount,@Amount,@Narr);
END;
GO

