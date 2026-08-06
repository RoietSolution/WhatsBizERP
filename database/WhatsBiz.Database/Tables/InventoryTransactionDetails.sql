CREATE TABLE [inventory].[InventoryTransactionDetails] (
    [TransactionDetailId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_InventoryTransactionDetails_Id] DEFAULT NEWSEQUENTIALID(),[TransactionId] UNIQUEIDENTIFIER NOT NULL,[ProductId] UNIQUEIDENTIFIER NOT NULL,
    [BatchNo] NVARCHAR(100) NULL,[SerialNo] NVARCHAR(100) NULL,[Quantity] DECIMAL(18,4) NOT NULL,[UnitCost] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_InventoryTransactionDetails_Cost] DEFAULT 0,
    [TotalCost] AS ([Quantity]*[UnitCost]) PERSISTED,
    CONSTRAINT [PK_InventoryTransactionDetails] PRIMARY KEY ([TransactionDetailId]),
    CONSTRAINT [FK_InventoryTransactionDetails_Transaction] FOREIGN KEY ([TransactionId]) REFERENCES [inventory].[InventoryTransactions]([TransactionId]) ON DELETE CASCADE,
    CONSTRAINT [FK_InventoryTransactionDetails_Product] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]),
    CONSTRAINT [CK_InventoryTransactionDetails_Quantity] CHECK ([Quantity]>0),CONSTRAINT [CK_InventoryTransactionDetails_Cost] CHECK ([UnitCost]>=0)
);
GO
CREATE INDEX [IX_InventoryTransactionDetails_Product] ON [inventory].[InventoryTransactionDetails]([ProductId],[TransactionId]);
