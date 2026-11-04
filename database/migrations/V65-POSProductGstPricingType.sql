/* V65: make regular POS posting honor persisted product GST pricing mode. */
SET XACT_ABORT ON;

IF OBJECT_ID(N'sales.POS_PostInvoice', N'P') IS NULL
    THROW 65000, N'V65 requires sales.POS_PostInvoice.', 1;
IF COL_LENGTH(N'master.Products', N'GstPricingType') IS NULL
    THROW 65001, N'V65 requires master.Products.GstPricingType.', 1;

DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'sales.POS_PostInvoice', N'P'));
IF @definition IS NULL
    THROW 65002, N'V65 could not read the existing POS procedure definition.', 1;

DECLARE @old nvarchar(max), @new nvarchar(max);

SET @definition = LTRIM(@definition);
DECLARE @procedurePosition int = CHARINDEX(N'PROCEDURE', UPPER(@definition));
IF @procedurePosition = 0
    THROW 65003, N'V65 expected the existing POS procedure definition.', 1;
SET @definition = STUFF(@definition, 1, @procedurePosition + LEN(N'PROCEDURE') - 1, N'CREATE OR ALTER PROCEDURE');

SET @old = N'@Tax DECIMAL(18,2)=0,@Grand DECIMAL(18,2)';
SET @new = N'@Tax DECIMAL(18,2)=0,@AdditionalTax DECIMAL(18,2)=0,@Grand DECIMAL(18,2)';
IF CHARINDEX(@old, @definition) = 0
    THROW 65004, N'V65 could not locate the POS tax variable contract.', 1;
SET @definition = REPLACE(@definition, @old, @new);

SET @old = N'@Tax=SUM(((Quantity*UnitPrice)-CASE WHEN DiscountAmount>0 THEN DiscountAmount ELSE Quantity*UnitPrice*DiscountPercentage/100 END)*TaxPercentage/100) FROM OPENJSON(@ItemsJson) WITH(ProductId UNIQUEIDENTIFIER,Quantity DECIMAL(18,4),UnitPrice DECIMAL(18,4),DiscountPercentage DECIMAL(5,2),DiscountAmount DECIMAL(18,2),TaxPercentage DECIMAL(5,2))';
SET @new = N'@Tax=SUM(CASE WHEN p.GstPricingType=N''INCLUSIVE'' THEN ROUND((j.Quantity*j.UnitPrice-CASE WHEN j.DiscountAmount>0 THEN j.DiscountAmount ELSE j.Quantity*j.UnitPrice*j.DiscountPercentage/100 END)*j.TaxPercentage/(100+j.TaxPercentage),2) ELSE ROUND((j.Quantity*j.UnitPrice-CASE WHEN j.DiscountAmount>0 THEN j.DiscountAmount ELSE j.Quantity*j.UnitPrice*j.DiscountPercentage/100 END)*j.TaxPercentage/100,2) END),@AdditionalTax=SUM(CASE WHEN p.GstPricingType=N''INCLUSIVE'' THEN 0 ELSE ROUND((j.Quantity*j.UnitPrice-CASE WHEN j.DiscountAmount>0 THEN j.DiscountAmount ELSE j.Quantity*j.UnitPrice*j.DiscountPercentage/100 END)*j.TaxPercentage/100,2) END) FROM OPENJSON(@ItemsJson) WITH(ProductId UNIQUEIDENTIFIER,Quantity DECIMAL(18,4),UnitPrice DECIMAL(18,4),DiscountPercentage DECIMAL(5,2),DiscountAmount DECIMAL(18,2),TaxPercentage DECIMAL(5,2)) j JOIN master.Products p ON p.ProductId=j.ProductId AND p.TenantId=@TenantId';
IF CHARINDEX(@old, @definition) = 0
    THROW 65005, N'V65 could not locate the POS aggregate tax calculation.', 1;
SET @definition = REPLACE(@definition, @old, @new);

SET @old = N'@Grand=ROUND(@Subtotal-@ItemDiscount-@BillDiscount+@Tax+@DeliveryCharge+@RoundOff,2)';
SET @new = N'@Grand=ROUND(@Subtotal-@ItemDiscount-@BillDiscount+@AdditionalTax+@DeliveryCharge+@RoundOff,2)';
IF CHARINDEX(@old, @definition) = 0
    THROW 65006, N'V65 could not locate the POS grand-total calculation.', 1;
SET @definition = REPLACE(@definition, @old, @new);

SET @old = N'DECLARE item_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT ProductId,Barcode,Quantity,UnitPrice,DiscountPercentage,DiscountAmount,TaxPercentage FROM OPENJSON(@ItemsJson) WITH(ProductId UNIQUEIDENTIFIER,Barcode NVARCHAR(100),Quantity DECIMAL(18,4),UnitPrice DECIMAL(18,4),DiscountPercentage DECIMAL(5,2),DiscountAmount DECIMAL(18,2),TaxPercentage DECIMAL(5,2));';
SET @new = N'DECLARE item_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT j.ProductId,j.Barcode,j.Quantity,j.UnitPrice,j.DiscountPercentage,j.DiscountAmount,j.TaxPercentage,p.GstPricingType FROM OPENJSON(@ItemsJson) WITH(ProductId UNIQUEIDENTIFIER,Barcode NVARCHAR(100),Quantity DECIMAL(18,4),UnitPrice DECIMAL(18,4),DiscountPercentage DECIMAL(5,2),DiscountAmount DECIMAL(18,2),TaxPercentage DECIMAL(5,2)) j JOIN master.Products p ON p.ProductId=j.ProductId AND p.TenantId=@TenantId;';
IF CHARINDEX(@old, @definition) = 0
    THROW 65007, N'V65 could not locate the POS item cursor.', 1;
SET @definition = REPLACE(@definition, @old, @new);

SET @old = N'@ProductId UNIQUEIDENTIFIER,@Barcode NVARCHAR(100),@Quantity DECIMAL(18,4),@UnitPrice DECIMAL(18,4),@DiscountPercentage DECIMAL(5,2),@DiscountAmount DECIMAL(18,2),@TaxPercentage DECIMAL(5,2),@Taxable DECIMAL(18,2)';
SET @new = N'@ProductId UNIQUEIDENTIFIER,@Barcode NVARCHAR(100),@Quantity DECIMAL(18,4),@UnitPrice DECIMAL(18,4),@DiscountPercentage DECIMAL(5,2),@DiscountAmount DECIMAL(18,2),@TaxPercentage DECIMAL(5,2),@GstPricingType NVARCHAR(10),@Taxable DECIMAL(18,2)';
IF CHARINDEX(@old, @definition) = 0
    THROW 65008, N'V65 could not locate the POS item variable contract.', 1;
SET @definition = REPLACE(@definition, @old, @new);

SET @old = N'FETCH NEXT FROM item_cursor INTO @ProductId,@Barcode,@Quantity,@UnitPrice,@DiscountPercentage,@DiscountAmount,@TaxPercentage;';
SET @new = N'FETCH NEXT FROM item_cursor INTO @ProductId,@Barcode,@Quantity,@UnitPrice,@DiscountPercentage,@DiscountAmount,@TaxPercentage,@GstPricingType;';
IF CHARINDEX(@old, @definition) = 0
    THROW 65009, N'V65 could not locate the POS item cursor fetch.', 1;
SET @definition = REPLACE(@definition, @old, @new);

SET @old = N'SET @DiscountAmount=CASE WHEN ISNULL(@DiscountAmount,0)>0 THEN @DiscountAmount ELSE @Quantity*@UnitPrice*ISNULL(@DiscountPercentage,0)/100 END; SET @Taxable=@Quantity*@UnitPrice-@DiscountAmount; SET @TaxAmount=@Taxable*ISNULL(@TaxPercentage,0)/100; SET @LineTotal=ROUND(@Taxable+@TaxAmount,2);';
SET @new = N'SET @DiscountAmount=CASE WHEN ISNULL(@DiscountAmount,0)>0 THEN @DiscountAmount ELSE @Quantity*@UnitPrice*ISNULL(@DiscountPercentage,0)/100 END; SET @Taxable=CASE WHEN @GstPricingType=N''INCLUSIVE'' THEN ROUND((@Quantity*@UnitPrice-@DiscountAmount)*100/(100+ISNULL(@TaxPercentage,0)),2) ELSE @Quantity*@UnitPrice-@DiscountAmount END; SET @TaxAmount=CASE WHEN @GstPricingType=N''INCLUSIVE'' THEN ROUND((@Quantity*@UnitPrice-@DiscountAmount)-@Taxable,2) ELSE ROUND(@Taxable*ISNULL(@TaxPercentage,0)/100,2) END; SET @LineTotal=CASE WHEN @GstPricingType=N''INCLUSIVE'' THEN ROUND(@Quantity*@UnitPrice-@DiscountAmount,2) ELSE ROUND(@Taxable+@TaxAmount,2) END;';
IF CHARINDEX(@old, @definition) = 0
    THROW 65010, N'V65 could not locate the POS item tax calculation.', 1;
SET @definition = REPLACE(@definition, @old, @new);

EXEC sys.sp_executesql @definition;
