IF OBJECT_ID(N'commerce.StorefrontReturnRequests', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontReturnRequests
    (
        ReturnRequestId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StorefrontReturnRequests_Id DEFAULT NEWSEQUENTIALID(),
        TenantId UNIQUEIDENTIFIER NOT NULL,
        InvoiceId UNIQUEIDENTIFIER NOT NULL,
        InvoiceItemId UNIQUEIDENTIFIER NOT NULL,
        CustomerId UNIQUEIDENTIFIER NOT NULL,
        Quantity DECIMAL(18,4) NOT NULL,
        Reason NVARCHAR(250) NOT NULL,
        Explanation NVARCHAR(1000) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_StorefrontReturnRequests_Status DEFAULT N'REQUESTED',
        RequestedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontReturnRequests_Requested DEFAULT SYSUTCDATETIME(),
        DecidedAtUtc DATETIMEOFFSET NULL,
        DecidedBy UNIQUEIDENTIFIER NULL,
        DecisionNote NVARCHAR(500) NULL,
        ReceivedAtUtc DATETIMEOFFSET NULL,
        CompletedAtUtc DATETIMEOFFSET NULL,
        UpdatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontReturnRequests_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_StorefrontReturnRequests PRIMARY KEY(ReturnRequestId),
        CONSTRAINT FK_StorefrontReturnRequests_Invoice FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId),
        CONSTRAINT FK_StorefrontReturnRequests_Item FOREIGN KEY(InvoiceItemId) REFERENCES sales.SalesInvoiceItems(InvoiceItemId),
        CONSTRAINT CK_StorefrontReturnRequests_Status CHECK(Status IN(N'REQUESTED',N'APPROVED',N'REJECTED',N'RECEIVED',N'COMPLETED')),
        CONSTRAINT CK_StorefrontReturnRequests_Quantity CHECK(Quantity>0)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontReturnRequests_ActiveItem' AND object_id=OBJECT_ID(N'commerce.StorefrontReturnRequests'))
    CREATE UNIQUE INDEX UX_StorefrontReturnRequests_ActiveItem ON commerce.StorefrontReturnRequests(TenantId,InvoiceItemId) WHERE Status IN(N'REQUESTED',N'APPROVED',N'RECEIVED');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontReturnRequests_TenantStatus' AND object_id=OBJECT_ID(N'commerce.StorefrontReturnRequests'))
    CREATE INDEX IX_StorefrontReturnRequests_TenantStatus ON commerce.StorefrontReturnRequests(TenantId,Status,RequestedAtUtc DESC);
