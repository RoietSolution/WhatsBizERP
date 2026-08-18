/* RC-DEV-010 safe, rerunnable post-deployment additions. */
IF OBJECT_ID(N'integration.CustomerNotifications',N'U') IS NULL
BEGIN
    CREATE TABLE integration.CustomerNotifications
    (
      CustomerNotificationId uniqueidentifier NOT NULL CONSTRAINT DF_CustomerNotifications_Id DEFAULT NEWSEQUENTIALID(),
      CustomerId uniqueidentifier NOT NULL, DocumentId uniqueidentifier NOT NULL, DocumentType nvarchar(40) NOT NULL,
      EventType nvarchar(50) NOT NULL, Channel nvarchar(20) NOT NULL, Recipient nvarchar(30) NULL,
      MessageTemplate nvarchar(4000) NOT NULL, Message nvarchar(4000) NOT NULL,
      Status nvarchar(20) NOT NULL CONSTRAINT DF_CustomerNotifications_Status DEFAULT N'PENDING',
      ProviderMessageId nvarchar(200) NULL, ErrorMessage nvarchar(1000) NULL,
      AttemptCount int NOT NULL CONSTRAINT DF_CustomerNotifications_Attempts DEFAULT 0,
      CreatedOn datetimeoffset(7) NOT NULL CONSTRAINT DF_CustomerNotifications_CreatedOn DEFAULT SYSUTCDATETIME(),
      SentOn datetimeoffset(7) NULL, LastAttemptOn datetimeoffset(7) NULL, NextAttemptOn datetimeoffset(7) NULL, ModifiedBy nvarchar(256) NULL,
      CONSTRAINT PK_CustomerNotifications PRIMARY KEY(CustomerNotificationId),
      CONSTRAINT FK_CustomerNotifications_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
      CONSTRAINT FK_CustomerNotifications_Invoice FOREIGN KEY(DocumentId) REFERENCES sales.SalesInvoices(InvoiceId),
      CONSTRAINT CK_CustomerNotifications_Channel CHECK(Channel IN(N'WHATSAPP',N'SMS')),
      CONSTRAINT CK_CustomerNotifications_Status CHECK(Status IN(N'PENDING',N'PROCESSING',N'SENT',N'FAILED')),
      CONSTRAINT CK_CustomerNotifications_Attempts CHECK(AttemptCount BETWEEN 0 AND 3),
      CONSTRAINT UQ_CustomerNotifications_Event UNIQUE(DocumentId,DocumentType,CustomerId,Channel,EventType)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.CustomerNotifications') AND name=N'IX_CustomerNotifications_Work')
 CREATE INDEX IX_CustomerNotifications_Work ON integration.CustomerNotifications(Status,NextAttemptOn,CreatedOn) INCLUDE(Channel,Recipient,AttemptCount);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.CustomerNotifications') AND name=N'IX_CustomerNotifications_History')
 CREATE INDEX IX_CustomerNotifications_History ON integration.CustomerNotifications(CreatedOn DESC) INCLUDE(CustomerId,DocumentId,Channel,Status,AttemptCount);

DECLARE @CompanyId uniqueidentifier=(SELECT TOP(1) CompanyId FROM admin.Companies WHERE IsActive=1 ORDER BY CreatedOn);
IF @CompanyId IS NOT NULL
BEGIN
 DECLARE @Defaults table(SettingKey nvarchar(100),SettingValue nvarchar(max),DataType nvarchar(30));
 INSERT @Defaults VALUES
 (N'CustomerNotifications.Enabled',N'False',N'Boolean'),(N'CustomerNotifications.WhatsApp.Enabled',N'False',N'Boolean'),(N'CustomerNotifications.Sms.Enabled',N'False',N'Boolean'),
 (N'CustomerNotifications.Events.SuccessfulSale',N'True',N'Boolean'),(N'CustomerNotifications.Events.SuccessfulPayment',N'True',N'Boolean'),
 (N'CustomerNotifications.WhatsApp.Template',N'Thank you for shopping with {{company_name}}!'+NCHAR(10)+NCHAR(10)+N'Invoice: {{invoice_no}}'+NCHAR(10)+N'Amount: {{currency}}{{total_amount}}'+NCHAR(10)+NCHAR(10)+N'We appreciate your business.'+NCHAR(10)+N'Visit us again!',N'String'),
 (N'CustomerNotifications.Sms.Template',N'Thank you for shopping with {{company_name}}. Invoice {{invoice_no}}, Amount {{currency}}{{total_amount}}. We appreciate your business.',N'String');
 INSERT admin.ApplicationSettings(CompanyId,SettingKey,SettingValue,DataType,Category)
 SELECT @CompanyId,d.SettingKey,d.SettingValue,d.DataType,N'Customer Notifications' FROM @Defaults d
 WHERE NOT EXISTS(SELECT 1 FROM admin.ApplicationSettings s WHERE s.CompanyId=@CompanyId AND s.SettingKey=d.SettingKey);
END;
