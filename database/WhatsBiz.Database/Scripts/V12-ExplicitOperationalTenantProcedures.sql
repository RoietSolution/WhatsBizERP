/* Idempotent procedure contract hardening. Applies only to Phase 2 writers. */
SET NOCOUNT ON;
DECLARE @Names TABLE(Name sysname PRIMARY KEY);
INSERT @Names VALUES (N'sales.POS_PostInvoice'),(N'sales.POS_AddPayment'),(N'sales.POS_ReturnInvoice'),(N'purchase.Purchase_Post'),(N'purchase.Purchase_AddPayment'),(N'purchase.Purchase_Return'),(N'inventory.StockAdjustment_Post'),(N'inventory.StockTransfer_Post'),(N'inventory.PhysicalVerification_Post');
DECLARE @name sysname,@sql nvarchar(max),@def nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @Names; OPEN c; FETCH NEXT FROM c INTO @name;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @def=OBJECT_DEFINITION(OBJECT_ID(@name));
 IF @def IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID(@name) AND name=N'@TenantId')
 BEGIN
  SET @def=REPLACE(@def,N'ALTER   PROCEDURE',N'__PROC__'); SET @def=REPLACE(@def,N'ALTER PROCEDURE',N'__PROC__'); SET @def=REPLACE(@def,N'CREATE   PROCEDURE',N'__PROC__'); SET @def=REPLACE(@def,N'CREATE PROCEDURE',N'__PROC__'); SET @def=REPLACE(@def,N'__PROC__',N'CREATE OR ALTER PROCEDURE');
  SET @def=REPLACE(@def,N'@CreatedBy NVARCHAR(256)=NULL',N'@CreatedBy NVARCHAR(256),@TenantId UNIQUEIDENTIFIER');
  SET @def=REPLACE(@def,N'@CreatedBy NVARCHAR(256) = NULL',N'@CreatedBy NVARCHAR(256),@TenantId UNIQUEIDENTIFIER');
  SET @def=REPLACE(@def,N'AS BEGIN SET NOCOUNT ON;',N'AS BEGIN SET NOCOUNT ON;DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N''TenantId''));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,''Tenant context is missing or mismatched.'',1;');
  SET @def=REPLACE(@def,N'INSERT sales.SalesInvoices(InvoiceId,InvoiceNumber,InvoiceDate,CounterId,ShiftId,CustomerId,WarehouseId,SalesPersonId,Subtotal,DiscountAmount,TaxAmount,RoundOff,GrandTotal,PaidAmount,Status,Remarks,CreatedBy)',N'INSERT sales.SalesInvoices(InvoiceId,InvoiceNumber,InvoiceDate,CounterId,ShiftId,CustomerId,WarehouseId,SalesPersonId,Subtotal,DiscountAmount,TaxAmount,RoundOff,GrandTotal,PaidAmount,Status,Remarks,CreatedBy,TenantId)');
  SET @def=REPLACE(@def,N'INSERT purchase.PurchaseInvoices(PurchaseInvoiceId,InvoiceNumber,SupplierId,SupplierInvoiceNo,InvoiceDate,DueDate,WarehouseId,Subtotal,DiscountAmount,TaxAmount,ExpenseAmount,RoundOff,GrandTotal,PaidAmount,Status,Remarks,CreatedBy)',N'INSERT purchase.PurchaseInvoices(PurchaseInvoiceId,InvoiceNumber,SupplierId,SupplierInvoiceNo,InvoiceDate,DueDate,WarehouseId,Subtotal,DiscountAmount,TaxAmount,ExpenseAmount,RoundOff,GrandTotal,PaidAmount,Status,Remarks,CreatedBy,TenantId)');
  SET @def=REPLACE(@def,N'INSERT inventory.InventoryTransactions(TransactionId,TransactionNo,TransactionDate,TransactionType,ReferenceType,ReferenceId,WarehouseId,Remarks,CreatedBy)',N'INSERT inventory.InventoryTransactions(TransactionId,TransactionNo,TransactionDate,TransactionType,ReferenceType,ReferenceId,WarehouseId,Remarks,CreatedBy,TenantId)');
  SET @def=REPLACE(@def,N'INSERT inventory.InventoryBalances(InventoryBalanceId,ProductId,WarehouseId,BatchNo,QuantityOnHand,AverageCost,LastPurchaseCost,CreatedBy)',N'INSERT inventory.InventoryBalances(InventoryBalanceId,ProductId,WarehouseId,BatchNo,QuantityOnHand,AverageCost,LastPurchaseCost,CreatedBy,TenantId)');
  SET @def=REPLACE(@def,N'@Grand,@Paid,@Status,@Remarks,@CreatedBy)',N'@Grand,@Paid,@Status,@Remarks,@CreatedBy,@TenantId)');
  SET @def=REPLACE(@def,N'@WarehouseId,@Remarks,@CreatedBy)',N'@WarehouseId,@Remarks,@CreatedBy,@TenantId)');
  SET @def=REPLACE(@def,N'@Price,@CreatedBy)',N'@Price,@CreatedBy,@TenantId)');
  EXEC sys.sp_executesql @def;
 END;
 FETCH NEXT FROM c INTO @name;
END;
CLOSE c; DEALLOCATE c;
