SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @CategoryId UNIQUEIDENTIFIER = NEWID(), @BrandId UNIQUEIDENTIFIER = NEWID(),
        @UnitId UNIQUEIDENTIFIER = NEWID(), @ProductId UNIQUEIDENTIFIER = NEWID(),
        @WarehouseTypeId UNIQUEIDENTIFIER = NEWID(), @WarehouseId UNIQUEIDENTIFIER = NEWID(),
        @BalanceId UNIQUEIDENTIFIER = NEWID(), @InvoiceId UNIQUEIDENTIFIER, @InvoiceItemId UNIQUEIDENTIFIER;

INSERT master.ProductCategories(ProductCategoryId,CategoryCode,CategoryName,DisplayOrder,CreatedOn,IsActive,IsDeleted)
VALUES(@CategoryId,'POS-VERIFY-CAT','POS Verification',1,SYSUTCDATETIME(),1,0);
INSERT master.Brands(BrandId,BrandCode,BrandName,CreatedOn,IsActive,IsDeleted)
VALUES(@BrandId,'POS-VERIFY-BRAND','POS Verification',SYSUTCDATETIME(),1,0);
INSERT master.UnitsOfMeasure(UnitId,UnitCode,UnitName,ShortName,DecimalPlaces,CreatedOn,IsActive,IsDeleted)
VALUES(@UnitId,'POS-VERIFY-EA','POS Verification Each','EA',0,SYSUTCDATETIME(),1,0);
INSERT master.Products(ProductId,ProductCode,Barcode,ProductName,CategoryId,BrandId,UnitId,GSTPercentage,PurchasePrice,SellingPrice,MRP,MinimumStock,MaximumStock,ReorderLevel,IsBatchManaged,IsSerialManaged,CreatedOn,IsActive,IsDeleted)
VALUES(@ProductId,'POS-VERIFY-PRODUCT','8900000000001','POS Verification Product',@CategoryId,@BrandId,@UnitId,18,50,100,118,0,100,5,0,0,SYSUTCDATETIME(),1,0);
INSERT inventory.WarehouseTypes(WarehouseTypeId,TypeCode,TypeName)
VALUES(@WarehouseTypeId,'POS-VERIFY','POS Verification');
INSERT inventory.Warehouses(WarehouseId,WarehouseCode,WarehouseName,WarehouseTypeId,IsDefault,CreatedBy)
VALUES(@WarehouseId,'POS-VERIFY-WH','POS Verification Warehouse',@WarehouseTypeId,0,'verification');
INSERT inventory.InventoryBalances(InventoryBalanceId,ProductId,WarehouseId,QuantityOnHand,AverageCost,LastPurchaseCost,CreatedBy)
VALUES(@BalanceId,@ProductId,@WarehouseId,10,50,50,'verification');

DECLARE @Created TABLE(InvoiceId UNIQUEIDENTIFIER,InvoiceNumber NVARCHAR(50),GrandTotal DECIMAL(18,2),PaidAmount DECIMAL(18,2),Status NVARCHAR(20));
DECLARE @ItemsJson NVARCHAR(MAX) = CONCAT(N'[{"ProductId":"',CONVERT(NVARCHAR(36),@ProductId),N'","Barcode":"8900000000001","Quantity":2,"UnitPrice":100,"DiscountPercentage":0,"DiscountAmount":0,"TaxPercentage":18}]');
INSERT @Created EXEC sales.POS_PostInvoice
  @WarehouseId=@WarehouseId,
  @ItemsJson=@ItemsJson,
  @PaymentsJson=N'[{"MethodCode":"CASH","Amount":100},{"MethodCode":"UPI","Amount":50,"ReferenceNumber":"VERIFY-UPI"}]',
  @CreatedBy=N'verification';

SELECT @InvoiceId=InvoiceId FROM @Created;
SELECT @InvoiceItemId=InvoiceItemId FROM sales.SalesInvoiceItems WHERE InvoiceId=@InvoiceId;
IF (SELECT QuantityOnHand FROM inventory.InventoryBalances WHERE InventoryBalanceId=@BalanceId) <> 8
    THROW 51100, 'Invoice did not deduct stock.', 1;

EXEC sales.POS_AddPayment @InvoiceId=@InvoiceId,@MethodCode=N'CARD',@Amount=86,@ReferenceNumber=N'VERIFY-CARD',@CreatedBy=N'verification';
IF (SELECT BalanceAmount FROM sales.SalesInvoices WHERE InvoiceId=@InvoiceId) <> 0
    THROW 51100, 'Payment did not settle the invoice.', 1;

DECLARE @ReturnJson NVARCHAR(MAX)=CONCAT(N'[{"InvoiceItemId":"',CONVERT(NVARCHAR(36),@InvoiceItemId),N'","Quantity":1}]');
EXEC sales.POS_ReturnInvoice @InvoiceId=@InvoiceId,@ItemsJson=@ReturnJson,@Reason=N'POS verification',@CreatedBy=N'verification';
IF (SELECT QuantityOnHand FROM inventory.InventoryBalances WHERE InventoryBalanceId=@BalanceId) <> 9
    THROW 51100, 'Return did not restore stock.', 1;

SELECT c.InvoiceNumber,c.GrandTotal,i.PaidAmount,i.BalanceAmount,i.Status,
       b.QuantityOnHand,
       (SELECT COUNT(*) FROM sales.SalesPayments WHERE InvoiceId=@InvoiceId) PaymentCount,
       (SELECT COUNT(*) FROM sales.SalesTaxes WHERE InvoiceId=@InvoiceId) TaxRowCount,
       (SELECT COUNT(*) FROM sales.SalesInvoiceReturns WHERE InvoiceId=@InvoiceId) ReturnRowCount
FROM @Created c
JOIN sales.SalesInvoices i ON i.InvoiceId=c.InvoiceId
JOIN inventory.InventoryBalances b ON b.InventoryBalanceId=@BalanceId;

ROLLBACK TRANSACTION;
