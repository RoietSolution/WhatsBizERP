/*
  Idempotent QA bootstrap. This is deliberately not part of PostDeployment.sql so a
  database publish can never create QA business data in another environment.

  Prerequisite: publish WhatsBiz.Database.dacpac to WhatsBizERP_QA first.
  Identity users are intentionally excluded; the API IdentityBootstrap configuration
  creates/resets the administrator with ASP.NET Core Identity's UserManager.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'WhatsBizERP_QA'
    THROW 51250, 'Bootstrap_QA.sql may run only in database WhatsBizERP_QA.', 1;

IF OBJECT_ID(N'core.Tenants', N'U') IS NULL
   OR OBJECT_ID(N'core.Plans', N'U') IS NULL
   OR OBJECT_ID(N'core.Features', N'U') IS NULL
   OR OBJECT_ID(N'core.PlanFeatures', N'U') IS NULL
   OR OBJECT_ID(N'core.Subscriptions', N'U') IS NULL
   OR OBJECT_ID(N'core.TenantFeatures', N'U') IS NULL
    THROW 51251, 'Current platform/subscription schema is missing. Publish the database project first.', 1;

DECLARE @TenantKey nvarchar(100)=N'QA_DEFAULT';
DECLARE @TenantName nvarchar(200)=N'KhataDhari QA Retailer';
DECLARE @PlanKey nvarchar(100)=N'V2_COMMERCE';
DECLARE @Actor nvarchar(256)=N'database-bootstrap:qa';
DECLARE @TenantId uniqueidentifier;
DECLARE @PlanId uniqueidentifier=(SELECT PlanId FROM core.Plans WHERE PlanKey=@PlanKey AND IsActive=1);

IF @PlanId IS NULL THROW 51252, 'Active V2_COMMERCE plan is missing.', 1;
IF NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'V1' AND FeatureType=N'VERSION' AND IsActive=1)
   OR NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'V2' AND FeatureType=N'VERSION' AND IsActive=1)
    THROW 51253, 'Current V1/V2 hierarchy is missing.', 1;
IF EXISTS
(
    SELECT 1 FROM core.Features f
    LEFT JOIN core.PlanFeatures pf ON pf.PlanId=@PlanId AND pf.FeatureId=f.FeatureId
    WHERE f.IsActive=1 AND pf.PlanFeatureId IS NULL
)
    THROW 51254, 'V2_COMMERCE has no entitlement row for one or more active features.', 1;
IF EXISTS
(
    SELECT 1 FROM core.Features f JOIN core.PlanFeatures pf ON pf.FeatureId=f.FeatureId AND pf.PlanId=@PlanId
    WHERE f.FeatureKey IN(N'V1',N'V2',N'WHATSAPP_COMMERCE',N'PRODUCTS',N'INVENTORY',N'CUSTOMERS',N'POS') AND pf.IsEnabled=0
)
    THROW 51255, 'V2_COMMERCE does not entitle a required V1/WhatsApp Commerce dependency.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock @Resource=N'WhatsBiz:Bootstrap:QA_DEFAULT',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
    IF @LockResult<0 THROW 51256, 'Could not acquire the QA bootstrap lock.', 1;

    SELECT @TenantId=TenantId FROM core.Tenants WITH(UPDLOCK,HOLDLOCK) WHERE TenantKey=@TenantKey;
    IF @TenantId IS NULL
    BEGIN
        SET @TenantId='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1';
        IF EXISTS(SELECT 1 FROM core.Tenants WHERE TenantId=@TenantId) SET @TenantId=NEWID();
        INSERT core.Tenants(TenantId,TenantKey,Name,IsActive,CreatedBy)
        VALUES(@TenantId,@TenantKey,@TenantName,1,@Actor);
    END
    ELSE
        UPDATE core.Tenants SET Name=@TenantName,IsActive=1,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@Actor WHERE TenantId=@TenantId;

    DECLARE @SubscriptionId uniqueidentifier=(SELECT TOP(1) SubscriptionId FROM core.Subscriptions WHERE TenantId=@TenantId AND PlanId=@PlanId ORDER BY IsActive DESC,CreatedOn,SubscriptionId);
    IF @SubscriptionId IS NULL
    BEGIN
        SET @SubscriptionId=NEWID();
        INSERT core.Subscriptions(SubscriptionId,TenantId,PlanId,StartDate,EndDate,IsActive,CreatedBy)
        VALUES(@SubscriptionId,@TenantId,@PlanId,SYSUTCDATETIME(),NULL,1,@Actor);
    END
    UPDATE core.Subscriptions SET IsActive=0,EndDate=COALESCE(EndDate,SYSUTCDATETIME()),
        ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@Actor
    WHERE TenantId=@TenantId AND IsActive=1 AND SubscriptionId<>@SubscriptionId;
    UPDATE core.Subscriptions SET IsActive=1,EndDate=NULL,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@Actor
    WHERE SubscriptionId=@SubscriptionId;

    MERGE core.TenantFeatures WITH(HOLDLOCK) AS target
    USING
    (
        SELECT @TenantId TenantId,f.FeatureId,pf.IsEnabled
        FROM core.Features f JOIN core.PlanFeatures pf ON pf.FeatureId=f.FeatureId AND pf.PlanId=@PlanId
        WHERE f.IsActive=1
    ) source
    ON target.TenantId=source.TenantId AND target.FeatureId=source.FeatureId
    WHEN MATCHED THEN UPDATE SET IsEnabled=source.IsEnabled,IsActive=1,StartDate=NULL,EndDate=NULL,
        Reason=N'Initialized from QA plan '+@PlanKey,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@Actor
    WHEN NOT MATCHED THEN INSERT(TenantFeatureId,TenantId,FeatureId,IsEnabled,StartDate,EndDate,Reason,IsActive,CreatedBy)
        VALUES(NEWID(),source.TenantId,source.FeatureId,source.IsEnabled,NULL,NULL,N'Initialized from QA plan '+@PlanKey,1,@Actor);

    /* Required company/reference configuration (these V1 tables are currently database-scoped). */
    DECLARE @CompanyId uniqueidentifier=COALESCE((SELECT CompanyId FROM admin.Companies WHERE CompanyCode=N'QA'),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2'));
    IF NOT EXISTS(SELECT 1 FROM admin.Companies WHERE CompanyId=@CompanyId)
        INSERT admin.Companies(CompanyId,CompanyCode,CompanyName,LegalName,City,State,StateCode,Country,IsActive,CreatedOn)
        VALUES(@CompanyId,N'QA',@TenantName,@TenantName,N'Pune',N'Maharashtra','27',N'India',1,SYSUTCDATETIME());
    ELSE UPDATE admin.Companies SET CompanyName=@TenantName,LegalName=@TenantName,IsActive=1,ModifiedOn=SYSUTCDATETIME() WHERE CompanyId=@CompanyId;

    MERGE admin.Currencies AS t USING(VALUES(CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3'),'INR',N'Indian Rupee',N'₹',CONVERT(tinyint,2),CONVERT(bit,1),CONVERT(bit,1)))
      s(CurrencyId,CurrencyCode,CurrencyName,Symbol,DecimalPlaces,IsDefault,IsActive) ON t.CurrencyCode=s.CurrencyCode
    WHEN MATCHED THEN UPDATE SET CurrencyName=s.CurrencyName,Symbol=s.Symbol,DecimalPlaces=s.DecimalPlaces,IsDefault=s.IsDefault,IsActive=s.IsActive
    WHEN NOT MATCHED THEN INSERT(CurrencyId,CurrencyCode,CurrencyName,Symbol,DecimalPlaces,IsDefault,IsActive) VALUES(s.CurrencyId,s.CurrencyCode,s.CurrencyName,s.Symbol,s.DecimalPlaces,s.IsDefault,s.IsActive);

    DECLARE @FyStartYear int=CASE WHEN MONTH(GETDATE())>=4 THEN YEAR(GETDATE()) ELSE YEAR(GETDATE())-1 END;
    DECLARE @FyCode varchar(9)=CONCAT(@FyStartYear,'-',RIGHT(CONVERT(varchar(4),@FyStartYear+1),2));
    MERGE admin.FinancialYears AS t USING(SELECT @CompanyId CompanyId,@FyCode YearCode,DATEFROMPARTS(@FyStartYear,4,1) StartDate,DATEFROMPARTS(@FyStartYear+1,3,31) EndDate) s
      ON t.CompanyId=s.CompanyId AND t.YearCode=s.YearCode
    WHEN MATCHED THEN UPDATE SET StartDate=s.StartDate,EndDate=s.EndDate,Status='OPEN',IsDefault=1
    WHEN NOT MATCHED THEN INSERT(FinancialYearId,CompanyId,YearCode,StartDate,EndDate,Status,IsDefault,CreatedOn) VALUES(NEWID(),s.CompanyId,s.YearCode,s.StartDate,s.EndDate,'OPEN',1,SYSUTCDATETIME());

    MERGE gst.GSTRates AS t USING(VALUES(CONVERT(decimal(5,2),0)),(5),(12),(18),(28)) s(Rate) ON t.Rate=s.Rate
    WHEN MATCHED THEN UPDATE SET CessRate=0,EffectiveTo=NULL,IsActive=1
    WHEN NOT MATCHED THEN INSERT(GSTRateId,Rate,CessRate,EffectiveFrom,IsActive) VALUES(NEWID(),s.Rate,0,'2017-07-01',1);
    MERGE gst.TaxConfiguration AS t USING(VALUES
      (N'GST0',N'GST 0%',CONVERT(decimal(5,2),0)),(N'GST5',N'GST 5%',5),(N'GST12',N'GST 12%',12),(N'GST18',N'GST 18%',18),(N'GST28',N'GST 28%',28)) s(TaxCode,TaxName,Rate)
      ON t.TaxCode=s.TaxCode
    WHEN MATCHED THEN UPDATE SET TaxName=s.TaxName,TaxType=N'GST',Rate=s.Rate,IsReverseCharge=0,IsCess=0,EffectiveTo=NULL,IsActive=1
    WHEN NOT MATCHED THEN INSERT(TaxConfigurationId,TaxCode,TaxName,TaxType,Rate,IsReverseCharge,IsCess,EffectiveFrom,IsActive,CreatedOn) VALUES(NEWID(),s.TaxCode,s.TaxName,N'GST',s.Rate,0,0,'2017-07-01',1,SYSUTCDATETIME());
    IF NOT EXISTS(SELECT 1 FROM gst.GSTSettings WHERE IsActive=1)
        INSERT gst.GSTSettings(GSTSettingsId,CompanyGSTIN,StateCode,RegistrationType,IsCompositionScheme,EffectiveDate,LegalName,TradeName,IsActive,CreatedOn,CreatedBy)
        VALUES(NEWID(),NULL,'27',N'UNREGISTERED',0,CONVERT(date,SYSUTCDATETIME()),@TenantName,@TenantName,1,SYSUTCDATETIME(),@Actor);

    DECLARE @Groups TABLE(GroupCode nvarchar(30),GroupName nvarchar(100),Nature nvarchar(20));
    INSERT @Groups VALUES(N'ASSET',N'Assets',N'ASSET'),(N'LIABILITY',N'Liabilities',N'LIABILITY'),(N'INCOME',N'Income',N'INCOME'),(N'EXPENSE',N'Expenses',N'EXPENSE');
    MERGE finance.AccountGroups AS t USING @Groups s ON t.GroupCode=s.GroupCode
    WHEN MATCHED THEN UPDATE SET GroupName=s.GroupName,Nature=s.Nature,IsActive=1
    WHEN NOT MATCHED THEN INSERT(AccountGroupId,GroupCode,GroupName,ParentGroupId,Nature,IsActive) VALUES(NEWID(),s.GroupCode,s.GroupName,NULL,s.Nature,1);

    DECLARE @Accounts TABLE(AccountCode nvarchar(30),AccountName nvarchar(150),GroupCode nvarchar(30));
    INSERT @Accounts VALUES
      (N'CASH',N'Cash',N'ASSET'),(N'BANK',N'Bank',N'ASSET'),(N'CUSTOMER',N'Customer Receivables',N'ASSET'),
      (N'SUPPLIER',N'Supplier Payables',N'LIABILITY'),(N'INVENTORY',N'Inventory',N'ASSET'),
      (N'INPUT_GST',N'Input GST',N'ASSET'),(N'OUTPUT_GST',N'Output GST',N'LIABILITY'),
      (N'SALES',N'Sales',N'INCOME'),(N'PURCHASE_RETURN',N'Purchase Returns',N'INCOME'),
      (N'SALES_RETURN',N'Sales Returns',N'EXPENSE'),(N'STOCK_ADJUST',N'Stock Adjustments',N'EXPENSE');
    MERGE finance.Accounts AS t USING(SELECT a.AccountCode,a.AccountName,g.AccountGroupId FROM @Accounts a JOIN finance.AccountGroups g ON g.GroupCode=a.GroupCode) s ON t.AccountCode=s.AccountCode
    WHEN MATCHED THEN UPDATE SET AccountName=s.AccountName,AccountGroupId=s.AccountGroupId,IsSystem=1,IsActive=1
    WHEN NOT MATCHED THEN INSERT(AccountId,AccountCode,AccountName,AccountGroupId,OpeningBalance,IsSystem,IsActive,CreatedOn) VALUES(NEWID(),s.AccountCode,s.AccountName,s.AccountGroupId,0,1,1,SYSUTCDATETIME());

    MERGE finance.PaymentModes AS t USING(VALUES(N'CASH',N'Cash',N'CASH'),(N'UPI',N'UPI',N'BANK'),(N'CARD',N'Card',N'BANK'),(N'BANK',N'Bank Transfer',N'BANK'),(N'WALLET',N'Wallet',N'BANK'),(N'CREDIT',N'Credit',N'CREDIT')) s(ModeCode,ModeName,BookType) ON t.ModeCode=s.ModeCode
    WHEN MATCHED THEN UPDATE SET ModeName=s.ModeName,BookType=s.BookType,IsActive=1
    WHEN NOT MATCHED THEN INSERT(PaymentModeId,ModeCode,ModeName,BookType,IsActive) VALUES(NEWID(),s.ModeCode,s.ModeName,s.BookType,1);

    DECLARE @WarehouseTypeId uniqueidentifier=(SELECT WarehouseTypeId FROM inventory.WarehouseTypes WHERE TypeCode=N'GENERAL' AND IsActive=1);
    IF @WarehouseTypeId IS NULL THROW 51257, 'GENERAL warehouse type seed is missing.', 1;
    DECLARE @WarehouseId uniqueidentifier=COALESCE((SELECT WarehouseId FROM inventory.Warehouses WHERE WarehouseCode=N'QA-MAIN' AND IsDeleted=0),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4'));
    IF NOT EXISTS(SELECT 1 FROM inventory.Warehouses WHERE WarehouseId=@WarehouseId)
        INSERT inventory.Warehouses(WarehouseId,WarehouseCode,WarehouseName,WarehouseTypeId,Capacity,IsDefault,IsActive,IsDeleted,CreatedBy)
        VALUES(@WarehouseId,N'QA-MAIN',N'QA Main Warehouse',@WarehouseTypeId,10000,1,1,0,@Actor);
    ELSE UPDATE inventory.Warehouses SET WarehouseName=N'QA Main Warehouse',WarehouseTypeId=@WarehouseTypeId,IsDefault=1,IsActive=1,IsDeleted=0,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@Actor WHERE WarehouseId=@WarehouseId;

    DECLARE @BranchId uniqueidentifier=COALESCE((SELECT BranchId FROM admin.Branches WHERE CompanyId=@CompanyId AND BranchCode=N'QA-MAIN'),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5'));
    IF NOT EXISTS(SELECT 1 FROM admin.Branches WHERE BranchId=@BranchId)
        INSERT admin.Branches(BranchId,CompanyId,BranchCode,BranchName,DefaultWarehouseId,City,State,PostalCode,IsDefault,IsActive,CreatedOn)
        VALUES(@BranchId,@CompanyId,N'QA-MAIN',N'QA Main Branch',@WarehouseId,N'Pune',N'Maharashtra',NULL,1,1,SYSUTCDATETIME());
    ELSE UPDATE admin.Branches SET BranchName=N'QA Main Branch',DefaultWarehouseId=@WarehouseId,IsDefault=1,IsActive=1 WHERE BranchId=@BranchId;
    UPDATE inventory.Warehouses SET BranchId=@BranchId WHERE WarehouseId=@WarehouseId;

    DECLARE @CustomerTerm uniqueidentifier=(SELECT PaymentTermId FROM sales.CustomerPaymentTerms WHERE PaymentTermCode=N'IMMEDIATE');
    IF NOT EXISTS(SELECT 1 FROM sales.Customers WHERE CustomerCode=N'QA-CUST-001' AND IsDeleted=0)
        INSERT sales.Customers(CustomerCode,CustomerName,CustomerType,Currency,PaymentTermId,CreditLimit,OpeningBalance,IsGSTRegistered,IsActive,IsDeleted,Remarks,CreatedBy,TenantId)
        VALUES(N'QA-CUST-001',N'QA Walk-in Customer',N'RETAIL',N'INR',@CustomerTerm,0,0,0,1,0,N'QA bootstrap customer',@Actor,@TenantId);
    ELSE UPDATE sales.Customers SET TenantId=@TenantId,IsActive=1,IsDeleted=0 WHERE CustomerCode=N'QA-CUST-001';
    DECLARE @SupplierTerm uniqueidentifier=(SELECT PaymentTermId FROM purchase.SupplierPaymentTerms WHERE PaymentTermCode=N'NET30');
    IF NOT EXISTS(SELECT 1 FROM purchase.Suppliers WHERE SupplierCode=N'QA-SUP-001' AND IsDeleted=0)
        INSERT purchase.Suppliers(SupplierCode,SupplierName,SupplierType,Currency,PaymentTermId,CreditLimit,OpeningBalance,IsGSTRegistered,IsTDSApplicable,IsActive,IsDeleted,Remarks,CreatedBy)
        VALUES(N'QA-SUP-001',N'QA Test Supplier',N'LOCAL',N'INR',@SupplierTerm,0,0,0,0,1,0,N'QA bootstrap supplier',@Actor);

    DECLARE @CategoryId uniqueidentifier=COALESCE((SELECT ProductCategoryId FROM master.ProductCategories WHERE CategoryCode=N'QA-CATALOG' AND IsDeleted=0),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6'));
    IF NOT EXISTS(SELECT 1 FROM master.ProductCategories WHERE ProductCategoryId=@CategoryId)
        INSERT master.ProductCategories(ProductCategoryId,CategoryCode,CategoryName,Description,DisplayOrder,ParentCategoryId,CreatedOn,CreatedBy,IsActive,IsDeleted) VALUES(@CategoryId,N'QA-CATALOG',N'QA Catalogue',N'QA bootstrap products',1,NULL,SYSUTCDATETIME(),@Actor,1,0);
    DECLARE @BrandId uniqueidentifier=COALESCE((SELECT BrandId FROM master.Brands WHERE BrandCode=N'QA-BRAND' AND IsDeleted=0),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7'));
    IF NOT EXISTS(SELECT 1 FROM master.Brands WHERE BrandId=@BrandId)
        INSERT master.Brands(BrandId,BrandCode,BrandName,Description,CreatedOn,CreatedBy,IsActive,IsDeleted) VALUES(@BrandId,N'QA-BRAND',N'QA Brand',N'QA bootstrap brand',SYSUTCDATETIME(),@Actor,1,0);
    DECLARE @UnitId uniqueidentifier=COALESCE((SELECT UnitId FROM master.UnitsOfMeasure WHERE UnitCode=N'EA' AND IsDeleted=0),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa8'));
    IF NOT EXISTS(SELECT 1 FROM master.UnitsOfMeasure WHERE UnitId=@UnitId)
        INSERT master.UnitsOfMeasure(UnitId,UnitCode,UnitName,ShortName,DecimalPlaces,CreatedOn,CreatedBy,IsActive,IsDeleted) VALUES(@UnitId,N'EA',N'Each',N'ea',0,SYSUTCDATETIME(),@Actor,1,0);

    DECLARE @ProductId uniqueidentifier=COALESCE((SELECT ProductId FROM master.Products WHERE ProductCode=N'QA-PROD-001' AND IsDeleted=0),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa9'));
    IF NOT EXISTS(SELECT 1 FROM master.Products WHERE ProductId=@ProductId)
        INSERT master.Products(ProductId,ProductCode,Barcode,BarcodeType,ProductName,ShortDescription,CategoryId,BrandId,UnitId,HSNCode,GSTPercentage,PurchasePrice,SellingPrice,MRP,MinimumStock,MaximumStock,ReorderLevel,IsBatchManaged,IsSerialManaged,CreatedOn,CreatedBy,IsActive,IsDeleted,TenantId)
        VALUES(@ProductId,N'QA-PROD-001',N'8900000000001',N'EAN13',N'QA Test Product',N'Commerce/POS bootstrap product',@CategoryId,@BrandId,@UnitId,N'9999',18,80,100,100,5,500,10,0,0,SYSUTCDATETIME(),@Actor,1,0,@TenantId);
    ELSE UPDATE master.Products SET TenantId=@TenantId,CategoryId=@CategoryId,BrandId=@BrandId,UnitId=@UnitId,IsActive=1,IsDeleted=0,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@Actor WHERE ProductId=@ProductId;

    IF NOT EXISTS(SELECT 1 FROM inventory.InventoryBalances WHERE ProductId=@ProductId AND WarehouseId=@WarehouseId AND ZoneId IS NULL AND BinId IS NULL AND BatchNo IS NULL AND SerialNo IS NULL)
        INSERT inventory.InventoryBalances(ProductId,WarehouseId,QuantityOnHand,QuantityReserved,AverageCost,LastPurchaseCost,CreatedBy) VALUES(@ProductId,@WarehouseId,100,0,80,80,@Actor);

    DECLARE @InvoiceSeriesId uniqueidentifier=(SELECT TOP(1) InvoiceSeriesId FROM sales.InvoiceSeries WHERE IsDefault=1 AND IsActive=1 ORDER BY FinancialYear DESC);
    IF @InvoiceSeriesId IS NULL THROW 51258, 'Default POS invoice series seed is missing.', 1;
    IF NOT EXISTS(SELECT 1 FROM sales.POSCounters WHERE CounterCode=N'QA-POS-01')
        INSERT sales.POSCounters(CounterCode,CounterName,WarehouseId,InvoiceSeriesId,IsActive) VALUES(N'QA-POS-01',N'QA POS Counter',@WarehouseId,@InvoiceSeriesId,1);
    ELSE UPDATE sales.POSCounters SET CounterName=N'QA POS Counter',WarehouseId=@WarehouseId,InvoiceSeriesId=@InvoiceSeriesId,IsActive=1 WHERE CounterCode=N'QA-POS-01';

    DECLARE @CollectionId uniqueidentifier=COALESCE((SELECT CollectionId FROM commerce.Collections WHERE TenantId=@TenantId AND Slug=N'qa-featured' AND IsDeleted=0),CONVERT(uniqueidentifier,'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa10'));
    IF NOT EXISTS(SELECT 1 FROM commerce.Collections WHERE CollectionId=@CollectionId)
        INSERT commerce.Collections(CollectionId,TenantId,Name,Slug,Description,IsActive,DisplayOrder,CreatedOn,CreatedBy,IsDeleted) VALUES(@CollectionId,@TenantId,N'QA Featured',N'qa-featured',N'QA commerce bootstrap collection',1,1,SYSUTCDATETIME(),@Actor,0);
    IF NOT EXISTS(SELECT 1 FROM commerce.CollectionProducts WHERE TenantId=@TenantId AND CollectionId=@CollectionId AND ProductId=@ProductId)
        INSERT commerce.CollectionProducts(CollectionProductId,TenantId,CollectionId,ProductId,DisplayOrder,CreatedOn,CreatedBy) VALUES(NEWID(),@TenantId,@CollectionId,@ProductId,1,SYSUTCDATETIME(),@Actor);

    /* MOCK needs no credential and does not bypass feature/subscription checks. Preserve any later tenant configuration. */
    IF NOT EXISTS(SELECT 1 FROM integration.WhatsAppConfigurations WHERE TenantId=@TenantId)
        INSERT integration.WhatsAppConfigurations(WhatsAppConfigurationId,TenantId,ProviderMode,IsEnabled,ConnectionStatus,CreatedBy)
        VALUES(NEWID(),@TenantId,N'MOCK',1,N'CONFIGURED',@Actor);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT t.TenantId,t.TenantKey,t.Name,p.PlanKey,s.StartDate,s.EndDate,s.IsActive SubscriptionActive
FROM core.Tenants t JOIN core.Subscriptions s ON s.TenantId=t.TenantId JOIN core.Plans p ON p.PlanId=s.PlanId
WHERE t.TenantKey=@TenantKey AND s.IsActive=1;
PRINT 'QA bootstrap completed. Start the API once with IdentityBootstrap administrator settings to create the hashed Identity user.';
