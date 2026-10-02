SET NOCOUNT ON;
IF DB_NAME() NOT IN(N'WhatsBizERP_QA',N'WhatsBizERP_PROD') THROW 52210,N'Unexpected database for V44 validation.',1;
IF OBJECT_ID(N'sales.POS_PostInvoice',N'P') IS NULL THROW 52211,N'sales.POS_PostInvoice is missing.',1;
IF (SELECT COUNT(*) FROM sys.parameters WHERE object_id=OBJECT_ID(N'sales.POS_PostInvoice'))<>22 THROW 52212,N'V44 POS invoice parameter count is incorrect.',1;
DECLARE @MismatchMessage nvarchar(2048);
;WITH Expected AS
(
    SELECT * FROM (VALUES
      (1,N'@CounterId',N'uniqueidentifier',16,0,0,0),(2,N'@ShiftId',N'uniqueidentifier',16,0,0,0),(3,N'@CustomerId',N'uniqueidentifier',16,0,0,0),(4,N'@WarehouseId',N'uniqueidentifier',16,0,0,0),(5,N'@SalesPersonId',N'uniqueidentifier',16,0,0,0),(6,N'@ItemsJson',N'nvarchar',-1,0,0,0),(7,N'@PaymentsJson',N'nvarchar',-1,0,0,0),(8,N'@BillDiscount',N'decimal',9,18,2,0),(9,N'@RoundOff',N'decimal',9,18,2,0),(10,N'@Remarks',N'nvarchar',2000,0,0,0),(11,N'@Status',N'nvarchar',40,0,0,0),(12,N'@InterState',N'bit',1,1,0,0),(13,N'@DiscountAuthorizedBy',N'nvarchar',512,0,0,0),(14,N'@CreatedBy',N'nvarchar',512,0,0,0),(15,N'@TenantId',N'uniqueidentifier',16,0,0,0),(16,N'@DeliveryCharge',N'decimal',9,18,2,0),(17,N'@PromotionDiscountAmount',N'decimal',9,18,2,0),(18,N'@AppliedPromotionId',N'uniqueidentifier',16,0,0,0),(19,N'@AppliedPromotionName',N'nvarchar',300,0,0,0),(20,N'@FreeDeliveryApplied',N'bit',1,1,0,0),(21,N'@FreeDeliveryThresholdSnapshot',N'decimal',9,18,2,0),(22,N'@ServicePincode',N'varchar',6,0,0,0)
    ) v(Ordinal,ParameterName,TypeName,MaxLength,PrecisionValue,ScaleValue,IsOutput)
), Mismatches AS
(
    SELECT CONCAT(N'ordinal=',e.Ordinal,N' expected=',e.ParameterName,N' ',e.TypeName,N'(',e.MaxLength,N',',e.PrecisionValue,N',',e.ScaleValue,N',out=',e.IsOutput,N') actual=',COALESCE(p.name,N'<missing>'),N' ',COALESCE(t.name,N'<missing>'),N'(',COALESCE(CONVERT(nvarchar(20),p.max_length),N'<missing>'),N',',COALESCE(CONVERT(nvarchar(20),p.precision),N'<missing>'),N',',COALESCE(CONVERT(nvarchar(20),p.scale),N'<missing>'),N',out=',COALESCE(CONVERT(nvarchar(20),p.is_output),N'<missing>'),N')') AS Detail
    FROM Expected e
    LEFT JOIN sys.parameters p ON p.object_id=OBJECT_ID(N'sales.POS_PostInvoice') AND p.parameter_id=e.Ordinal
    LEFT JOIN sys.types t ON t.user_type_id=p.user_type_id
    WHERE p.parameter_id IS NULL OR p.name<>e.ParameterName OR t.name<>e.TypeName OR p.max_length<>e.MaxLength OR p.precision<>e.PrecisionValue OR p.scale<>e.ScaleValue OR CONVERT(bit,p.is_output)<>e.IsOutput
)
SELECT @MismatchMessage=LEFT(STRING_AGG(CONVERT(nvarchar(max),Detail),N'; '),2048) FROM Mismatches;
IF @MismatchMessage IS NOT NULL
    THROW 52213,@MismatchMessage,1;
SELECT N'V44_VALID' AS ValidationResult;