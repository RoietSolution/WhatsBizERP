CREATE TABLE [finance].[CustomerOutstanding]
(
    [CustomerOutstandingId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CustomerOutstanding_Id] DEFAULT NEWSEQUENTIALID(),
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [InvoiceId] UNIQUEIDENTIFIER NOT NULL,
    [InvoiceNumber] NVARCHAR(50) NOT NULL,
    [InvoiceDate] DATETIMEOFFSET NOT NULL,
    [DueDate] DATETIMEOFFSET NOT NULL,
    [InvoiceAmount] DECIMAL(18,2) NOT NULL,
    [ReceivedAmount] DECIMAL(18,2) NOT NULL,
    [OutstandingAmount] DECIMAL(18,2) NOT NULL,
    [AgeDays] INT NOT NULL,
    [AgeBucket] NVARCHAR(20) NOT NULL,
    [LastUpdated] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_CustomerOutstanding_LastUpdated] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_CustomerOutstanding] PRIMARY KEY ([CustomerOutstandingId]),
    CONSTRAINT [UQ_CustomerOutstanding_Invoice] UNIQUE ([InvoiceId]),
    CONSTRAINT [FK_CustomerOutstanding_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [sales].[Customers]([CustomerId]),
    CONSTRAINT [FK_CustomerOutstanding_Invoice] FOREIGN KEY ([InvoiceId]) REFERENCES [sales].[SalesInvoices]([InvoiceId])
);
GO
CREATE INDEX [IX_CustomerOutstanding_Ageing]
    ON [finance].[CustomerOutstanding]([CustomerId],[AgeBucket],[OutstandingAmount]);
