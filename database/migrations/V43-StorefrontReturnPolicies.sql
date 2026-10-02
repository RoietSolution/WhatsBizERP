SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'DefaultReturnWindowDays') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD DefaultReturnWindowDays int NOT NULL
        CONSTRAINT DF_StorefrontConfigurations_DefaultReturnWindowDays DEFAULT (7) WITH VALUES;
IF COL_LENGTH(N'master.Products', N'ReturnPolicyMode') IS NULL
    ALTER TABLE master.Products ADD ReturnPolicyMode varchar(20) NOT NULL
        CONSTRAINT DF_Products_ReturnPolicyMode DEFAULT ('INHERIT_DEFAULT') WITH VALUES;
IF COL_LENGTH(N'master.Products', N'ReturnWindowDays') IS NULL
    ALTER TABLE master.Products ADD ReturnWindowDays int NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Products_ReturnPolicyMode')
    ALTER TABLE master.Products ADD CONSTRAINT CK_Products_ReturnPolicyMode CHECK (ReturnPolicyMode IN ('INHERIT_DEFAULT','CUSTOM','NON_RETURNABLE'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Products_ReturnWindowDays')
    ALTER TABLE master.Products ADD CONSTRAINT CK_Products_ReturnWindowDays CHECK (ReturnWindowDays IS NULL OR ReturnWindowDays >= 0);
GO

