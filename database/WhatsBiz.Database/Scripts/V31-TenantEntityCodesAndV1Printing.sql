/* V31 - tenant-configurable entity code formats and V1 printing entitlement repair. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'sales.Customers') AND name=N'UX_Customers_Code')
    DROP INDEX UX_Customers_Code ON sales.Customers;
CREATE UNIQUE INDEX UX_Customers_Code ON sales.Customers(TenantId,CustomerCode) WHERE TenantId IS NOT NULL AND IsDeleted=0;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'master.Products') AND name=N'UX_Products_ProductCode')
    DROP INDEX UX_Products_ProductCode ON master.Products;
CREATE UNIQUE INDEX UX_Products_ProductCode ON master.Products(TenantId,ProductCode) WHERE TenantId IS NOT NULL AND IsDeleted=0;

DECLARE @Defaults TABLE(SettingKey nvarchar(100),SettingValue nvarchar(max),DataType nvarchar(50),Category nvarchar(100));
INSERT @Defaults VALUES
(N'CUSTOMER_CODE_PREFIX',N'CUS',N'STRING',N'Customer Code'),
(N'CUSTOMER_CODE_SEPARATOR',N'-',N'STRING',N'Customer Code'),
(N'CUSTOMER_CODE_PADDING',N'6',N'NUMBER',N'Customer Code'),
(N'CUSTOMER_CODE_NEXT_NUMBER',N'1',N'NUMBER',N'Internal'),
(N'SUPPLIER_CODE_PREFIX',N'SUP',N'STRING',N'Supplier Code'),
(N'SUPPLIER_CODE_SEPARATOR',N'-',N'STRING',N'Supplier Code'),
(N'SUPPLIER_CODE_PADDING',N'6',N'NUMBER',N'Supplier Code'),
(N'SUPPLIER_CODE_NEXT_NUMBER',N'1',N'NUMBER',N'Internal'),
(N'PRODUCT_CODE_PREFIX',N'PRD',N'STRING',N'Product Code'),
(N'PRODUCT_CODE_SEPARATOR',N'-',N'STRING',N'Product Code'),
(N'PRODUCT_CODE_PADDING',N'6',N'NUMBER',N'Product Code'),
(N'PRODUCT_CODE_NEXT_NUMBER',N'1',N'NUMBER',N'Internal');

INSERT admin.ApplicationSettings(CompanyId,SettingKey,SettingValue,DataType,Category,ModifiedBy)
SELECT c.CompanyId,d.SettingKey,d.SettingValue,d.DataType,d.Category,N'V31 tenant entity codes'
FROM admin.Companies c CROSS JOIN @Defaults d
WHERE c.IsActive=1 AND NOT EXISTS
 (SELECT 1 FROM admin.ApplicationSettings s WHERE s.CompanyId=c.CompanyId AND s.SettingKey=d.SettingKey);

UPDATE s SET SettingValue=CONVERT(nvarchar(30),x.NextNumber)
FROM admin.ApplicationSettings s JOIN admin.Companies c ON c.CompanyId=s.CompanyId
CROSS APPLY(SELECT COALESCE(MAX(TRY_CONVERT(bigint,SUBSTRING(CustomerCode,5,50))),0)+1 NextNumber FROM sales.Customers WHERE TenantId=c.TenantId AND CustomerCode LIKE N'CUS-%' AND IsDeleted=0)x
WHERE s.SettingKey=N'CUSTOMER_CODE_NEXT_NUMBER' AND TRY_CONVERT(bigint,s.SettingValue)<=x.NextNumber;
UPDATE s SET SettingValue=CONVERT(nvarchar(30),x.NextNumber)
FROM admin.ApplicationSettings s JOIN admin.Companies c ON c.CompanyId=s.CompanyId
CROSS APPLY(SELECT COALESCE(MAX(TRY_CONVERT(bigint,SUBSTRING(SupplierCode,5,50))),0)+1 NextNumber FROM purchase.Suppliers WHERE TenantId=c.TenantId AND SupplierCode LIKE N'SUP-%' AND IsDeleted=0)x
WHERE s.SettingKey=N'SUPPLIER_CODE_NEXT_NUMBER' AND TRY_CONVERT(bigint,s.SettingValue)<=x.NextNumber;
UPDATE s SET SettingValue=CONVERT(nvarchar(30),x.NextNumber)
FROM admin.ApplicationSettings s JOIN admin.Companies c ON c.CompanyId=s.CompanyId
CROSS APPLY(SELECT COALESCE(MAX(TRY_CONVERT(bigint,SUBSTRING(ProductCode,5,50))),0)+1 NextNumber FROM master.Products WHERE TenantId=c.TenantId AND ProductCode LIKE N'PRD-%' AND IsDeleted=0)x
WHERE s.SettingKey=N'PRODUCT_CODE_NEXT_NUMBER' AND TRY_CONVERT(bigint,s.SettingValue)<=x.NextNumber;

DECLARE @printing uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'PRINTING');
DECLARE @v1 uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'V1');
DECLARE @v1Plan uniqueidentifier=(SELECT PlanId FROM core.Plans WHERE PlanKey=N'V1_DEFAULT');
IF @printing IS NOT NULL
BEGIN
    UPDATE core.Features SET Version=N'V1',ParentFeatureId=@v1,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'V31 V1 printing entitlement' WHERE FeatureId=@printing;
    IF @v1Plan IS NOT NULL
    BEGIN
        UPDATE core.PlanFeatures SET IsEnabled=1,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'V31 V1 printing entitlement' WHERE PlanId=@v1Plan AND FeatureId=@printing;
        IF @@ROWCOUNT=0 INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy) VALUES(NEWID(),@v1Plan,@printing,1,N'V31 V1 printing entitlement');
        UPDATE tf SET IsEnabled=1,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'V31 V1 printing entitlement'
        FROM core.TenantFeatures tf
        WHERE tf.FeatureId=@printing AND tf.IsActive=1 AND tf.Reason=N'Initialized from active subscription plan'
          AND EXISTS(SELECT 1 FROM core.Subscriptions s WHERE s.TenantId=tf.TenantId AND s.PlanId=@v1Plan AND s.IsActive=1 AND s.StartDate<=SYSUTCDATETIME() AND (s.EndDate IS NULL OR s.EndDate>=SYSUTCDATETIME()));
    END;
END;
COMMIT TRANSACTION;
