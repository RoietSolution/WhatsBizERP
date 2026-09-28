CREATE PROCEDURE [sales].[POS_ReverseFullSale]
    @TenantId uniqueidentifier,
    @InvoiceId uniqueidentifier,
    @Reason nvarchar(250),
    @CreatedBy nvarchar(256)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @TenantId IS NULL OR @InvoiceId IS NULL OR
       TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')) IS NULL OR
       TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'))<>@TenantId
        THROW 51900,N'Trusted tenant context is required.',1;
    SET @Reason=LTRIM(RTRIM(@Reason));
    IF @Reason IS NULL OR LEN(@Reason)<3 OR LEN(@Reason)>250
        THROW 51901,N'A reversal reason of 3 to 250 characters is required.',1;

    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Status nvarchar(20),@CustomerId uniqueidentifier,@WarehouseId uniqueidentifier,
            @InvoiceNumber nvarchar(50),@Grand decimal(18,2),@Paid decimal(18,2),
            @Tax decimal(18,2),@Delivery decimal(18,2),@Net decimal(18,2),
            @Collected decimal(18,2),@SaleJournalId uniqueidentifier,@SaleStockId uniqueidentifier,
            @ReversalId uniqueidentifier,@JournalId uniqueidentifier,@RestoreId uniqueidentifier,
            @Now datetimeoffset=SYSUTCDATETIME(),@DeliveryId uniqueidentifier,@DeliveryStatus nvarchar(30);

        SELECT @Status=i.Status,@CustomerId=i.CustomerId,@WarehouseId=i.WarehouseId,
            @InvoiceNumber=i.InvoiceNumber,@Grand=i.GrandTotal,@Paid=i.PaidAmount,
            @Tax=i.TaxAmount,@Delivery=i.DeliveryCharge
        FROM sales.SalesInvoices i WITH(UPDLOCK,HOLDLOCK)
        WHERE i.InvoiceId=@InvoiceId AND i.TenantId=@TenantId;
        IF @Status IS NULL THROW 51902,N'Sale not found for this tenant.',1;

        -- The unique invoice claim is both the durable reversal link and replay guard.
        IF EXISTS(SELECT 1 FROM sales.FullSaleReversals WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND InvoiceId=@InvoiceId)
        BEGIN
            IF @Status<>N'CANCELLED' THROW 51903,N'Reversed sale has inconsistent status.',1;
            SELECT @ReversalId=ReversalId,@JournalId=ReversalJournalId,
                @RestoreId=InventoryRestorationId,@Collected=CollectedAmount,
                @Grand=OriginalGrandTotal
            FROM sales.FullSaleReversals WHERE TenantId=@TenantId AND InvoiceId=@InvoiceId;
            COMMIT TRANSACTION;
            SELECT @InvoiceId InvoiceId,@ReversalId ReversalId,@JournalId ReversalJournalId,
                @RestoreId InventoryRestorationId,@Grand OriginalGrandTotal,
                @Collected CollectedAmount,@Collected RefundRequiredAmount,
                CAST(1 AS bit) AlreadyReversed;
            RETURN;
        END;

        IF @Status<>N'COMPLETED' THROW 51904,N'Only an unreversed completed sale is eligible.',1;
        IF NOT EXISTS(SELECT 1 FROM inventory.Warehouses WITH(HOLDLOCK)
                      WHERE WarehouseId=@WarehouseId AND TenantId=@TenantId)
            THROW 51905,N'Sale warehouse tenant ownership is invalid.',1;
        IF @CustomerId IS NOT NULL AND NOT EXISTS
            (SELECT 1 FROM sales.Customers WHERE CustomerId=@CustomerId AND TenantId=@TenantId)
            THROW 51906,N'Sale customer tenant ownership is invalid.',1;
        IF EXISTS(SELECT 1 FROM sales.SalesInvoiceReturns WITH(UPDLOCK,HOLDLOCK)
                  WHERE InvoiceId=@InvoiceId)
           OR EXISTS(SELECT 1 FROM sales.SalesInvoiceItems WHERE InvoiceId=@InvoiceId AND ReturnedQuantity>0)
            THROW 51907,N'Sale with return activity cannot be fully reversed.',1;
        IF EXISTS(SELECT 1 FROM commerce.StorefrontRefunds WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND InvoiceId=@InvoiceId)
            THROW 51908,N'Sale has existing refund activity.',1;

        SELECT @DeliveryId=OrderDeliveryId,@DeliveryStatus=DeliveryStatus
        FROM commerce.OrderDeliveries WITH(UPDLOCK,HOLDLOCK)
        WHERE OrderId=@InvoiceId AND TenantId=@TenantId;
        IF EXISTS(SELECT 1 FROM commerce.OrderDeliveries WHERE OrderId=@InvoiceId AND TenantId<>@TenantId)
            THROW 51909,N'Delivery tenant ownership is invalid.',1;
        IF @DeliveryId IS NOT NULL AND @DeliveryStatus NOT IN
            (N'UNASSIGNED',N'ASSIGNED',N'READY_FOR_PICKUP')
            THROW 51910,N'Delivery has progressed beyond safe pre-delivery reversal.',1;

        SELECT @Collected=ISNULL(SUM(Amount),0)
        FROM sales.SalesPayments WITH(UPDLOCK,HOLDLOCK)
        WHERE InvoiceId=@InvoiceId AND Status=N'COMPLETED';
        IF @Grand<=0 OR @Paid<0 OR @Collected<>@Paid OR @Collected>@Grand
            THROW 51911,N'Committed invoice and collection amounts are inconsistent.',1;
        IF @Collected>0 AND @CustomerId IS NULL
            THROW 51912,N'A collected sale requires an owned customer for refund liability.',1;

        SELECT @SaleJournalId=JournalEntryId FROM finance.JournalEntries WITH(UPDLOCK,HOLDLOCK)
        WHERE TenantId=@TenantId AND ReferenceType=N'SALE' AND ReferenceId=@InvoiceId
          AND TransactionType=N'SALE';
        IF @SaleJournalId IS NULL THROW 51913,N'Original sale journal is missing.',1;
        IF EXISTS(SELECT 1 FROM finance.JournalEntries WHERE ReferenceType=N'SALE'
                  AND ReferenceId=@InvoiceId AND TenantId<>@TenantId)
            THROW 51914,N'Original sale journal tenant ownership is invalid.',1;

        -- A SALE journal can also contain an inline POS receipt. Reverse only the
        -- four committed sale-side lines, never its Cash/Bank or receipt lines.
        SET @Net=@Grand-@Tax-@Delivery;
        IF @Tax<0 OR @Delivery<0 OR @Net<0 THROW 51915,N'Committed sale components are invalid.',1;
        DECLARE @BusinessLines table(AccountId uniqueidentifier NOT NULL,
            DebitAmount decimal(18,2) NOT NULL,CreditAmount decimal(18,2) NOT NULL,
            Description nvarchar(250) NULL);
        INSERT @BusinessLines(AccountId,DebitAmount,CreditAmount,Description)
        SELECT d.AccountId,d.DebitAmount,d.CreditAmount,d.Description
        FROM finance.JournalEntryDetails d
        WHERE d.JournalEntryId=@SaleJournalId AND d.Description IN
            (N'Customer receivable',N'Merchandise sales',N'Output GST',
             N'Customer delivery charge pending accounting classification');
        IF (SELECT COUNT(*) FROM @BusinessLines WHERE Description=N'Customer receivable'
            AND AccountId=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'CUSTOMER')
            AND DebitAmount=@Grand AND CreditAmount=0)<>1
          OR (SELECT COUNT(*) FROM @BusinessLines WHERE Description=N'Merchandise sales'
            AND AccountId=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'SALES')
            AND DebitAmount=0 AND CreditAmount=@Net)<>CASE WHEN @Net>0 THEN 1 ELSE 0 END
          OR (SELECT COUNT(*) FROM @BusinessLines WHERE Description=N'Output GST'
            AND AccountId=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'OUTPUT_GST')
            AND DebitAmount=0 AND CreditAmount=@Tax)<>CASE WHEN @Tax>0 THEN 1 ELSE 0 END
          OR (SELECT COUNT(*) FROM @BusinessLines WHERE Description=N'Customer delivery charge pending accounting classification'
            AND AccountId=(SELECT AccountId FROM finance.Accounts WHERE AccountCode=N'DELIVERY_CLEARING')
            AND DebitAmount=0 AND CreditAmount=@Delivery)<>CASE WHEN @Delivery>0 THEN 1 ELSE 0 END
          OR (SELECT COUNT(*) FROM @BusinessLines)<>1+CASE WHEN @Net>0 THEN 1 ELSE 0 END+
            CASE WHEN @Tax>0 THEN 1 ELSE 0 END+CASE WHEN @Delivery>0 THEN 1 ELSE 0 END
          OR (SELECT SUM(DebitAmount) FROM @BusinessLines)<>@Grand
          OR (SELECT SUM(CreditAmount) FROM @BusinessLines)<>@Grand
            THROW 51916,N'Original sale journal does not match committed sale components.',1;

        SELECT @SaleStockId=TransactionId FROM inventory.InventoryTransactions WITH(UPDLOCK,HOLDLOCK)
        WHERE ReferenceType=N'SALES_INVOICE' AND ReferenceId=@InvoiceId AND TransactionType=N'SALE'
          AND WarehouseId=@WarehouseId AND (TenantId=@TenantId OR TenantId IS NULL);
        IF @SaleStockId IS NULL OR
           (SELECT COUNT(*) FROM inventory.InventoryTransactions WHERE ReferenceType=N'SALES_INVOICE'
            AND ReferenceId=@InvoiceId AND TransactionType=N'SALE')<>1
            THROW 51917,N'Exactly one owned original sale stock transaction is required.',1;
        IF NOT EXISTS(SELECT 1 FROM inventory.InventoryTransactionDetails WHERE TransactionId=@SaleStockId)
            THROW 51918,N'Original sale stock details are missing.',1;
        IF EXISTS(
            SELECT ProductId,SUM(Quantity) Quantity,COUNT(*) LineCount
            FROM inventory.InventoryTransactionDetails WHERE TransactionId=@SaleStockId GROUP BY ProductId
            EXCEPT SELECT ProductId,SUM(Quantity),COUNT(*)
            FROM sales.SalesInvoiceItems WHERE InvoiceId=@InvoiceId GROUP BY ProductId)
           OR EXISTS(
            SELECT ProductId,SUM(Quantity) Quantity,COUNT(*) LineCount
            FROM sales.SalesInvoiceItems WHERE InvoiceId=@InvoiceId GROUP BY ProductId
            EXCEPT SELECT ProductId,SUM(Quantity),COUNT(*)
            FROM inventory.InventoryTransactionDetails WHERE TransactionId=@SaleStockId GROUP BY ProductId)
            THROW 51919,N'Original stock transaction does not match committed invoice lines.',1;
        IF EXISTS(SELECT 1 FROM inventory.InventoryTransactionDetails d
            LEFT JOIN master.Products p ON p.ProductId=d.ProductId AND p.TenantId=@TenantId
            WHERE d.TransactionId=@SaleStockId AND p.ProductId IS NULL)
            THROW 51920,N'Original stock product tenant ownership is invalid.',1;

        SET @ReversalId=NEWID(); SET @JournalId=NEWID(); SET @RestoreId=NEWID();
        INSERT finance.JournalEntries(JournalEntryId,JournalNumber,EntryDate,TransactionType,
            ReferenceType,ReferenceId,Narration,CreatedBy,TenantId)
        VALUES(@JournalId,CONCAT(N'JV-',UPPER(LEFT(REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N''),20))),
            @Now,N'SALE_REVERSAL',N'SALE_REVERSAL',@ReversalId,@Reason,@CreatedBy,@TenantId);
        INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount,Description)
        SELECT @JournalId,AccountId,CreditAmount,DebitAmount,CONCAT(N'Reversal: ',Description)
        FROM @BusinessLines;
        INSERT finance.LedgerEntries(JournalEntryId,AccountId,EntryDate,ReferenceType,
            ReferenceId,DebitAmount,CreditAmount,Narration)
        SELECT @JournalId,AccountId,@Now,N'SALE_REVERSAL',@ReversalId,CreditAmount,DebitAmount,
            CONCAT(N'Reversal: ',Description) FROM @BusinessLines;
        INSERT finance.DayBook(JournalEntryId,EntryDate,TransactionType,ReferenceType,ReferenceId,
            ReferenceNumber,DebitTotal,CreditTotal,Narration)
        VALUES(@JournalId,@Now,N'SALE_REVERSAL',N'SALE_REVERSAL',@ReversalId,
            @InvoiceNumber,@Grand,@Grand,@Reason);
        IF @CustomerId IS NOT NULL
            INSERT finance.CustomerLedger(CustomerId,JournalEntryId,EntryDate,EntryType,
                ReferenceId,ReferenceNumber,DebitAmount,CreditAmount,Narration)
            VALUES(@CustomerId,@JournalId,@Now,N'SALE_REVERSAL',@ReversalId,@InvoiceNumber,
                0,@Grand,@Reason);

        INSERT inventory.InventoryTransactions(TransactionId,TransactionNo,TransactionDate,
            TransactionType,ReferenceType,ReferenceId,WarehouseId,TenantId,Remarks,CreatedBy)
        VALUES(@RestoreId,CONCAT(N'REV-',REPLACE(CONVERT(nvarchar(36),@ReversalId),N'-',N'')),
            @Now,N'SALE_REVERSAL',N'FULL_SALE_REVERSAL',@ReversalId,@WarehouseId,@TenantId,@Reason,@CreatedBy);
        INSERT inventory.InventoryTransactionDetails(TransactionId,ProductId,BatchNo,SerialNo,Quantity,UnitCost)
        SELECT @RestoreId,ProductId,BatchNo,SerialNo,Quantity,UnitCost
        FROM inventory.InventoryTransactionDetails WHERE TransactionId=@SaleStockId;

        -- POS completion consumed the unbinned/unbatched balance at this warehouse.
        ;WITH restored AS (
            SELECT ProductId,SUM(Quantity) Quantity
            FROM inventory.InventoryTransactionDetails WHERE TransactionId=@SaleStockId GROUP BY ProductId)
        UPDATE b SET QuantityOnHand=b.QuantityOnHand+r.Quantity,
            LastUpdated=@Now,ModifiedOn=@Now,ModifiedBy=@CreatedBy
        FROM inventory.InventoryBalances b WITH(UPDLOCK,HOLDLOCK)
        JOIN restored r ON r.ProductId=b.ProductId
        WHERE b.WarehouseId=@WarehouseId AND (b.TenantId=@TenantId OR b.TenantId IS NULL)
          AND b.ZoneId IS NULL AND b.BinId IS NULL AND b.BatchNo IS NULL AND b.SerialNo IS NULL;
        IF @@ROWCOUNT<>(SELECT COUNT(DISTINCT ProductId) FROM inventory.InventoryTransactionDetails
                       WHERE TransactionId=@SaleStockId)
            THROW 51921,N'Original sale stock balances cannot all be restored.',1;

        UPDATE sales.SalesInvoices SET Status=N'CANCELLED',ModifiedOn=@Now
        WHERE InvoiceId=@InvoiceId AND TenantId=@TenantId AND Status=N'COMPLETED';
        IF @@ROWCOUNT<>1 THROW 51922,N'Sale state changed during reversal.',1;
        IF @DeliveryId IS NOT NULL
        BEGIN
            UPDATE commerce.OrderDeliveries SET DeliveryStatus=N'CANCELLED',UpdatedAt=@Now
            WHERE OrderDeliveryId=@DeliveryId AND TenantId=@TenantId
              AND DeliveryStatus=@DeliveryStatus;
            IF @@ROWCOUNT<>1 THROW 51923,N'Delivery state changed during reversal.',1;
            INSERT commerce.OrderDeliveryEvents(TenantId,OrderDeliveryId,EventType,PreviousStatus,
                NewStatus,Notes) VALUES(@TenantId,@DeliveryId,N'SALE_REVERSAL',@DeliveryStatus,
                N'CANCELLED',@Reason);
        END;
        INSERT sales.FullSaleReversals(ReversalId,TenantId,InvoiceId,OriginalSaleJournalId,
            ReversalJournalId,OriginalInventoryTransactionId,InventoryRestorationId,
            OriginalGrandTotal,CollectedAmount,RefundRequiredAmount,RefundStatus,Reason,CreatedBy,CreatedAt)
        VALUES(@ReversalId,@TenantId,@InvoiceId,@SaleJournalId,@JournalId,@SaleStockId,
            @RestoreId,@Grand,@Collected,@Collected,
            CASE WHEN @Collected>0 THEN N'REFUND_REQUIRED' ELSE N'NONE' END,
            @Reason,@CreatedBy,@Now);
        COMMIT TRANSACTION;
        SELECT @InvoiceId InvoiceId,@ReversalId ReversalId,@JournalId ReversalJournalId,
            @RestoreId InventoryRestorationId,@Grand OriginalGrandTotal,
            @Collected CollectedAmount,@Collected RefundRequiredAmount,
            CAST(0 AS bit) AlreadyReversed;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
