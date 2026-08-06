CREATE TABLE [inventory].[InventorySettings] (
    [InventorySettingsId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_InventorySettings_Id] DEFAULT NEWSEQUENTIALID(),
    [NegativeStockAllowed] BIT NOT NULL CONSTRAINT [DF_InventorySettings_Negative] DEFAULT 0,
    [BatchTrackingEnabled] BIT NOT NULL CONSTRAINT [DF_InventorySettings_Batch] DEFAULT 1,
    [SerialTrackingEnabled] BIT NOT NULL CONSTRAINT [DF_InventorySettings_Serial] DEFAULT 1,
    [ValuationMethod] NVARCHAR(20) NOT NULL CONSTRAINT [DF_InventorySettings_Valuation] DEFAULT 'AVERAGE',
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_InventorySettings_Created] DEFAULT SYSUTCDATETIME(),
    [ModifiedOn] DATETIMEOFFSET NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_InventorySettings] PRIMARY KEY ([InventorySettingsId]),
    CONSTRAINT [CK_InventorySettings_Valuation] CHECK ([ValuationMethod] IN ('AVERAGE','FIFO','LIFO'))
);
