/* V30: force newly enrolled retailer administrators to replace temporary passwords,
   and scope supplier uniqueness to each retailer. */
IF COL_LENGTH(N'core.Users', N'MustChangePassword') IS NULL
BEGIN
    ALTER TABLE core.Users ADD MustChangePassword BIT NOT NULL
        CONSTRAINT DF_Users_MustChangePassword DEFAULT (0) WITH VALUES;
END;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'purchase.Suppliers') AND name = N'UX_Suppliers_Code')
    DROP INDEX UX_Suppliers_Code ON purchase.Suppliers;
GO
CREATE UNIQUE INDEX UX_Suppliers_Code ON purchase.Suppliers(TenantId, SupplierCode)
    WHERE TenantId IS NOT NULL AND IsDeleted = 0;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'purchase.Suppliers') AND name = N'UX_Suppliers_GSTIN')
    DROP INDEX UX_Suppliers_GSTIN ON purchase.Suppliers;
GO
CREATE UNIQUE INDEX UX_Suppliers_GSTIN ON purchase.Suppliers(TenantId, GSTIN)
    WHERE TenantId IS NOT NULL AND GSTIN IS NOT NULL AND IsDeleted = 0;
GO
