ALTER TABLE [inventory].[Warehouses] ADD CONSTRAINT [FK_Warehouses_Address] FOREIGN KEY ([AddressId]) REFERENCES [inventory].[WarehouseAddresses]([AddressId]);
