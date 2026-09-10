CREATE TABLE [master].[TenantProductCategories]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [ProductCategoryId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_TenantProductCategories] PRIMARY KEY ([TenantId], [ProductCategoryId]),
    CONSTRAINT [FK_TenantProductCategories_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants]([TenantId]),
    CONSTRAINT [FK_TenantProductCategories_Category] FOREIGN KEY ([ProductCategoryId]) REFERENCES [master].[ProductCategories]([ProductCategoryId])
);
GO

CREATE TABLE [master].[TenantBrands]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [BrandId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_TenantBrands] PRIMARY KEY ([TenantId], [BrandId]),
    CONSTRAINT [FK_TenantBrands_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants]([TenantId]),
    CONSTRAINT [FK_TenantBrands_Brand] FOREIGN KEY ([BrandId]) REFERENCES [master].[Brands]([BrandId])
);
GO

CREATE TABLE [master].[TenantUnitsOfMeasure]
(
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [UnitId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_TenantUnitsOfMeasure] PRIMARY KEY ([TenantId], [UnitId]),
    CONSTRAINT [FK_TenantUnitsOfMeasure_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants]([TenantId]),
    CONSTRAINT [FK_TenantUnitsOfMeasure_Unit] FOREIGN KEY ([UnitId]) REFERENCES [master].[UnitsOfMeasure]([UnitId])
);
GO
