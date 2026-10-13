CREATE TABLE [master].[ProductSpecifications]
(
    [ProductSpecificationId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_ProductSpecifications] PRIMARY KEY,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Value] NVARCHAR(1000) NOT NULL,
    [DisplayOrder] INT NOT NULL CONSTRAINT [DF_ProductSpecifications_DisplayOrder] DEFAULT ((0)),
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_ProductSpecifications_CreatedOn] DEFAULT (SYSUTCDATETIME()),
    [CreatedBy] NVARCHAR(256) NULL,
    [ModifiedOn] DATETIMEOFFSET NULL,
    [ModifiedBy] NVARCHAR(256) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ProductSpecifications_IsActive] DEFAULT ((1)),
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_ProductSpecifications_IsDeleted] DEFAULT ((0)),
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductSpecifications_Product] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products] ([ProductId])
);
GO
CREATE UNIQUE INDEX [UX_ProductSpecifications_Tenant_Product_Name] ON [master].[ProductSpecifications] ([TenantId], [ProductId], [Name]) WHERE [IsDeleted] = 0;
