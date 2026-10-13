IF COL_LENGTH(N'master.Products',N'ReturnPolicyDescription') IS NULL
    THROW 51053, 'V53 validation failed: product return policy description is missing.', 1;
IF COL_LENGTH(N'sales.SalesInvoiceItems',N'ReturnPolicyModeSnapshot') IS NULL OR COL_LENGTH(N'sales.SalesInvoiceItems',N'ReturnWindowDaysSnapshot') IS NULL OR COL_LENGTH(N'sales.SalesInvoiceItems',N'ReturnPolicyDescriptionSnapshot') IS NULL
    THROW 51054, 'V53 validation failed: sales item return policy snapshots are missing.', 1;
IF OBJECT_ID(N'sales.TR_SalesInvoiceItems_ReturnPolicySnapshot',N'TR') IS NULL
    THROW 51055, 'V53 validation failed: return policy snapshot trigger is missing.', 1;
IF EXISTS (SELECT 1 FROM master.Products WHERE ReturnPolicyMode NOT IN(N'INHERIT_DEFAULT',N'CUSTOM',N'NON_RETURNABLE') OR ReturnWindowDays IS NOT NULL AND (ReturnWindowDays<0 OR ReturnWindowDays>3650))
    THROW 51056, 'V53 validation failed: invalid product return policy values exist.', 1;
