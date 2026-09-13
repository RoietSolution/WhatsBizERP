/* V32: enforce tenant-scoped supplier-name uniqueness for active suppliers. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'purchase.Suppliers') AND name = N'UX_Suppliers_Name')
BEGIN
    CREATE UNIQUE INDEX UX_Suppliers_Name
        ON purchase.Suppliers(TenantId, SupplierName)
        WHERE TenantId IS NOT NULL AND IsDeleted = 0;
END;
