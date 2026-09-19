/* Recreate the Phase 2 operational tenant guards with the SET options required
   for DML against indexed computed columns and indexed views. Guard behavior is
   intentionally identical to V11-OperationalTenantWriteGuards.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER purchase.TR_Suppliers_TenantGuard ON purchase.Suppliers AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for supplier writes.',1;
 UPDATE s SET TenantId=@t FROM purchase.Suppliers s JOIN inserted i ON i.SupplierId=s.SupplierId WHERE s.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM purchase.Suppliers s JOIN inserted i ON i.SupplierId=s.SupplierId WHERE s.TenantId<>@t) THROW 51401,'Supplier is outside the current tenant.',1;
END;
GO

/* The following Phase 2 procedures were also found with captured
   QUOTED_IDENTIFIER OFF during post-V24 flow verification. Their live,
   tenant-parameterized definitions are restated explicitly so the required
   module SET metadata is deterministic. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE inventory.PhysicalVerification_Post @WarehouseId uniqueidentifier,@VerificationDate datetimeoffset,@ApprovalStatus nvarchar(20),@Remarks nvarchar(1000)=NULL,@ItemsJson nvarchar(max),@CreatedBy NVARCHAR(256),@TenantId UNIQUEIDENTIFIER AS BEGIN SET NOCOUNT ON;DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N'TenantId'));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,'Tenant context is missing or mismatched.',1;SET XACT_ABORT ON;BEGIN TRAN;BEGIN TRY DECLARE @id uniqueidentifier=NEWID(),@no nvarchar(50)=CONCAT('PHY-',FORMAT(SYSUTCDATETIME(),'yyyyMMddHHmmssfff')),@adjust uniqueidentifier=NULL,@tx uniqueidentifier=NEWID();DECLARE @i table(ProductId uniqueidentifier,ZoneId uniqueidentifier,BinId uniqueidentifier,BatchNo nvarchar(100),SerialNo nvarchar(100),CountedQuantity decimal(18,4),UnitCost decimal(18,4),SystemQuantity decimal(18,4));INSERT @i(ProductId,ZoneId,BinId,BatchNo,SerialNo,CountedQuantity,UnitCost)SELECT * FROM OPENJSON(@ItemsJson) WITH(ProductId uniqueidentifier '$.ProductId',ZoneId uniqueidentifier '$.ZoneId',BinId uniqueidentifier '$.BinId',BatchNo nvarchar(100) '$.BatchNo',SerialNo nvarchar(100) '$.SerialNo',CountedQuantity decimal(18,4) '$.CountedQuantity',UnitCost decimal(18,4) '$.UnitCost');UPDATE i SET SystemQuantity=ISNULL((SELECT SUM(b.QuantityOnHand) FROM inventory.InventoryBalances b WITH(UPDLOCK,HOLDLOCK) WHERE b.ProductId=i.ProductId AND b.WarehouseId=@WarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.ZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.BinId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,'')),0) FROM @i i;INSERT inventory.PhysicalStockVerification(VerificationId,VerificationNo,VerificationDate,WarehouseId,ApprovalStatus,Remarks,ApprovedBy,ApprovedOn,CreatedBy)VALUES(@id,@no,@VerificationDate,@WarehouseId,@ApprovalStatus,@Remarks,CASE WHEN @ApprovalStatus='APPROVED' THEN @CreatedBy END,CASE WHEN @ApprovalStatus='APPROVED' THEN SYSUTCDATETIME() END,@CreatedBy);INSERT inventory.PhysicalStockVerificationItems(VerificationId,ProductId,ZoneId,BinId,BatchNo,SerialNo,SystemQuantity,CountedQuantity,UnitCost)SELECT @id,ProductId,ZoneId,BinId,BatchNo,SerialNo,SystemQuantity,CountedQuantity,UnitCost FROM @i;
IF @ApprovalStatus='APPROVED' AND EXISTS(SELECT 1 FROM @i WHERE CountedQuantity<>SystemQuantity) BEGIN DECLARE @json nvarchar(max)=(SELECT ProductId,ZoneId SourceZoneId,BinId SourceBinId,BatchNo,SerialNo,ABS(CountedQuantity-SystemQuantity) Quantity,UnitCost FROM @i WHERE CountedQuantity<>SystemQuantity FOR JSON PATH);DECLARE @result table(OperationId uniqueidentifier,TransactionId uniqueidentifier,Number nvarchar(50));IF NOT EXISTS(SELECT 1 FROM @i WHERE CountedQuantity<SystemQuantity) OR NOT EXISTS(SELECT 1 FROM @i WHERE CountedQuantity>SystemQuantity) BEGIN DECLARE @type nvarchar(10)=CASE WHEN EXISTS(SELECT 1 FROM @i WHERE CountedQuantity>SystemQuantity) THEN 'INCREASE' ELSE 'DECREASE' END;INSERT @result EXEC inventory.StockAdjustment_Post @WarehouseId,@type,'PHYSICAL_VERIFICATION','APPROVED',@Remarks,@json,@CreatedBy,@TenantId;SELECT TOP 1 @adjust=OperationId,@tx=TransactionId FROM @result;END ELSE BEGIN DECLARE @up nvarchar(max)=(SELECT ProductId,ZoneId SourceZoneId,BinId SourceBinId,BatchNo,SerialNo,CountedQuantity-SystemQuantity Quantity,UnitCost FROM @i WHERE CountedQuantity>SystemQuantity FOR JSON PATH),@down nvarchar(max)=(SELECT ProductId,ZoneId SourceZoneId,BinId SourceBinId,BatchNo,SerialNo,SystemQuantity-CountedQuantity Quantity,UnitCost FROM @i WHERE CountedQuantity<SystemQuantity FOR JSON PATH);INSERT @result EXEC inventory.StockAdjustment_Post @WarehouseId,'INCREASE','PHYSICAL_VERIFICATION','APPROVED',@Remarks,@up,@CreatedBy,@TenantId;INSERT @result EXEC inventory.StockAdjustment_Post @WarehouseId,'DECREASE','PHYSICAL_VERIFICATION','APPROVED',@Remarks,@down,@CreatedBy,@TenantId;SELECT TOP 1 @adjust=OperationId,@tx=TransactionId FROM @result;END UPDATE inventory.PhysicalStockVerification SET GeneratedAdjustmentId=@adjust WHERE VerificationId=@id;END COMMIT;SELECT @id OperationId,@tx TransactionId,@no Number;END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK;THROW;END CATCH END
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE inventory.StockAdjustment_Post @WarehouseId uniqueidentifier,@AdjustmentType nvarchar(10),@ReasonCode nvarchar(50),@ApprovalStatus nvarchar(20),@Remarks nvarchar(1000)=NULL,@ItemsJson nvarchar(max),@CreatedBy NVARCHAR(256),@TenantId UNIQUEIDENTIFIER AS
BEGIN SET NOCOUNT ON;DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N'TenantId'));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,'Tenant context is missing or mismatched.',1;SET XACT_ABORT ON;BEGIN TRAN;BEGIN TRY
DECLARE @id uniqueidentifier=NEWID(),@tx uniqueidentifier=NEWID(),@no nvarchar(50)=CONCAT('ADJ-',FORMAT(SYSUTCDATETIME(),'yyyyMMddHHmmssfff')),@sign decimal(18,4)=CASE WHEN @AdjustmentType='INCREASE' THEN 1 ELSE -1 END;
DECLARE @items table(ProductId uniqueidentifier,ZoneId uniqueidentifier NULL,BinId uniqueidentifier NULL,BatchNo nvarchar(100) NULL,SerialNo nvarchar(100) NULL,Quantity decimal(18,4),UnitCost decimal(18,4));INSERT @items SELECT ProductId,SourceZoneId,SourceBinId,BatchNo,SerialNo,Quantity,UnitCost FROM OPENJSON(@ItemsJson) WITH(ProductId uniqueidentifier '$.ProductId',SourceZoneId uniqueidentifier '$.SourceZoneId',SourceBinId uniqueidentifier '$.SourceBinId',BatchNo nvarchar(100) '$.BatchNo',SerialNo nvarchar(100) '$.SerialNo',Quantity decimal(18,4) '$.Quantity',UnitCost decimal(18,4) '$.UnitCost');IF NOT EXISTS(SELECT 1 FROM @items) THROW 51000,'At least one adjustment item is required.',1;
INSERT inventory.InventoryTransactions(TransactionId,TransactionNo,TransactionDate,TransactionType,ReferenceType,ReferenceId,WarehouseId,Remarks,CreatedBy,CreatedOn)VALUES(@tx,@no,SYSUTCDATETIME(),CASE WHEN @AdjustmentType='INCREASE' THEN 'ADJUSTMENT_IN' ELSE 'ADJUSTMENT_OUT' END,'STOCK_ADJUSTMENT',@id,@WarehouseId,@Remarks,@CreatedBy,SYSUTCDATETIME());INSERT inventory.StockAdjustments(StockAdjustmentId,TransactionId,AdjustmentNo,AdjustmentType,ReasonCode,ApprovalStatus,ApprovedBy,ApprovedOn,CreatedOn)VALUES(@id,@tx,@no,@AdjustmentType,@ReasonCode,@ApprovalStatus,CASE WHEN @ApprovalStatus='APPROVED' THEN @CreatedBy END,CASE WHEN @ApprovalStatus='APPROVED' THEN SYSUTCDATETIME() END,SYSUTCDATETIME());INSERT inventory.StockAdjustmentItems(StockAdjustmentId,ProductId,ZoneId,BinId,BatchNo,SerialNo,Quantity,UnitCost)SELECT @id,* FROM @items;
IF @ApprovalStatus='APPROVED' BEGIN IF @AdjustmentType='DECREASE' AND EXISTS(SELECT 1 FROM @items i OUTER APPLY(SELECT SUM(QuantityAvailable) q FROM inventory.InventoryBalances b WITH(UPDLOCK,HOLDLOCK) WHERE b.ProductId=i.ProductId AND b.WarehouseId=@WarehouseId AND (b.ZoneId=i.ZoneId OR b.ZoneId IS NULL AND i.ZoneId IS NULL) AND (b.BinId=i.BinId OR b.BinId IS NULL AND i.BinId IS NULL) AND (b.BatchNo=i.BatchNo OR b.BatchNo IS NULL AND i.BatchNo IS NULL) AND (b.SerialNo=i.SerialNo OR b.SerialNo IS NULL AND i.SerialNo IS NULL))b WHERE ISNULL(b.q,0)<i.Quantity) AND NOT EXISTS(SELECT 1 FROM inventory.InventorySettings WHERE NegativeStockAllowed=1) THROW 51001,'Insufficient available stock.',1;
INSERT inventory.InventoryBalances(ProductId,WarehouseId,ZoneId,BinId,BatchNo,SerialNo,QuantityOnHand,QuantityReserved,AverageCost,LastPurchaseCost,LastUpdated,CreatedOn,CreatedBy)SELECT i.ProductId,@WarehouseId,i.ZoneId,i.BinId,i.BatchNo,i.SerialNo,0,0,i.UnitCost,i.UnitCost,SYSUTCDATETIME(),SYSUTCDATETIME(),@CreatedBy FROM @items i WHERE NOT EXISTS(SELECT 1 FROM inventory.InventoryBalances b WHERE b.ProductId=i.ProductId AND b.WarehouseId=@WarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.ZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.BinId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,''));UPDATE b SET QuantityOnHand=b.QuantityOnHand+@sign*i.Quantity,LastUpdated=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@CreatedBy FROM inventory.InventoryBalances b JOIN @items i ON b.ProductId=i.ProductId AND b.WarehouseId=@WarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.ZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.BinId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,'');
INSERT inventory.InventoryTransactionDetails(TransactionId,ProductId,BatchNo,SerialNo,Quantity,UnitCost)SELECT @tx,ProductId,BatchNo,SerialNo,Quantity,UnitCost FROM @items;INSERT inventory.StockMovementHistory(TransactionId,TransactionDetailId,MovementDate,MovementType,ProductId,WarehouseId,ZoneId,BinId,BatchNo,SerialNo,Quantity,BalanceAfter,ReferenceType,ReferenceId,Remarks,CreatedBy)SELECT @tx,d.TransactionDetailId,SYSUTCDATETIME(),CASE WHEN @sign=1 THEN 'ADJUSTMENT_IN' ELSE 'ADJUSTMENT_OUT' END,d.ProductId,@WarehouseId,i.ZoneId,i.BinId,d.BatchNo,d.SerialNo,@sign*d.Quantity,b.QuantityOnHand,'STOCK_ADJUSTMENT',@id,@Remarks,@CreatedBy FROM inventory.InventoryTransactionDetails d JOIN @items i ON i.ProductId=d.ProductId AND ISNULL(i.BatchNo,'')=ISNULL(d.BatchNo,'') AND ISNULL(i.SerialNo,'')=ISNULL(d.SerialNo,'') JOIN inventory.InventoryBalances b ON b.ProductId=i.ProductId AND b.WarehouseId=@WarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.ZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.BinId,'00000000-0000-0000-0000-000000000000') WHERE d.TransactionId=@tx;
IF OBJECT_ID('finance.PostStockAdjustment') IS NOT NULL EXEC finance.PostStockAdjustment @TransactionId=@tx,@CreatedBy=@CreatedBy;END
COMMIT;SELECT @id OperationId,@tx TransactionId,@no Number;END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK;THROW;END CATCH END
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE inventory.StockTransfer_Post @SourceWarehouseId uniqueidentifier,@DestinationWarehouseId uniqueidentifier,@TransferDate datetimeoffset,@ApprovalStatus nvarchar(20),@Remarks nvarchar(1000)=NULL,@ItemsJson nvarchar(max),@CreatedBy NVARCHAR(256),@TenantId UNIQUEIDENTIFIER AS
BEGIN SET NOCOUNT ON;DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N'TenantId'));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,'Tenant context is missing or mismatched.',1;SET XACT_ABORT ON;BEGIN TRAN;BEGIN TRY DECLARE @id uniqueidentifier=NEWID(),@out uniqueidentifier=NEWID(),@in uniqueidentifier=NEWID(),@no nvarchar(50)=CONCAT('TRF-',FORMAT(SYSUTCDATETIME(),'yyyyMMddHHmmssfff'));DECLARE @i table(ProductId uniqueidentifier,SourceZoneId uniqueidentifier,SourceBinId uniqueidentifier,DestinationZoneId uniqueidentifier,DestinationBinId uniqueidentifier,BatchNo nvarchar(100),SerialNo nvarchar(100),Quantity decimal(18,4),UnitCost decimal(18,4));INSERT @i SELECT * FROM OPENJSON(@ItemsJson) WITH(ProductId uniqueidentifier '$.ProductId',SourceZoneId uniqueidentifier '$.SourceZoneId',SourceBinId uniqueidentifier '$.SourceBinId',DestinationZoneId uniqueidentifier '$.DestinationZoneId',DestinationBinId uniqueidentifier '$.DestinationBinId',BatchNo nvarchar(100) '$.BatchNo',SerialNo nvarchar(100) '$.SerialNo',Quantity decimal(18,4) '$.Quantity',UnitCost decimal(18,4) '$.UnitCost');IF NOT EXISTS(SELECT 1 FROM @i)THROW 51000,'At least one transfer item is required.',1;INSERT inventory.InventoryTransactions(TransactionId,TransactionNo,TransactionDate,TransactionType,ReferenceType,ReferenceId,WarehouseId,Remarks,CreatedBy)VALUES(@out,@no+'-OUT',@TransferDate,'TRANSFER_OUT','STOCK_TRANSFER',@id,@SourceWarehouseId,@Remarks,@CreatedBy),(@in,@no+'-IN',@TransferDate,'TRANSFER_IN','STOCK_TRANSFER',@id,@DestinationWarehouseId,@Remarks,@CreatedBy);INSERT inventory.StockTransfers(StockTransferId,TransferNo,SourceTransactionId,DestinationTransactionId,SourceWarehouseId,DestinationWarehouseId,ApprovalStatus,TransferDate,Remarks,CreatedBy,CreatedOn)VALUES(@id,@no,@out,@in,@SourceWarehouseId,@DestinationWarehouseId,CASE WHEN @ApprovalStatus='APPROVED' THEN 'COMPLETED' ELSE @ApprovalStatus END,@TransferDate,@Remarks,@CreatedBy,SYSUTCDATETIME());INSERT inventory.StockTransferItems(StockTransferId,ProductId,SourceZoneId,SourceBinId,DestinationZoneId,DestinationBinId,BatchNo,SerialNo,Quantity,UnitCost)SELECT @id,* FROM @i;
IF @ApprovalStatus='APPROVED' BEGIN IF EXISTS(SELECT 1 FROM @i i OUTER APPLY(SELECT SUM(QuantityAvailable) q FROM inventory.InventoryBalances b WITH(UPDLOCK,HOLDLOCK) WHERE b.ProductId=i.ProductId AND b.WarehouseId=@SourceWarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.SourceZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.SourceBinId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,''))b WHERE ISNULL(b.q,0)<i.Quantity) AND NOT EXISTS(SELECT 1 FROM inventory.InventorySettings WHERE NegativeStockAllowed=1)THROW 51001,'Insufficient stock for transfer.',1;UPDATE b SET QuantityOnHand=QuantityOnHand-i.Quantity,LastUpdated=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@CreatedBy FROM inventory.InventoryBalances b JOIN @i i ON b.ProductId=i.ProductId AND b.WarehouseId=@SourceWarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.SourceZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.SourceBinId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,'');INSERT inventory.InventoryBalances(ProductId,WarehouseId,ZoneId,BinId,BatchNo,SerialNo,QuantityOnHand,QuantityReserved,AverageCost,LastPurchaseCost,LastUpdated,CreatedOn,CreatedBy)SELECT ProductId,@DestinationWarehouseId,DestinationZoneId,DestinationBinId,BatchNo,SerialNo,0,0,UnitCost,UnitCost,SYSUTCDATETIME(),SYSUTCDATETIME(),@CreatedBy FROM @i i WHERE NOT EXISTS(SELECT 1 FROM inventory.InventoryBalances b WHERE b.ProductId=i.ProductId AND b.WarehouseId=@DestinationWarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.DestinationZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.DestinationBinId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,''));UPDATE b SET QuantityOnHand=QuantityOnHand+i.Quantity,LastUpdated=SYSUTCDATETIME(),ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@CreatedBy FROM inventory.InventoryBalances b JOIN @i i ON b.ProductId=i.ProductId AND b.WarehouseId=@DestinationWarehouseId AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.DestinationZoneId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(i.DestinationBinId,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,'');
INSERT inventory.InventoryTransactionDetails(TransactionId,ProductId,BatchNo,SerialNo,Quantity,UnitCost)SELECT @out,ProductId,BatchNo,SerialNo,Quantity,UnitCost FROM @i UNION ALL SELECT @in,ProductId,BatchNo,SerialNo,Quantity,UnitCost FROM @i;INSERT inventory.StockMovementHistory(TransactionId,TransactionDetailId,MovementDate,MovementType,ProductId,WarehouseId,ZoneId,BinId,BatchNo,SerialNo,Quantity,BalanceAfter,ReferenceType,ReferenceId,Remarks,CreatedBy)SELECT d.TransactionId,d.TransactionDetailId,@TransferDate,CASE WHEN d.TransactionId=@out THEN 'TRANSFER_OUT' ELSE 'TRANSFER_IN' END,d.ProductId,CASE WHEN d.TransactionId=@out THEN @SourceWarehouseId ELSE @DestinationWarehouseId END,CASE WHEN d.TransactionId=@out THEN i.SourceZoneId ELSE i.DestinationZoneId END,CASE WHEN d.TransactionId=@out THEN i.SourceBinId ELSE i.DestinationBinId END,d.BatchNo,d.SerialNo,CASE WHEN d.TransactionId=@out THEN -d.Quantity ELSE d.Quantity END,b.QuantityOnHand,'STOCK_TRANSFER',@id,@Remarks,@CreatedBy FROM inventory.InventoryTransactionDetails d JOIN @i i ON i.ProductId=d.ProductId AND ISNULL(i.BatchNo,'')=ISNULL(d.BatchNo,'') AND ISNULL(i.SerialNo,'')=ISNULL(d.SerialNo,'') LEFT JOIN inventory.InventoryBalances b ON b.ProductId=i.ProductId AND b.WarehouseId=CASE WHEN d.TransactionId=@out THEN @SourceWarehouseId ELSE @DestinationWarehouseId END AND ISNULL(b.ZoneId,'00000000-0000-0000-0000-000000000000')=ISNULL(CASE WHEN d.TransactionId=@out THEN i.SourceZoneId ELSE i.DestinationZoneId END,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BinId,'00000000-0000-0000-0000-000000000000')=ISNULL(CASE WHEN d.TransactionId=@out THEN i.SourceBinId ELSE i.DestinationBinId END,'00000000-0000-0000-0000-000000000000') AND ISNULL(b.BatchNo,'')=ISNULL(i.BatchNo,'') AND ISNULL(b.SerialNo,'')=ISNULL(i.SerialNo,'') WHERE d.TransactionId IN(@out,@in);END COMMIT;SELECT @id OperationId,@out TransactionId,@no Number;END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK;THROW;END CATCH END
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER inventory.TR_Warehouses_TenantGuard ON inventory.Warehouses AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for warehouse writes.',1;
 UPDATE w SET TenantId=@t FROM inventory.Warehouses w JOIN inserted i ON i.WarehouseId=w.WarehouseId WHERE w.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.Warehouses w JOIN inserted i ON i.WarehouseId=w.WarehouseId WHERE w.TenantId<>@t) THROW 51401,'Warehouse is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesInvoices_TenantGuard ON sales.SalesInvoices AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for sales writes.',1;
 UPDATE h SET TenantId=@t FROM sales.SalesInvoices h JOIN inserted i ON i.InvoiceId=h.InvoiceId WHERE h.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM sales.SalesInvoices h JOIN inserted i ON i.InvoiceId=h.InvoiceId WHERE h.TenantId<>@t) THROW 51401,'Sales invoice is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Sales warehouse is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE c.TenantId<>@t OR c.IsDeleted=1) THROW 51403,'Sales customer is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchaseInvoices_TenantGuard ON purchase.PurchaseInvoices AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for purchase writes.',1;
 UPDATE h SET TenantId=@t FROM purchase.PurchaseInvoices h JOIN inserted i ON i.PurchaseInvoiceId=h.PurchaseInvoiceId WHERE h.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM purchase.PurchaseInvoices h JOIN inserted i ON i.PurchaseInvoiceId=h.PurchaseInvoiceId WHERE h.TenantId<>@t) THROW 51401,'Purchase invoice is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Purchase warehouse is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId WHERE s.TenantId<>@t OR s.IsDeleted=1) THROW 51403,'Purchase supplier is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER inventory.TR_InventoryBalances_TenantGuard ON inventory.InventoryBalances AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for inventory writes.',1;
 UPDATE b SET TenantId=@t FROM inventory.InventoryBalances b JOIN inserted i ON i.InventoryBalanceId=b.InventoryBalanceId WHERE b.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.InventoryBalances b JOIN inserted i ON i.InventoryBalanceId=b.InventoryBalanceId WHERE b.TenantId<>@t) THROW 51401,'Inventory balance is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN master.Products p ON p.ProductId=i.ProductId WHERE p.TenantId<>@t OR p.IsDeleted=1) THROW 51402,'Inventory product is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51403,'Inventory warehouse is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER inventory.TR_InventoryTransactions_TenantGuard ON inventory.InventoryTransactions AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for inventory writes.',1;
 UPDATE x SET TenantId=@t FROM inventory.InventoryTransactions x JOIN inserted i ON i.TransactionId=x.TransactionId WHERE x.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.InventoryTransactions x JOIN inserted i ON i.TransactionId=x.TransactionId WHERE x.TenantId<>@t) THROW 51401,'Inventory transaction is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Transaction warehouse is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesPayments_TenantGuard ON sales.SalesPayments AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for payment writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId WHERE h.TenantId<>@t OR h.InvoiceId IS NULL) THROW 51401,'Sales payment invoice is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesInvoiceItems_TenantGuard ON sales.SalesInvoiceItems AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for sales item writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=i.ProductId WHERE h.TenantId<>@t OR p.TenantId<>@t OR p.IsDeleted=1) THROW 51401,'Sales item ownership is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchaseInvoiceItems_TenantGuard ON purchase.PurchaseInvoiceItems AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for purchase item writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId JOIN master.Products p ON p.ProductId=i.ProductId WHERE h.TenantId<>@t OR p.TenantId<>@t OR p.IsDeleted=1) THROW 51401,'Purchase item ownership is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchasePayments_TenantGuard ON purchase.PurchasePayments AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for payment writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId WHERE h.TenantId<>@t OR h.PurchaseInvoiceId IS NULL) THROW 51401,'Purchase payment invoice is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchaseReturns_TenantGuard ON purchase.PurchaseReturns AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for return writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId WHERE h.TenantId<>@t OR h.PurchaseInvoiceId IS NULL) THROW 51401,'Purchase return invoice is outside the current tenant.',1;
END;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesReturns_TenantGuard ON sales.SalesInvoiceReturns AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for return writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId WHERE h.TenantId<>@t OR h.InvoiceId IS NULL) THROW 51401,'Sales return invoice is outside the current tenant.',1;
END;
GO
