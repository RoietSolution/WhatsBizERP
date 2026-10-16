/* V56: durable feedback notifications, external delivery queue, and private attachments. */
IF OBJECT_ID(N'commerce.StorefrontFeedbackNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontFeedbackNotifications
    (
        NotificationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StorefrontFeedbackNotifications_Id DEFAULT NEWSEQUENTIALID(),
        TenantId UNIQUEIDENTIFIER NOT NULL,
        TicketId UNIQUEIDENTIFIER NOT NULL,
        CustomerId UNIQUEIDENTIFIER NULL,
        RecipientType NVARCHAR(12) NOT NULL,
        NotificationType NVARCHAR(40) NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        Message NVARCHAR(1000) NOT NULL,
        IsRead BIT NOT NULL CONSTRAINT DF_StorefrontFeedbackNotifications_Read DEFAULT 0,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontFeedbackNotifications_Created DEFAULT SYSUTCDATETIME(),
        ReadAtUtc DATETIMEOFFSET NULL,
        IdempotencyKey NVARCHAR(180) NOT NULL,
        CONSTRAINT PK_StorefrontFeedbackNotifications PRIMARY KEY(NotificationId),
        CONSTRAINT CK_StorefrontFeedbackNotifications_Recipient CHECK(RecipientType IN(N'ERP',N'CUSTOMER')),
        CONSTRAINT FK_StorefrontFeedbackNotifications_Ticket FOREIGN KEY(TicketId) REFERENCES commerce.StorefrontFeedbackTickets(TicketId)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontFeedbackNotifications_Idempotency' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotifications')) CREATE UNIQUE INDEX UX_StorefrontFeedbackNotifications_Idempotency ON commerce.StorefrontFeedbackNotifications(TenantId,IdempotencyKey);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackNotifications_Recipient' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotifications')) CREATE INDEX IX_StorefrontFeedbackNotifications_Recipient ON commerce.StorefrontFeedbackNotifications(TenantId,RecipientType,CustomerId,IsRead,CreatedAtUtc DESC);

IF OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontFeedbackNotificationDeliveries
    (
        DeliveryId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StorefrontFeedbackNotificationDeliveries_Id DEFAULT NEWSEQUENTIALID(),
        TenantId UNIQUEIDENTIFIER NOT NULL,
        NotificationId UNIQUEIDENTIFIER NOT NULL,
        TicketId UNIQUEIDENTIFIER NOT NULL,
        Channel NVARCHAR(12) NOT NULL,
        RecipientReference NVARCHAR(320) NULL,
        TemplateKey NVARCHAR(100) NULL,
        Status NVARCHAR(12) NOT NULL CONSTRAINT DF_StorefrontFeedbackNotificationDeliveries_Status DEFAULT N'PENDING',
        AttemptCount INT NOT NULL CONSTRAINT DF_StorefrontFeedbackNotificationDeliveries_Attempts DEFAULT 0,
        LastAttemptAtUtc DATETIMEOFFSET NULL,
        ProviderMessageReference NVARCHAR(200) NULL,
        ErrorCategory NVARCHAR(200) NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontFeedbackNotificationDeliveries_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_StorefrontFeedbackNotificationDeliveries PRIMARY KEY(DeliveryId),
        CONSTRAINT CK_StorefrontFeedbackNotificationDeliveries_Channel CHECK(Channel IN(N'EMAIL',N'WHATSAPP')),
        CONSTRAINT CK_StorefrontFeedbackNotificationDeliveries_Status CHECK(Status IN(N'PENDING',N'SENT',N'FAILED',N'SKIPPED')),
        CONSTRAINT FK_StorefrontFeedbackNotificationDeliveries_Notification FOREIGN KEY(NotificationId) REFERENCES commerce.StorefrontFeedbackNotifications(NotificationId)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontFeedbackNotificationDeliveries_Channel' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries')) CREATE UNIQUE INDEX UX_StorefrontFeedbackNotificationDeliveries_Channel ON commerce.StorefrontFeedbackNotificationDeliveries(NotificationId,Channel);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackNotificationDeliveries_Work' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries')) CREATE INDEX IX_StorefrontFeedbackNotificationDeliveries_Work ON commerce.StorefrontFeedbackNotificationDeliveries(Status,CreatedAtUtc);

IF OBJECT_ID(N'commerce.StorefrontFeedbackAttachments', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontFeedbackAttachments
    (
        AttachmentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StorefrontFeedbackAttachments_Id DEFAULT NEWSEQUENTIALID(),
        TenantId UNIQUEIDENTIFIER NOT NULL,
        TicketId UNIQUEIDENTIFIER NOT NULL,
        CustomerId UNIQUEIDENTIFIER NULL,
        OriginalFileName NVARCHAR(255) NOT NULL,
        ContentType NVARCHAR(100) NOT NULL,
        SizeBytes BIGINT NOT NULL,
        StorageProvider NVARCHAR(20) NOT NULL,
        ObjectKey NVARCHAR(500) NULL,
        StorageContent VARBINARY(MAX) NULL,
        ScanStatus NVARCHAR(12) NOT NULL CONSTRAINT DF_StorefrontFeedbackAttachments_Scan DEFAULT N'CLEAN',
        IsInternal BIT NOT NULL CONSTRAINT DF_StorefrontFeedbackAttachments_Internal DEFAULT 0,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontFeedbackAttachments_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_StorefrontFeedbackAttachments PRIMARY KEY(AttachmentId),
        CONSTRAINT CK_StorefrontFeedbackAttachments_Scan CHECK(ScanStatus IN(N'PENDING',N'CLEAN',N'BLOCKED',N'FAILED')),
        CONSTRAINT FK_StorefrontFeedbackAttachments_Ticket FOREIGN KEY(TicketId) REFERENCES commerce.StorefrontFeedbackTickets(TicketId)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackAttachments_Ticket' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackAttachments')) CREATE INDEX IX_StorefrontFeedbackAttachments_Ticket ON commerce.StorefrontFeedbackAttachments(TenantId,TicketId,IsInternal,ScanStatus,CreatedAtUtc);
