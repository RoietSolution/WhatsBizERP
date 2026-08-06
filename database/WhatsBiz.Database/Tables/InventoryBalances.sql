CREATE TABLE [inventory].[InventoryBalances] (
    [InventoryBalanceId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_InventoryBalances_Id] DEFAULT NEWSEQUENTIALID(),
    [ProductId] UNIQUEIDENTIFIER NOT NULL,
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ZoneId] UNIQUEIDENTIFIER NULL,
    [BinId] UNIQUEIDENTIFIER NULL,
    [BatchNo] NVARCHAR(100) NULL,
    [SerialNo] NVARCHAR(100) NULL,
    [QuantityOnHand] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_InventoryBalances_OnHand] DEFAULT 0,
    [QuantityReserved] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_InventoryBalances_Reserved] DEFAULT 0,
    [QuantityAvailable] AS ([QuantityOnHand]-[QuantityReserved]) PERSISTED,
    [AverageCost] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_InventoryBalances_AverageCost] DEFAULT 0,
    [LastPurchaseCost] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_InventoryBalances_LastCost] DEFAULT 0,
    [LastUpdated] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_InventoryBalances_Updated] DEFAULT SYSUTCDATETIME(),
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_InventoryBalances_Created] DEFAULT SYSUTCDATETIME(),
    [CreatedBy] NVARCHAR(256) NULL,[ModifiedOn] DATETIMEOFFSET NULL,[ModifiedBy] NVARCHAR(256) NULL,[RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventoryBalances] PRIMARY KEY ([InventoryBalanceId]),
    CONSTRAINT [FK_InventoryBalances_Product] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]),
    CONSTRAINT [FK_InventoryBalances_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses]([WarehouseId]),
    CONSTRAINT [FK_InventoryBalances_Zone] FOREIGN KEY ([ZoneId]) REFERENCES [inventory].[WarehouseZones]([ZoneId]),
    CONSTRAINT [FK_InventoryBalances_Bin] FOREIGN KEY ([BinId]) REFERENCES [inventory].[WarehouseBins]([BinId]),
    CONSTRAINT [CK_InventoryBalances_Reserved] CHECK ([QuantityReserved]>=0)
);
GO
CREATE UNIQUE INDEX [UX_InventoryBalances_Location] ON [inventory].[InventoryBalances]([ProductId],[WarehouseId],[ZoneId],[BinId],[BatchNo],[SerialNo]);
GO
CREATE INDEX [IX_InventoryBalances_WarehouseProduct] ON [inventory].[InventoryBalances]([WarehouseId],[ProductId]) INCLUDE([QuantityOnHand],[QuantityReserved],[QuantityAvailable],[AverageCost]);
