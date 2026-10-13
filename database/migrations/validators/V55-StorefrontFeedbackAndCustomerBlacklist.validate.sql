IF OBJECT_ID(N'commerce.StorefrontFeedbackTickets',N'U') IS NULL THROW 52155,N'V55 feedback ticket table is missing.',1;
IF OBJECT_ID(N'commerce.StorefrontFeedbackTicketActivity',N'U') IS NULL THROW 52156,N'V55 feedback activity table is missing.',1;
IF OBJECT_ID(N'commerce.StorefrontCustomerPhoneBlacklist',N'U') IS NULL THROW 52157,N'V55 customer blacklist table is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StorefrontFeedbackTickets') AND name=N'UX_StorefrontFeedbackTickets_Reference' AND is_unique=1) THROW 52158,N'V55 ticket reference uniqueness is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StorefrontCustomerPhoneBlacklist') AND name=N'UX_StorefrontCustomerPhoneBlacklist_ActivePhone' AND is_unique=1) THROW 52159,N'V55 active phone blacklist uniqueness is missing.',1;
IF (SELECT COUNT(1) FROM core.Features WHERE FeatureKey IN(N'STOREFRONT_FEEDBACK',N'STOREFRONT_CUSTOMER_BLACKLIST'))<>2 THROW 52160,N'V55 feature catalog entries are missing.',1;
