CREATE TABLE [inventory].[WarehouseBins] (
    [BinId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WarehouseBins_Id] DEFAULT NEWSEQUENTIALID(),
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ZoneId] UNIQUEIDENTIFIER NOT NULL,
    [BinCode] NVARCHAR(50) NOT NULL,
    [BinName] NVARCHAR(150) NOT NULL,
    [MaximumCapacity] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_WarehouseBins_Capacity] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_WarehouseBins_Active] DEFAULT 1,
    CONSTRAINT [PK_WarehouseBins] PRIMARY KEY ([BinId]),
    CONSTRAINT [FK_WarehouseBins_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses]([WarehouseId]),
    CONSTRAINT [FK_WarehouseBins_Zone] FOREIGN KEY ([ZoneId]) REFERENCES [inventory].[WarehouseZones]([ZoneId]) ON DELETE CASCADE,
    CONSTRAINT [UQ_WarehouseBins_Code] UNIQUE ([WarehouseId],[BinCode]),
    CONSTRAINT [CK_WarehouseBins_Capacity] CHECK ([MaximumCapacity] >= 0)
);
GO
CREATE INDEX [IX_WarehouseBins_Zone] ON [inventory].[WarehouseBins]([ZoneId],[IsActive]);
