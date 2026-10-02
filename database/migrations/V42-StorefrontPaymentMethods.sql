SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.TenantPaymentConfigurations',N'U') IS NULL
    THROW 52042, N'Tenant payment configuration must be installed before V42.', 1;

IF COL_LENGTH(N'commerce.TenantPaymentConfigurations', N'UpiEnabled') IS NULL
    ALTER TABLE commerce.TenantPaymentConfigurations ADD UpiEnabled bit NOT NULL
        CONSTRAINT DF_TenantPaymentConfigurations_UpiEnabled DEFAULT(1) WITH VALUES;

IF COL_LENGTH(N'commerce.TenantPaymentConfigurations', N'NetBankingEnabled') IS NULL
    ALTER TABLE commerce.TenantPaymentConfigurations ADD NetBankingEnabled bit NOT NULL
        CONSTRAINT DF_TenantPaymentConfigurations_NetBankingEnabled DEFAULT(1) WITH VALUES;
GO
