CREATE TABLE [inventory].[WarehouseAddresses] (
    [AddressId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WarehouseAddresses_Id] DEFAULT NEWSEQUENTIALID(),
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [AddressLine1] NVARCHAR(250) NOT NULL,
    [AddressLine2] NVARCHAR(250) NULL,
    [City] NVARCHAR(100) NOT NULL,
    [District] NVARCHAR(100) NULL,
    [State] NVARCHAR(100) NOT NULL,
    [Country] NVARCHAR(100) NOT NULL CONSTRAINT [DF_WarehouseAddresses_Country] DEFAULT 'India',
    [PostalCode] NVARCHAR(20) NOT NULL,
    CONSTRAINT [PK_WarehouseAddresses] PRIMARY KEY ([AddressId]),
    CONSTRAINT [FK_WarehouseAddresses_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses]([WarehouseId]) ON DELETE CASCADE
);
GO
CREATE UNIQUE INDEX [UX_WarehouseAddresses_Warehouse] ON [inventory].[WarehouseAddresses]([WarehouseId]);
