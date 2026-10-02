SET NOCOUNT ON;
IF DB_NAME() NOT IN(N'WhatsBizERP_QA',N'WhatsBizERP_PROD')
    THROW 52120,N'Unexpected database for V42 validation.',1;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.TenantPaymentConfigurations') AND name=N'UpiEnabled' AND system_type_id=104 AND is_nullable=0)
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.TenantPaymentConfigurations') AND name=N'NetBankingEnabled' AND system_type_id=104 AND is_nullable=0)
    THROW 52121,N'V42 payment-method columns are missing or have unexpected definitions.',1;

IF NOT EXISTS(SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.TenantPaymentConfigurations') AND name=N'DF_TenantPaymentConfigurations_UpiEnabled' AND REPLACE(REPLACE(definition,N'(',N''),N')',N'')=N'1')
 OR NOT EXISTS(SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.TenantPaymentConfigurations') AND name=N'DF_TenantPaymentConfigurations_NetBankingEnabled' AND REPLACE(REPLACE(definition,N'(',N''),N')',N'')=N'1')
    THROW 52122,N'V42 default constraints are missing or incorrect.',1;

IF EXISTS(SELECT 1 FROM commerce.TenantPaymentConfigurations WHERE UpiEnabled IS NULL OR NetBankingEnabled IS NULL)
    THROW 52123,N'V42 tenant payment configuration contains NULL method flags.',1;

SELECT N'V42_VALID' AS ValidationResult;

