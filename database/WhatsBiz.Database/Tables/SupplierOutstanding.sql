CREATE TABLE [finance].[SupplierOutstanding]
(
    [SupplierOutstandingId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_SupplierOutstanding_Id] DEFAULT NEWSEQUENTIALID(),
    [SupplierId] UNIQUEIDENTIFIER NOT NULL,
    [PurchaseInvoiceId] UNIQUEIDENTIFIER NOT NULL,
    [InvoiceNumber] NVARCHAR(50) NOT NULL,
    [InvoiceDate] DATETIMEOFFSET NOT NULL,
    [DueDate] DATETIMEOFFSET NOT NULL,
    [InvoiceAmount] DECIMAL(18,2) NOT NULL,
    [PaidAmount] DECIMAL(18,2) NOT NULL,
    [OutstandingAmount] DECIMAL(18,2) NOT NULL,
    [AgeDays] INT NOT NULL,
    [AgeBucket] NVARCHAR(20) NOT NULL,
    [LastUpdated] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_SupplierOutstanding_LastUpdated] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SupplierOutstanding] PRIMARY KEY ([SupplierOutstandingId]),
    CONSTRAINT [UQ_SupplierOutstanding_Invoice] UNIQUE ([PurchaseInvoiceId]),
    CONSTRAINT [FK_SupplierOutstanding_Supplier] FOREIGN KEY ([SupplierId]) REFERENCES [purchase].[Suppliers]([SupplierId]),
    CONSTRAINT [FK_SupplierOutstanding_Invoice] FOREIGN KEY ([PurchaseInvoiceId]) REFERENCES [purchase].[PurchaseInvoices]([PurchaseInvoiceId])
);
GO
CREATE INDEX [IX_SupplierOutstanding_Ageing]
    ON [finance].[SupplierOutstanding]([SupplierId],[AgeBucket],[OutstandingAmount]);
