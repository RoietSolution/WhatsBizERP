CREATE PROCEDURE [sales].[POS_TransitionHeldInvoice]
    @InvoiceId uniqueidentifier,
    @Action nvarchar(20),
    @ModifiedBy nvarchar(256)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @Action NOT IN(N'COMPLETE',N'CANCEL') THROW 51100,'Invalid held invoice action.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Status nvarchar(20),@WarehouseId uniqueidentifier,@InvoiceNumber nvarchar(50);
        SELECT @Status=Status,@WarehouseId=WarehouseId,@InvoiceNumber=InvoiceNumber
        FROM sales.SalesInvoices WITH(UPDLOCK,HOLDLOCK) WHERE InvoiceId=@InvoiceId;
        IF @Status IS NULL THROW 51100,'Invoice not found.',1;
        IF @Status NOT IN(N'HELD',N'SUSPENDED') THROW 51100,'Only held or suspended invoices can be processed.',1;
        IF @Action=N'CANCEL'
        BEGIN
            UPDATE sales.SalesInvoices SET Status=N'CANCELLED',ModifiedOn=SYSUTCDATETIME() WHERE InvoiceId=@InvoiceId;
            COMMIT TRANSACTION; RETURN;
        END;
        DECLARE @TransactionId uniqueidentifier=NEWID(),@AllowNegative bit=(SELECT TOP(1)NegativeStockAllowed FROM inventory.InventorySettings);
        INSERT inventory.InventoryTransactions(TransactionId,TransactionNo,TransactionDate,TransactionType,ReferenceType,ReferenceId,WarehouseId,Remarks,CreatedBy)
        VALUES(@TransactionId,@InvoiceNumber+N'-STK',SYSUTCDATETIME(),N'SALE',N'SALES_INVOICE',@InvoiceId,@WarehouseId,N'Completed from held order',@ModifiedBy);
        DECLARE @ProductId uniqueidentifier,@Quantity decimal(18,4),@BalanceId uniqueidentifier,@Available decimal(18,4),@AverageCost decimal(18,4);
        DECLARE items CURSOR LOCAL FAST_FORWARD FOR SELECT ProductId,Quantity FROM sales.SalesInvoiceItems WHERE InvoiceId=@InvoiceId;
        OPEN items; FETCH NEXT FROM items INTO @ProductId,@Quantity;
        WHILE @@FETCH_STATUS=0
        BEGIN
            SET @BalanceId=NULL; SET @Available=NULL; SET @AverageCost=NULL;
            SELECT @BalanceId=InventoryBalanceId,@Available=QuantityAvailable,@AverageCost=AverageCost
            FROM inventory.InventoryBalances WITH(UPDLOCK,HOLDLOCK)
            WHERE ProductId=@ProductId AND WarehouseId=@WarehouseId AND ZoneId IS NULL AND BinId IS NULL AND BatchNo IS NULL AND SerialNo IS NULL;
            IF @BalanceId IS NULL OR(ISNULL(@AllowNegative,0)=0 AND @Available<@Quantity)
            BEGIN CLOSE items; DEALLOCATE items; THROW 51101,'Insufficient stock for invoice.',1; END;
            UPDATE inventory.InventoryBalances SET QuantityOnHand=QuantityOnHand-@Quantity,LastUpdated=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@ModifiedBy WHERE InventoryBalanceId=@BalanceId;
            INSERT inventory.InventoryTransactionDetails(TransactionId,ProductId,Quantity,UnitCost) VALUES(@TransactionId,@ProductId,@Quantity,ISNULL(@AverageCost,0));
            FETCH NEXT FROM items INTO @ProductId,@Quantity;
        END;
        CLOSE items; DEALLOCATE items;
        UPDATE sales.SalesInvoices SET Status=N'COMPLETED',ModifiedOn=SYSUTCDATETIME() WHERE InvoiceId=@InvoiceId;
        EXEC finance.PostSource @SourceType=N'SALE',@SourceId=@InvoiceId,@CreatedBy=@ModifiedBy;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF CURSOR_STATUS('local','items')>=0 CLOSE items;
        IF CURSOR_STATUS('local','items')>=-1 DEALLOCATE items;
        IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
