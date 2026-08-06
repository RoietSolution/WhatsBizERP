CREATE TABLE [inventory].[WarehouseContacts] (
    [ContactId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WarehouseContacts_Id] DEFAULT NEWSEQUENTIALID(),
    [WarehouseId] UNIQUEIDENTIFIER NOT NULL,
    [ContactPerson] NVARCHAR(150) NOT NULL,
    [Designation] NVARCHAR(100) NULL,
    [Mobile] NVARCHAR(15) NULL,
    [Email] NVARCHAR(256) NULL,
    [IsPrimary] BIT NOT NULL CONSTRAINT [DF_WarehouseContacts_Primary] DEFAULT 0,
    CONSTRAINT [PK_WarehouseContacts] PRIMARY KEY ([ContactId]),
    CONSTRAINT [FK_WarehouseContacts_Warehouse] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses]([WarehouseId]) ON DELETE CASCADE
);
GO
CREATE INDEX [IX_WarehouseContacts_Warehouse] ON [inventory].[WarehouseContacts]([WarehouseId],[IsPrimary]);
