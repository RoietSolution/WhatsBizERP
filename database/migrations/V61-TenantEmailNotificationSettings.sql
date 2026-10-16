/* V61: tenant-specific sender settings for storefront order notification email. */
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'AutomatedEmailMode') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD AutomatedEmailMode varchar(24) NOT NULL CONSTRAINT DF_StorefrontConfigurations_AutomatedEmailMode DEFAULT('KHATADHARI_DEFAULT') WITH VALUES;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'NotificationSenderDisplayName') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD NotificationSenderDisplayName nvarchar(150) NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'NotificationReplyToEmail') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD NotificationReplyToEmail nvarchar(256) NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'CustomSmtpHost') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD CustomSmtpHost nvarchar(255) NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'CustomSmtpPort') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD CustomSmtpPort int NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'CustomSmtpSecurityMode') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD CustomSmtpSecurityMode varchar(16) NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'CustomSmtpUsername') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD CustomSmtpUsername nvarchar(256) NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'CustomSmtpPasswordProtected') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD CustomSmtpPasswordProtected nvarchar(4000) NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'CustomSmtpFromEmail') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD CustomSmtpFromEmail nvarchar(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_StorefrontConfigurations_AutomatedEmailMode' AND parent_object_id=OBJECT_ID(N'commerce.StorefrontConfigurations'))
    EXEC sys.sp_executesql N'ALTER TABLE commerce.StorefrontConfigurations ADD CONSTRAINT CK_StorefrontConfigurations_AutomatedEmailMode CHECK (AutomatedEmailMode IN (''KHATADHARI_DEFAULT'',''CUSTOM_SMTP''));';
