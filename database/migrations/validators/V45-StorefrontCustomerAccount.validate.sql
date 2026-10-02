SET NOCOUNT ON;
IF DB_NAME() NOT IN(N'WhatsBizERP_QA',N'WhatsBizERP_PROD') THROW 52300,N'Unexpected database for V45 validation.',1;
IF COL_LENGTH(N'sales.Customers',N'ProfileMediaId') IS NULL THROW 52301,N'V45 customer profile media column is missing.',1;
IF COL_LENGTH(N'sales.CustomerAddresses',N'RecipientName') IS NULL OR COL_LENGTH(N'sales.CustomerAddresses',N'Mobile') IS NULL OR COL_LENGTH(N'sales.CustomerAddresses',N'Landmark') IS NULL OR COL_LENGTH(N'sales.CustomerAddresses',N'IsDefault') IS NULL THROW 52302,N'V45 customer address columns are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'sales.CustomerAddresses') AND name=N'UX_CustomerAddresses_Default') THROW 52303,N'V45 default address index is missing.',1;
IF COL_LENGTH(N'commerce.StorefrontMedia',N'CustomerId') IS NULL OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StorefrontMedia') AND name=N'UX_StorefrontMedia_TenantCustomer') OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StorefrontMedia_Customer' AND parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia')) OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Customers_ProfileMedia' AND parent_object_id=OBJECT_ID(N'sales.Customers')) THROW 52304,N'V45 profile media ownership schema is missing.',1;
DECLARE @resourceDefinition nvarchar(max), @resourceDisabled bit, @resourceNotTrusted bit;
SELECT @resourceDefinition=definition,@resourceDisabled=is_disabled,@resourceNotTrusted=is_not_trusted
FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia') AND name=N'CK_StorefrontMedia_Resource';
IF @resourceDefinition IS NULL THROW 52305,N'V45 StorefrontMedia resource constraint is missing.',1;
IF @resourceDisabled<>0 OR @resourceNotTrusted<>0 THROW 52306,N'V45 StorefrontMedia resource constraint must be enabled and trusted.',1;
IF @resourceDefinition NOT LIKE N'%category-all%' OR @resourceDefinition NOT LIKE N'%customer-profile%' THROW 52307,N'V45 StorefrontMedia resource constraint does not preserve all supported resource types.',1;
SELECT N'V45_VALID' AS ValidationResult;
