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

-- Add new columns in their own batch. SQL Server must compile the later static
-- references only after these schema changes are visible.
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'master.Products', N'U') IS NULL OR OBJECT_ID(N'master.ProductBarcodes', N'U') IS NULL
        THROW 51000, 'The product identifier tables do not exist.', 1;

    IF COL_LENGTH(N'master.ProductBarcodes', N'TenantId') IS NULL
        ALTER TABLE master.ProductBarcodes ADD TenantId UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH(N'master.ProductBarcodes', N'BarcodeType') IS NULL
        ALTER TABLE master.ProductBarcodes ADD BarcodeType NVARCHAR(20) NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ProductsObjectId INT = OBJECT_ID(N'master.Products', N'U');
    DECLARE @ProductBarcodesObjectId INT = OBJECT_ID(N'master.ProductBarcodes', N'U');
    DECLARE @TenantIdColumnId INT = COLUMNPROPERTY(@ProductBarcodesObjectId, N'TenantId', 'ColumnId');
    DECLARE @BarcodeTypeColumnId INT = COLUMNPROPERTY(@ProductBarcodesObjectId, N'BarcodeType', 'ColumnId');

    IF @ProductsObjectId IS NULL OR @ProductBarcodesObjectId IS NULL OR @TenantIdColumnId IS NULL OR @BarcodeTypeColumnId IS NULL
        THROW 51000, 'The product identifier tables or V22 columns do not exist.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = @ProductBarcodesObjectId
          AND c.column_id = @TenantIdColumnId
          AND t.name = N'uniqueidentifier'
    )
        THROW 51000, 'ProductBarcodes.TenantId has an unexpected datatype.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = @ProductBarcodesObjectId
          AND c.column_id = @BarcodeTypeColumnId
          AND t.name = N'nvarchar'
    )
        THROW 51000, 'ProductBarcodes.BarcodeType has an unexpected datatype.', 1;

    IF EXISTS (
        SELECT 1
        FROM (
            SELECT p.TenantId, p.ProductId, p.Barcode
            FROM master.Products p
            WHERE p.Barcode IS NOT NULL AND p.IsActive = 1 AND p.IsDeleted = 0
            UNION ALL
            SELECT p.TenantId, b.ProductId, b.Barcode
            FROM master.ProductBarcodes b
            JOIN master.Products p ON p.ProductId = b.ProductId
            WHERE b.IsActive = 1 AND b.IsDeleted = 0 AND p.IsActive = 1 AND p.IsDeleted = 0
        ) identifiers
        GROUP BY identifiers.TenantId, identifiers.Barcode
        HAVING COUNT(DISTINCT identifiers.ProductId) > 1
    )
        THROW 51000, 'Duplicate product identifiers exist within a tenant. Resolve them before rerunning V22.', 1;

    IF EXISTS (
        SELECT 1
        FROM master.ProductBarcodes b
        JOIN master.Products p ON p.ProductId = b.ProductId
        WHERE b.IsActive = 1 AND b.IsDeleted = 0 AND p.IsActive = 1 AND p.IsDeleted = 0
        GROUP BY p.TenantId, b.Barcode
        HAVING COUNT(*) > 1
    )
        THROW 51000, 'Duplicate ProductBarcodes rows exist within a tenant. Resolve them before rerunning V22.', 1;

    UPDATE b
    SET TenantId = p.TenantId
    FROM master.ProductBarcodes b
    JOIN master.Products p ON p.ProductId = b.ProductId
    WHERE b.TenantId IS NULL;

    IF EXISTS (
        SELECT 1
        FROM master.ProductBarcodes b
        LEFT JOIN master.Products p ON p.ProductId = b.ProductId
        WHERE b.TenantId IS NULL OR p.ProductId IS NULL OR b.TenantId <> p.TenantId
    )
        THROW 51000, 'One or more ProductBarcodes rows have a NULL, unresolved, or mismatched tenant.', 1;

    UPDATE master.ProductBarcodes
    SET BarcodeType = N'CUSTOM'
    WHERE BarcodeType IS NULL OR LTRIM(RTRIM(BarcodeType)) = N'';

    IF EXISTS (
        SELECT 1
        FROM master.ProductBarcodes
        WHERE BarcodeType NOT IN (N'CODE128',N'EAN13',N'EAN8',N'UPC',N'UPCA',N'UPCE',N'CODE39',N'QR',N'CUSTOM')
    )
        THROW 51000, 'ProductBarcodes contains an unsupported BarcodeType.', 1;

    IF EXISTS (SELECT 1 FROM master.ProductBarcodes WHERE LEN(Barcode) > 450)
        THROW 51000, 'ProductBarcodes contains a Barcode longer than 450 characters.', 1;

    IF EXISTS (SELECT 1 FROM master.ProductBarcodes WHERE LEN(BarcodeType) > 20)
        THROW 51000, 'ProductBarcodes contains a BarcodeType longer than 20 characters.', 1;

    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = @ProductBarcodesObjectId AND c.column_id = @TenantIdColumnId AND c.is_nullable = 1
    )
        ALTER TABLE master.ProductBarcodes ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;

    IF COL_LENGTH(N'master.ProductBarcodes', N'BarcodeType') <> 40
       OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @ProductBarcodesObjectId AND column_id = @BarcodeTypeColumnId AND is_nullable = 1)
        ALTER TABLE master.ProductBarcodes ALTER COLUMN BarcodeType NVARCHAR(20) NOT NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = @ProductBarcodesObjectId AND c.name = N'Barcode' AND t.name = N'nvarchar'
    )
        THROW 51000, 'ProductBarcodes.Barcode has an unexpected datatype.', 1;

    IF COL_LENGTH(N'master.ProductBarcodes', N'Barcode') <> 900
        ALTER TABLE master.ProductBarcodes ALTER COLUMN Barcode NVARCHAR(450) NOT NULL;

    DECLARE @ExistingDefaultName SYSNAME;
    DECLARE @ExistingDefaultDefinition NVARCHAR(MAX);
    SELECT @ExistingDefaultName = dc.name, @ExistingDefaultDefinition = dc.definition
    FROM sys.default_constraints dc
    WHERE dc.parent_object_id = @ProductBarcodesObjectId AND dc.parent_column_id = @BarcodeTypeColumnId;

    IF @ExistingDefaultName IS NOT NULL
       AND REPLACE(REPLACE(REPLACE(REPLACE(UPPER(@ExistingDefaultDefinition), N'(', N''), N')', N''), N' ', N''), N'N''', N'''') <> N'''CUSTOM'''
    BEGIN
        DECLARE @DropDefaultSql NVARCHAR(1000) = N'ALTER TABLE master.ProductBarcodes DROP CONSTRAINT ' + QUOTENAME(@ExistingDefaultName) + N';';
        EXEC sys.sp_executesql @DropDefaultSql;
        SET @ExistingDefaultName = NULL;
    END;

    IF @ExistingDefaultName IS NULL
        ALTER TABLE master.ProductBarcodes ADD CONSTRAINT DF_ProductBarcodes_BarcodeType DEFAULT (N'CUSTOM') FOR BarcodeType;

    DECLARE @ExpectedBarcodeTypeCheck NVARCHAR(MAX) = N'BARCODETYPEIN''CODE128'',''EAN13'',''EAN8'',''UPC'',''UPCA'',''UPCE'',''CODE39'',''QR'',''CUSTOM''';
    DECLARE @ConstraintDefinition NVARCHAR(MAX);
    SELECT @ConstraintDefinition = cc.definition
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = @ProductBarcodesObjectId AND cc.name = N'CK_ProductBarcodes_BarcodeType';
    SET @ConstraintDefinition = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(@ConstraintDefinition), N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), NCHAR(10), N''), N'N''', N'''');

    IF @ConstraintDefinition IS NOT NULL AND (
        @ConstraintDefinition <> @ExpectedBarcodeTypeCheck
        OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @ProductBarcodesObjectId AND name = N'CK_ProductBarcodes_BarcodeType' AND (is_disabled = 1 OR is_not_trusted = 1))
    )
        ALTER TABLE master.ProductBarcodes DROP CONSTRAINT CK_ProductBarcodes_BarcodeType;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @ProductBarcodesObjectId AND name = N'CK_ProductBarcodes_BarcodeType')
        ALTER TABLE master.ProductBarcodes WITH CHECK ADD CONSTRAINT CK_ProductBarcodes_BarcodeType CHECK (BarcodeType IN (N'CODE128',N'EAN13',N'EAN8',N'UPC',N'UPCA',N'UPCE',N'CODE39',N'QR',N'CUSTOM'));

    SET @ConstraintDefinition = NULL;
    SELECT @ConstraintDefinition = cc.definition
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = @ProductsObjectId AND cc.name = N'CK_Products_BarcodeType';
    SET @ConstraintDefinition = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(@ConstraintDefinition), N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), NCHAR(10), N''), N'N''', N'''');

    IF @ConstraintDefinition IS NOT NULL AND (
        @ConstraintDefinition <> @ExpectedBarcodeTypeCheck
        OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @ProductsObjectId AND name = N'CK_Products_BarcodeType' AND (is_disabled = 1 OR is_not_trusted = 1))
    )
        ALTER TABLE master.Products DROP CONSTRAINT CK_Products_BarcodeType;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @ProductsObjectId AND name = N'CK_Products_BarcodeType')
        ALTER TABLE master.Products WITH CHECK ADD CONSTRAINT CK_Products_BarcodeType CHECK (BarcodeType IN (N'CODE128',N'EAN13',N'EAN8',N'UPC',N'UPCA',N'UPCE',N'CODE39',N'QR',N'CUSTOM'));

    DECLARE @ForeignKeyId INT = (
        SELECT object_id FROM sys.foreign_keys
        WHERE parent_object_id = @ProductBarcodesObjectId AND name = N'FK_ProductBarcodes_TenantProduct'
    );
    IF @ForeignKeyId IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        WHERE fk.object_id = @ForeignKeyId
          AND fk.referenced_object_id = @ProductsObjectId
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
          AND fk.delete_referential_action = 0 AND fk.update_referential_action = 0
          AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = fk.object_id) = 2
          AND EXISTS (
              SELECT 1 FROM sys.foreign_key_columns fkc
              JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
              JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
              WHERE fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 1 AND pc.name = N'TenantId' AND rc.name = N'TenantId'
          )
          AND EXISTS (
              SELECT 1 FROM sys.foreign_key_columns fkc
              JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
              JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
              WHERE fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 2 AND pc.name = N'ProductId' AND rc.name = N'ProductId'
          )
    )
        ALTER TABLE master.ProductBarcodes DROP CONSTRAINT FK_ProductBarcodes_TenantProduct;

    SET @ForeignKeyId = (
        SELECT object_id FROM sys.foreign_keys
        WHERE parent_object_id = @ProductBarcodesObjectId AND name = N'FK_ProductBarcodes_Tenants'
    );
    IF @ForeignKeyId IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        WHERE fk.object_id = @ForeignKeyId
          AND fk.referenced_object_id = OBJECT_ID(N'core.Tenants', N'U')
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
          AND fk.delete_referential_action = 0 AND fk.update_referential_action = 0
          AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = fk.object_id) = 1
          AND EXISTS (
              SELECT 1 FROM sys.foreign_key_columns fkc
              JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
              JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
              WHERE fkc.constraint_object_id = fk.object_id AND pc.name = N'TenantId' AND rc.name = N'TenantId'
          )
    )
        ALTER TABLE master.ProductBarcodes DROP CONSTRAINT FK_ProductBarcodes_Tenants;

    DECLARE @IndexId INT = (
        SELECT index_id FROM sys.indexes
        WHERE object_id = @ProductBarcodesObjectId AND name = N'UX_ProductBarcodes_Barcode'
    );
    IF @IndexId IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes i
            WHERE i.object_id = @ProductBarcodesObjectId AND i.index_id = @IndexId AND i.is_unique = 1 AND i.is_disabled = 0 AND i.is_hypothetical = 0
              AND (SELECT COUNT(*) FROM sys.index_columns WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0) = 1
              AND EXISTS (
                  SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND ic.is_descending_key = 0 AND c.name = N'Barcode'
              )
        )
            THROW 51000, 'UX_ProductBarcodes_Barcode exists with an unexpected definition.', 1;
        DROP INDEX UX_ProductBarcodes_Barcode ON master.ProductBarcodes;
    END;

    SET @IndexId = (SELECT index_id FROM sys.indexes WHERE object_id = @ProductsObjectId AND name = N'UX_Products_Tenant_ProductId');
    IF @IndexId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = @ProductsObjectId AND i.index_id = @IndexId AND i.is_unique = 1 AND i.has_filter = 0 AND i.is_disabled = 0 AND i.is_hypothetical = 0
          AND (SELECT COUNT(*) FROM sys.index_columns WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND ic.is_descending_key = 0 AND c.name = N'TenantId')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND ic.is_descending_key = 0 AND c.name = N'ProductId')
    )
        DROP INDEX UX_Products_Tenant_ProductId ON master.Products;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @ProductsObjectId AND name = N'UX_Products_Tenant_ProductId')
        CREATE UNIQUE INDEX UX_Products_Tenant_ProductId ON master.Products(TenantId, ProductId);

    SET @IndexId = (SELECT index_id FROM sys.indexes WHERE object_id = @ProductBarcodesObjectId AND name = N'UX_ProductBarcodes_Tenant_Barcode');
    DECLARE @NormalizedFilter NVARCHAR(MAX) = (
        SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(filter_definition), N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), NCHAR(10), N'')
        FROM sys.indexes WHERE object_id = @ProductBarcodesObjectId AND index_id = @IndexId
    );
    IF @IndexId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = @ProductBarcodesObjectId AND i.index_id = @IndexId AND i.is_unique = 1 AND i.has_filter = 1 AND i.is_disabled = 0 AND i.is_hypothetical = 0
          AND @NormalizedFilter IN (N'ISACTIVE=1ANDISDELETED=0', N'ISDELETED=0ANDISACTIVE=1')
          AND (SELECT COUNT(*) FROM sys.index_columns WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND ic.is_descending_key = 0 AND c.name = N'TenantId')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND ic.is_descending_key = 0 AND c.name = N'Barcode')
    )
        DROP INDEX UX_ProductBarcodes_Tenant_Barcode ON master.ProductBarcodes;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @ProductBarcodesObjectId AND name = N'UX_ProductBarcodes_Tenant_Barcode')
        CREATE UNIQUE INDEX UX_ProductBarcodes_Tenant_Barcode ON master.ProductBarcodes(TenantId, Barcode) WHERE IsActive = 1 AND IsDeleted = 0;

    SET @IndexId = (SELECT index_id FROM sys.indexes WHERE object_id = @ProductBarcodesObjectId AND name = N'IX_ProductBarcodes_Tenant_Product');
    IF @IndexId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = @ProductBarcodesObjectId AND i.index_id = @IndexId AND i.is_unique = 0 AND i.has_filter = 0 AND i.is_disabled = 0 AND i.is_hypothetical = 0
          AND (SELECT COUNT(*) FROM sys.index_columns WHERE object_id = i.object_id AND index_id = i.index_id AND key_ordinal > 0) = 2
          AND (SELECT COUNT(*) FROM sys.index_columns WHERE object_id = i.object_id AND index_id = i.index_id AND is_included_column = 1) = 4
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND ic.is_descending_key = 0 AND c.name = N'TenantId')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND ic.is_descending_key = 0 AND c.name = N'ProductId')
          AND NOT EXISTS (
              SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
                AND c.name NOT IN (N'Barcode', N'BarcodeType', N'IsActive', N'IsDeleted')
          )
    )
        DROP INDEX IX_ProductBarcodes_Tenant_Product ON master.ProductBarcodes;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @ProductBarcodesObjectId AND name = N'IX_ProductBarcodes_Tenant_Product')
        CREATE INDEX IX_ProductBarcodes_Tenant_Product ON master.ProductBarcodes(TenantId, ProductId) INCLUDE(Barcode, BarcodeType, IsActive, IsDeleted);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @ProductBarcodesObjectId AND name = N'FK_ProductBarcodes_Tenants')
        ALTER TABLE master.ProductBarcodes WITH CHECK ADD CONSTRAINT FK_ProductBarcodes_Tenants FOREIGN KEY (TenantId) REFERENCES core.Tenants(TenantId);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @ProductBarcodesObjectId AND name = N'FK_ProductBarcodes_TenantProduct')
        ALTER TABLE master.ProductBarcodes WITH CHECK ADD CONSTRAINT FK_ProductBarcodes_TenantProduct FOREIGN KEY (TenantId, ProductId) REFERENCES master.Products(TenantId, ProductId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
