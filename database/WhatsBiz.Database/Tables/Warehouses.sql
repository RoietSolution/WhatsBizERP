CREATE TABLE [inventory].[Warehouses] (
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Warehouses_Id] DEFAULT NEWSEQUENTIALID(),
    [WarehouseCode] NVARCHAR(50) NOT NULL,
    [WarehouseName] NVARCHAR(200) NOT NULL,
    [WarehouseTypeId] UNIQUEIDENTIFIER NOT NULL,
    [BranchId] UNIQUEIDENTIFIER NULL,
    [ManagerName] NVARCHAR(150) NULL,
    [Email] NVARCHAR(256) NULL,
    [Phone] NVARCHAR(20) NULL,
    [Mobile] NVARCHAR(15) NULL,
    [Capacity] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_Warehouses_Capacity] DEFAULT 0,
    [AddressId] UNIQUEIDENTIFIER NULL,
    [IsDefault] BIT NOT NULL CONSTRAINT [DF_Warehouses_Default] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Warehouses_Active] DEFAULT 1,
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_Warehouses_Deleted] DEFAULT 0,
    [Remarks] NVARCHAR(1000) NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_Warehouses_Created] DEFAULT SYSUTCDATETIME(),
    [CreatedBy] NVARCHAR(256) NULL,
    [ModifiedOn] DATETIMEOFFSET NULL,
    [ModifiedBy] NVARCHAR(256) NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_Warehouses] PRIMARY KEY ([WarehouseId]),
    CONSTRAINT [FK_Warehouses_Type] FOREIGN KEY ([WarehouseTypeId]) REFERENCES [inventory].[WarehouseTypes]([WarehouseTypeId]),
    CONSTRAINT [CK_Warehouses_Capacity] CHECK ([Capacity] >= 0)
);
GO
CREATE UNIQUE INDEX [UX_Warehouses_Code] ON [inventory].[Warehouses]([WarehouseCode]) WHERE [IsDeleted] = 0;
GO
CREATE UNIQUE INDEX [UX_Warehouses_Name] ON [inventory].[Warehouses]([WarehouseName]) WHERE [IsDeleted] = 0;
GO
CREATE UNIQUE INDEX [UX_Warehouses_Default] ON [inventory].[Warehouses]([IsDefault]) WHERE [IsDefault] = 1 AND [IsDeleted] = 0;
GO
CREATE INDEX [IX_Warehouses_Search] ON [inventory].[Warehouses]([WarehouseName],[WarehouseTypeId],[IsActive]);
