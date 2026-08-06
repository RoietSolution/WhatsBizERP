CREATE TABLE [inventory].[WarehouseZones] (
    [ZoneId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WarehouseZones_Id] DEFAULT NEWSEQUENTIALID(),
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ZoneCode] NVARCHAR(50) NOT NULL,
    [ZoneName] NVARCHAR(150) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_WarehouseZones_Active] DEFAULT 1,
    CONSTRAINT [PK_WarehouseZones] PRIMARY KEY ([ZoneId]),
    CONSTRAINT [FK_WarehouseZones_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses]([WarehouseId]) ON DELETE CASCADE,
    CONSTRAINT [UQ_WarehouseZones_Code] UNIQUE ([WarehouseId],[ZoneCode])
);
GO
CREATE INDEX [IX_WarehouseZones_Warehouse] ON [inventory].[WarehouseZones]([WarehouseId],[IsActive]);
