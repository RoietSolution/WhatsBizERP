IF OBJECT_ID(N'commerce.StorefrontFeedbackNotifications',N'U') IS NULL THROW 56001,N'V56 feedback notifications table is missing.',1;
IF OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries',N'U') IS NULL THROW 56002,N'V56 feedback delivery table is missing.',1;
IF OBJECT_ID(N'commerce.StorefrontFeedbackAttachments',N'U') IS NULL THROW 56003,N'V56 feedback attachments table is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontFeedbackNotifications_Idempotency' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotifications')) THROW 56004,N'V56 notification idempotency index is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontFeedbackNotificationDeliveries_Channel' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries')) THROW 56005,N'V56 delivery uniqueness index is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackAttachments_Ticket' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackAttachments')) THROW 56006,N'V56 attachment index is missing.',1;
SELECT N'V56 Storefront feedback notifications and attachments validated.' AS ValidationMessage;
