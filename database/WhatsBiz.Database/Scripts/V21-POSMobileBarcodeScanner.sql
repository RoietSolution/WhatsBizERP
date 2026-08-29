SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ProductsObjectId INT = OBJECT_ID(N'master.Products');
    IF @ProductsObjectId IS NULL
        THROW 51000, 'The master.Products table does not exist.', 1;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'UX_Products_Tenant_Barcode'
          AND [object_id] = @ProductsObjectId
    )
    BEGIN
        DECLARE @NormalizedFilter NVARCHAR(4000) = (
            SELECT REPLACE(REPLACE(REPLACE([filter_definition], N' ', N''), N'(', N''), N')', N'')
            FROM sys.indexes
            WHERE [name] = N'UX_Products_Tenant_Barcode'
              AND [object_id] = @ProductsObjectId
        );

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            WHERE i.[name] = N'UX_Products_Tenant_Barcode'
              AND i.[object_id] = @ProductsObjectId
              AND i.[is_unique] = 1
              AND i.[has_filter] = 1
        )
        OR @NormalizedFilter <> N'[Barcode]ISNOTNULLAND[IsDeleted]=0'
        OR (SELECT COUNT(*) FROM sys.index_columns WHERE [object_id] = @ProductsObjectId AND [index_id] = INDEXPROPERTY(@ProductsObjectId, N'UX_Products_Tenant_Barcode', 'IndexId') AND [key_ordinal] > 0) <> 2
        OR NOT EXISTS (
            SELECT 1
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
            WHERE ic.[object_id] = @ProductsObjectId
              AND ic.[index_id] = INDEXPROPERTY(@ProductsObjectId, N'UX_Products_Tenant_Barcode', 'IndexId')
              AND ic.[key_ordinal] = 1
              AND c.[name] = N'TenantId'
        )
        OR NOT EXISTS (
            SELECT 1
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
            WHERE ic.[object_id] = @ProductsObjectId
              AND ic.[index_id] = INDEXPROPERTY(@ProductsObjectId, N'UX_Products_Tenant_Barcode', 'IndexId')
              AND ic.[key_ordinal] = 2
              AND c.[name] = N'Barcode'
        )
            THROW 51000, 'UX_Products_Tenant_Barcode exists with an unexpected definition.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'UX_Products_Barcode'
          AND [object_id] = @ProductsObjectId
    )
        DROP INDEX [UX_Products_Barcode] ON [master].[Products];

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'UX_Products_Tenant_Barcode'
          AND [object_id] = @ProductsObjectId
    )
        CREATE UNIQUE INDEX [UX_Products_Tenant_Barcode]
            ON [master].[Products]([TenantId], [Barcode])
            WHERE [Barcode] IS NOT NULL AND [IsDeleted] = 0;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
