CREATE TABLE [inventory].[StockAdjustments] (
    [StockAdjustmentId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_StockAdjustments_Id] DEFAULT NEWSEQUENTIALID(),[TransactionId] UNIQUEIDENTIFIER NOT NULL,[AdjustmentNo] NVARCHAR(50) NOT NULL,
    [AdjustmentType] NVARCHAR(10) NOT NULL,[ReasonCode] NVARCHAR(50) NOT NULL,[ApprovalStatus] NVARCHAR(20) NOT NULL CONSTRAINT [DF_StockAdjustments_Status] DEFAULT 'PENDING',
    [ApprovedBy] NVARCHAR(256) NULL,[ApprovedOn] DATETIMEOFFSET NULL,[CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_StockAdjustments_Created] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_StockAdjustments] PRIMARY KEY ([StockAdjustmentId]),CONSTRAINT [UQ_StockAdjustments_No] UNIQUE ([AdjustmentNo]),
    CONSTRAINT [FK_StockAdjustments_Transaction] FOREIGN KEY ([TransactionId]) REFERENCES [inventory].[InventoryTransactions]([TransactionId]),
    CONSTRAINT [CK_StockAdjustments_Type] CHECK ([AdjustmentType] IN ('INCREASE','DECREASE')),
    CONSTRAINT [CK_StockAdjustments_Status] CHECK ([ApprovalStatus] IN ('PENDING','APPROVED','REJECTED'))
);
