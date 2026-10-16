IF COL_LENGTH(N'commerce.StorefrontFeedbackNotificationDeliveries',N'NextAttemptAtUtc') IS NULL THROW 57002,N'V57 retry schedule column is missing.',1;
IF COL_LENGTH(N'commerce.StorefrontFeedbackNotificationDeliveries',N'LeaseUntilUtc') IS NULL THROW 57003,N'V57 delivery lease column is missing.',1;
IF COL_LENGTH(N'commerce.StorefrontFeedbackNotificationDeliveries',N'ErrorMessage') IS NULL THROW 57004,N'V57 delivery error column is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_StorefrontFeedbackNotificationDeliveries_Status' AND parent_object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries')) THROW 57005,N'V57 delivery status constraint is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackNotificationDeliveries_Ready' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackNotificationDeliveries')) THROW 57006,N'V57 ready-delivery index is missing.',1;
SELECT N'V57 Storefront feedback delivery worker schema validated.' AS ValidationMessage;
