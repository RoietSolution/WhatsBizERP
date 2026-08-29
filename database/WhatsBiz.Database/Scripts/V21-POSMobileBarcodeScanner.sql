SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'UX_Products_Barcode'
      AND [object_id] = OBJECT_ID(N'master.Products')
)
    DROP INDEX [UX_Products_Barcode] ON [master].[Products];

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'UX_Products_Tenant_Barcode'
      AND [object_id] = OBJECT_ID(N'master.Products')
)
    CREATE UNIQUE INDEX [UX_Products_Tenant_Barcode]
        ON [master].[Products]([TenantId], [Barcode])
        WHERE [Barcode] IS NOT NULL AND [IsDeleted] = 0;
