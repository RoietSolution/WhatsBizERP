CREATE TABLE [integration].[CustomerNotifications]
(
    [CustomerNotificationId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CustomerNotifications_Id] DEFAULT NEWSEQUENTIALID(),
    [CustomerId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentId] UNIQUEIDENTIFIER NOT NULL,
    [DocumentType] NVARCHAR(40) NOT NULL,
    [EventType] NVARCHAR(50) NOT NULL,
    [Channel] NVARCHAR(20) NOT NULL,
    [Recipient] NVARCHAR(30) NULL,
    [MessageTemplate] NVARCHAR(4000) NOT NULL,
    [Message] NVARCHAR(4000) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_CustomerNotifications_Status] DEFAULT N'PENDING',
    [ProviderMessageId] NVARCHAR(200) NULL,
    [ErrorMessage] NVARCHAR(1000) NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_CustomerNotifications_Attempts] DEFAULT 0,
    [CreatedOn] DATETIMEOFFSET(7) NOT NULL CONSTRAINT [DF_CustomerNotifications_CreatedOn] DEFAULT SYSUTCDATETIME(),
    [SentOn] DATETIMEOFFSET(7) NULL,
    [LastAttemptOn] DATETIMEOFFSET(7) NULL,
    [NextAttemptOn] DATETIMEOFFSET(7) NULL,
    [ModifiedBy] NVARCHAR(256) NULL,
    CONSTRAINT [PK_CustomerNotifications] PRIMARY KEY ([CustomerNotificationId]),
    CONSTRAINT [FK_CustomerNotifications_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [sales].[Customers]([CustomerId]),
    CONSTRAINT [FK_CustomerNotifications_Invoice] FOREIGN KEY ([DocumentId]) REFERENCES [sales].[SalesInvoices]([InvoiceId]),
    CONSTRAINT [CK_CustomerNotifications_Channel] CHECK ([Channel] IN (N'WHATSAPP',N'SMS')),
    CONSTRAINT [CK_CustomerNotifications_Status] CHECK ([Status] IN (N'PENDING',N'PROCESSING',N'SENT',N'FAILED')),
    CONSTRAINT [CK_CustomerNotifications_Attempts] CHECK ([AttemptCount] BETWEEN 0 AND 3),
    CONSTRAINT [UQ_CustomerNotifications_Event] UNIQUE ([DocumentId],[DocumentType],[CustomerId],[Channel],[EventType])
);
GO
CREATE INDEX [IX_CustomerNotifications_Work] ON [integration].[CustomerNotifications]([Status],[NextAttemptOn],[CreatedOn]) INCLUDE ([Channel],[Recipient],[AttemptCount]);
GO
CREATE INDEX [IX_CustomerNotifications_History] ON [integration].[CustomerNotifications]([CreatedOn] DESC) INCLUDE ([CustomerId],[DocumentId],[Channel],[Status],[AttemptCount]);
