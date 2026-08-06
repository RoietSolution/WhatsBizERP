CREATE TABLE [inventory].[InventoryTransactions] (
    [TransactionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_InventoryTransactions_Id] DEFAULT NEWSEQUENTIALID(),
    [TransactionNo] NVARCHAR(50) NOT NULL,
    [TransactionDate] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_InventoryTransactions_Date] DEFAULT SYSUTCDATETIME(),
    [TransactionType] NVARCHAR(30) NOT NULL,[ReferenceType] NVARCHAR(50) NULL,[ReferenceId] UNIQUEIDENTIFIER NULL,[WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [Remarks] NVARCHAR(1000) NULL,[CreatedBy] NVARCHAR(256) NULL,[CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_InventoryTransactions_Created] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_InventoryTransactions] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [UQ_InventoryTransactions_No] UNIQUE ([TransactionNo]),
    CONSTRAINT [FK_InventoryTransactions_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses]([WarehouseId]),
    CONSTRAINT [CK_InventoryTransactions_Type] CHECK ([TransactionType] IN ('ADJUSTMENT_IN','ADJUSTMENT_OUT','TRANSFER_OUT','TRANSFER_IN','RESERVATION','RELEASE','PURCHASE','SALE','RETURN','MANUFACTURING'))
);
GO
CREATE INDEX [IX_InventoryTransactions_Date] ON [inventory].[InventoryTransactions]([TransactionDate] DESC,[WarehouseId],[TransactionType]);
