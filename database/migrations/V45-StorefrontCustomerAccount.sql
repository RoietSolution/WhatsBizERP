/* V45 - authenticated Storefront customer addresses and profile media. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'sales.Customers', N'ProfileMediaId') IS NULL
    ALTER TABLE sales.Customers ADD ProfileMediaId uniqueidentifier NULL;

IF COL_LENGTH(N'sales.CustomerAddresses', N'RecipientName') IS NULL
BEGIN
    ALTER TABLE sales.CustomerAddresses ADD RecipientName nvarchar(250) NOT NULL CONSTRAINT DF_CustomerAddresses_RecipientName DEFAULT N'';
    ALTER TABLE sales.CustomerAddresses ADD Mobile nvarchar(18) NOT NULL CONSTRAINT DF_CustomerAddresses_Mobile DEFAULT N'';
    ALTER TABLE sales.CustomerAddresses ADD Landmark nvarchar(250) NULL;
    ALTER TABLE sales.CustomerAddresses ADD IsDefault bit NOT NULL CONSTRAINT DF_CustomerAddresses_IsDefault DEFAULT(0);
END;
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'sales.CustomerAddresses') AND name=N'CK_CustomerAddresses_Type')
    ALTER TABLE sales.CustomerAddresses DROP CONSTRAINT CK_CustomerAddresses_Type;
ALTER TABLE sales.CustomerAddresses WITH CHECK ADD CONSTRAINT CK_CustomerAddresses_Type CHECK(AddressType IN(N'Shipping',N'Billing',N'Home',N'Work',N'Other'));

;WITH ranked AS (SELECT AddressId,ROW_NUMBER() OVER(PARTITION BY CustomerId ORDER BY AddressId) rn FROM sales.CustomerAddresses WHERE IsDefault=1)
UPDATE a SET IsDefault=0 FROM sales.CustomerAddresses a JOIN ranked r ON r.AddressId=a.AddressId WHERE r.rn>1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'sales.CustomerAddresses') AND name=N'UX_CustomerAddresses_Default')
    CREATE UNIQUE INDEX UX_CustomerAddresses_Default ON sales.CustomerAddresses(CustomerId) WHERE IsDefault=1;

IF COL_LENGTH(N'commerce.StorefrontMedia', N'CustomerId') IS NULL
    ALTER TABLE commerce.StorefrontMedia ADD CustomerId uniqueidentifier NULL;
GO
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia') AND name=N'CK_StorefrontMedia_Resource')
    ALTER TABLE commerce.StorefrontMedia DROP CONSTRAINT CK_StorefrontMedia_Resource;
ALTER TABLE commerce.StorefrontMedia WITH CHECK ADD CONSTRAINT CK_StorefrontMedia_Resource CHECK(ResourceType IN(N'logo',N'category',N'category-all',N'banner-primary',N'banner-secondary',N'customer-profile'));
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StorefrontMedia') AND name=N'UX_StorefrontMedia_TenantCustomer')
    CREATE UNIQUE INDEX UX_StorefrontMedia_TenantCustomer ON commerce.StorefrontMedia(TenantId,CustomerId) WHERE CustomerId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StorefrontMedia_Customer' AND parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia'))
    ALTER TABLE commerce.StorefrontMedia WITH CHECK ADD CONSTRAINT FK_StorefrontMedia_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Customers_ProfileMedia' AND parent_object_id=OBJECT_ID(N'sales.Customers'))
    ALTER TABLE sales.Customers WITH CHECK ADD CONSTRAINT FK_Customers_ProfileMedia FOREIGN KEY(ProfileMediaId) REFERENCES commerce.StorefrontMedia(MediaId);
GO
