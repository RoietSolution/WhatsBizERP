CREATE TABLE [inventory].[InventoryValuation] (
    [InventoryValuationId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_InventoryValuation_Id] DEFAULT NEWSEQUENTIALID(),[ProductId] UNIQUEIDENTIFIER NOT NULL,[WarehouseId] UNIQUEIDENTIFIER NOT NULL,[ValuationMethod] NVARCHAR(20) NOT NULL,[Quantity] DECIMAL(18,4) NOT NULL,[UnitCost] DECIMAL(18,4) NOT NULL,[TotalValue] AS ([Quantity]*[UnitCost]) PERSISTED,[ValuationDate] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_InventoryValuation_Date] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_InventoryValuation] PRIMARY KEY ([InventoryValuationId]),CONSTRAINT [FK_InventoryValuation_Product] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]),CONSTRAINT [FK_InventoryValuation_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses]([WarehouseId]),CONSTRAINT [CK_InventoryValuation_Method] CHECK ([ValuationMethod] IN ('AVERAGE','FIFO','LIFO'))
);
GO
CREATE INDEX [IX_InventoryValuation_ProductWarehouse] ON [inventory].[InventoryValuation]([ProductId],[WarehouseId],[ValuationDate] DESC);
