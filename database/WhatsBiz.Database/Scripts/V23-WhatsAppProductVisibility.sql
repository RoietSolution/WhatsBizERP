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

    IF OBJECT_ID(N'master.Products', N'U') IS NULL
        THROW 51000, 'The master.Products table does not exist.', 1;

    IF COL_LENGTH(N'master.Products', N'IsWhatsAppVisible') IS NULL
        ALTER TABLE master.Products
            ADD IsWhatsAppVisible BIT NOT NULL
                CONSTRAINT DF_Products_IsWhatsAppVisible DEFAULT (1) WITH VALUES;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- The new column must be visible to SQL Server before compiling these static references.
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ProductsObjectId INT = OBJECT_ID(N'master.Products', N'U');
    IF @ProductsObjectId IS NULL OR COL_LENGTH(N'master.Products', N'IsWhatsAppVisible') IS NULL
        THROW 51000, 'Products.IsWhatsAppVisible was not created.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = @ProductsObjectId
          AND c.name = N'IsWhatsAppVisible'
          AND t.name = N'bit'
    )
        THROW 51000, 'Products.IsWhatsAppVisible has an unexpected datatype.', 1;

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = @ProductsObjectId
          AND name = N'IsWhatsAppVisible'
          AND is_nullable = 1
    )
    BEGIN
        UPDATE master.Products
        SET IsWhatsAppVisible = 1
        WHERE IsWhatsAppVisible IS NULL;

        ALTER TABLE master.Products ALTER COLUMN IsWhatsAppVisible BIT NOT NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        JOIN sys.columns c
          ON c.object_id = dc.parent_object_id
         AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = @ProductsObjectId
          AND c.name = N'IsWhatsAppVisible'
    )
        ALTER TABLE master.Products
            ADD CONSTRAINT DF_Products_IsWhatsAppVisible DEFAULT (1) FOR IsWhatsAppVisible;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
