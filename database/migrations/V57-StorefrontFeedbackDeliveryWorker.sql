/* V57: durable leases and retry scheduling for V56 feedback deliveries. */
IF OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries', N'U') IS NULL
    THROW 57001, N'V56 feedback delivery table is required before V57.', 1;

IF COL_LENGTH(N'commerce.StorefrontFeedbackNotificationDeliveries', N'NextAttemptAtUtc') IS NULL
    ALTER TABLE commerce.StorefrontFeedbackNotificationDeliveries ADD NextAttemptAtUtc DATETIMEOFFSET NULL;
GO
IF COL_LENGTH(N'commerce.StorefrontFeedbackNotificationDeliveries', N'LeaseUntilUtc') IS NULL
    ALTER TABLE commerce.StorefrontFeedbackNotificationDeliveries ADD LeaseUntilUtc DATETIMEOFFSET NULL;
GO
IF COL_LENGTH(N'commerce.StorefrontFeedbackNotificationDeliveries', N'ErrorMessage') IS NULL
    ALTER TABLE commerce.StorefrontFeedbackNotificationDeliveries ADD ErrorMessage NVARCHAR(500) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_StorefrontFeedbackNotificationDeliveries_Status' AND parent_object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries'))
    ALTER TABLE commerce.StorefrontFeedbackNotificationDeliveries DROP CONSTRAINT CK_StorefrontFeedbackNotificationDeliveries_Status;
ALTER TABLE commerce.StorefrontFeedbackNotificationDeliveries ADD CONSTRAINT CK_StorefrontFeedbackNotificationDeliveries_Status CHECK(Status IN(N'PENDING',N'PROCESSING',N'SENT',N'FAILED',N'SKIPPED'));
GO

UPDATE commerce.StorefrontFeedbackNotificationDeliveries
SET NextAttemptAtUtc=COALESCE(NextAttemptAtUtc,CreatedAtUtc)
WHERE Status=N'PENDING' AND NextAttemptAtUtc IS NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackNotificationDeliveries_Ready' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries'))
    CREATE INDEX IX_StorefrontFeedbackNotificationDeliveries_Ready ON commerce.StorefrontFeedbackNotificationDeliveries(Status,NextAttemptAtUtc,LeaseUntilUtc,CreatedAtUtc);
