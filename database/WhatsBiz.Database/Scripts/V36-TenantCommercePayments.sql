SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.TenantPaymentConfigurations',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.TenantPaymentConfigurations(
  TenantId uniqueidentifier NOT NULL CONSTRAINT PK_TenantPaymentConfigurations PRIMARY KEY,
  OnlinePaymentEnabled bit NOT NULL CONSTRAINT DF_TenantPaymentConfigurations_Online DEFAULT(0),
  CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_TenantPaymentConfigurations_Created DEFAULT(SYSUTCDATETIME()),
  CreatedBy nvarchar(256) NULL,UpdatedAt datetimeoffset NULL,UpdatedBy nvarchar(256) NULL,
  CONSTRAINT FK_TenantPaymentConfigurations_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId)
 );
END;

IF OBJECT_ID(N'commerce.TenantPaymentProviders',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.TenantPaymentProviders(
  PaymentProviderId uniqueidentifier NOT NULL CONSTRAINT PK_TenantPaymentProviders PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL,
  Provider nvarchar(30) NOT NULL,
  IsEnabled bit NOT NULL CONSTRAINT DF_TenantPaymentProviders_Enabled DEFAULT(0),
  IsDefault bit NOT NULL CONSTRAINT DF_TenantPaymentProviders_Default DEFAULT(0),
  KeyId nvarchar(200) NULL, KeySecretProtected nvarchar(max) NULL, WebhookSecretProtected nvarchar(max) NULL,
  IsTestMode bit NOT NULL CONSTRAINT DF_TenantPaymentProviders_Test DEFAULT(1),
  UpiVpa nvarchar(255) NULL, PayeeName nvarchar(200) NULL,
  CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_TenantPaymentProviders_Created DEFAULT(SYSUTCDATETIME()),
  CreatedBy nvarchar(256) NULL, UpdatedAt datetimeoffset NULL, UpdatedBy nvarchar(256) NULL,
  CONSTRAINT FK_TenantPaymentProviders_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT CK_TenantPaymentProviders_Provider CHECK(Provider IN(N'RAZORPAY',N'DIRECT_UPI',N'COD')),
  CONSTRAINT CK_TenantPaymentProviders_Configuration CHECK(
   (Provider=N'RAZORPAY' AND UpiVpa IS NULL AND PayeeName IS NULL) OR
   (Provider=N'DIRECT_UPI' AND KeyId IS NULL AND KeySecretProtected IS NULL AND WebhookSecretProtected IS NULL) OR
   (Provider=N'COD' AND KeyId IS NULL AND KeySecretProtected IS NULL AND WebhookSecretProtected IS NULL AND UpiVpa IS NULL AND PayeeName IS NULL))
 );
 CREATE UNIQUE INDEX UX_TenantPaymentProviders_TenantProvider ON commerce.TenantPaymentProviders(TenantId,Provider);
 CREATE UNIQUE INDEX UX_TenantPaymentProviders_OneDefault ON commerce.TenantPaymentProviders(TenantId) WHERE IsDefault=1;
END;

IF OBJECT_ID(N'commerce.CommercePayments',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.CommercePayments(
  PaymentId uniqueidentifier NOT NULL CONSTRAINT PK_CommercePayments PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL, InvoiceId uniqueidentifier NOT NULL,
  Provider nvarchar(30) NOT NULL, PaymentMethod nvarchar(30) NOT NULL,
  Amount decimal(18,2) NOT NULL, Currency char(3) NOT NULL,
  Status nvarchar(30) NOT NULL, AttemptNumber int NOT NULL,
  ProviderOrderId nvarchar(100) NULL, ProviderPaymentId nvarchar(100) NULL,
  ProviderReference nvarchar(100) NULL, PaymentLink nvarchar(1000) NULL,
  TransactionReference nvarchar(100) NOT NULL,
  CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_CommercePayments_Created DEFAULT(SYSUTCDATETIME()),
  CreatedBy nvarchar(256) NULL, UpdatedAt datetimeoffset NULL,
  PaidAt datetimeoffset NULL, FailedAt datetimeoffset NULL, VerifiedAt datetimeoffset NULL,
  VerifiedBy uniqueidentifier NULL, AppliedAt datetimeoffset NULL,
  CONSTRAINT FK_CommercePayments_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT FK_CommercePayments_Invoice FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId),
  CONSTRAINT CK_CommercePayments_Provider CHECK(Provider IN(N'RAZORPAY',N'DIRECT_UPI',N'COD')),
  CONSTRAINT CK_CommercePayments_Status CHECK(Status IN(N'PENDING',N'PENDING_VERIFICATION',N'PAID',N'FAILED',N'CANCELLED',N'REFUNDED',N'COD_PENDING')),
  CONSTRAINT CK_CommercePayments_Amount CHECK(Amount>0),
  CONSTRAINT CK_CommercePayments_Attempt CHECK(AttemptNumber>0)
 );
 CREATE UNIQUE INDEX UX_CommercePayments_TenantInvoiceAttempt ON commerce.CommercePayments(TenantId,InvoiceId,AttemptNumber);
 CREATE UNIQUE INDEX UX_CommercePayments_TransactionReference ON commerce.CommercePayments(TransactionReference);
 CREATE UNIQUE INDEX UX_CommercePayments_ProviderOrder ON commerce.CommercePayments(Provider,ProviderOrderId) WHERE ProviderOrderId IS NOT NULL;
 CREATE UNIQUE INDEX UX_CommercePayments_ProviderReference ON commerce.CommercePayments(Provider,ProviderReference) WHERE ProviderReference IS NOT NULL;
 CREATE UNIQUE INDEX UX_CommercePayments_OneAppliedSettlement ON commerce.CommercePayments(TenantId,InvoiceId) WHERE AppliedAt IS NOT NULL;
 CREATE INDEX IX_CommercePayments_TenantCreated ON commerce.CommercePayments(TenantId,CreatedAt DESC) INCLUDE(InvoiceId,Provider,Amount,Currency,Status);
 CREATE INDEX IX_CommercePayments_TenantStatus ON commerce.CommercePayments(TenantId,Status,CreatedAt DESC);
END;

IF OBJECT_ID(N'commerce.PaymentProviderEvents',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.PaymentProviderEvents(
  PaymentProviderEventId uniqueidentifier NOT NULL CONSTRAINT PK_PaymentProviderEvents PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL, PaymentId uniqueidentifier NOT NULL,
  Provider nvarchar(30) NOT NULL, ProviderEventId nvarchar(200) NOT NULL,
  EventType nvarchar(100) NULL, PayloadHash char(64) NOT NULL,
  ReceivedAt datetimeoffset NOT NULL CONSTRAINT DF_PaymentProviderEvents_Received DEFAULT(SYSUTCDATETIME()),
  ProcessedAt datetimeoffset NULL,
  CONSTRAINT FK_PaymentProviderEvents_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT FK_PaymentProviderEvents_Payment FOREIGN KEY(PaymentId) REFERENCES commerce.CommercePayments(PaymentId),
  CONSTRAINT CK_PaymentProviderEvents_Provider CHECK(Provider=N'RAZORPAY')
 );
 CREATE UNIQUE INDEX UX_PaymentProviderEvents_ProviderEvent ON commerce.PaymentProviderEvents(Provider,ProviderEventId);
 CREATE INDEX IX_PaymentProviderEvents_TenantPayment ON commerce.PaymentProviderEvents(TenantId,PaymentId,ReceivedAt DESC);
END;

IF OBJECT_ID(N'commerce.PaymentApplications',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.PaymentApplications(
  PaymentApplicationId uniqueidentifier NOT NULL CONSTRAINT PK_PaymentApplications PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL, PaymentId uniqueidentifier NOT NULL, InvoiceId uniqueidentifier NOT NULL,
  Amount decimal(18,2) NOT NULL, Currency char(3) NOT NULL,
  AppliedAt datetimeoffset NOT NULL CONSTRAINT DF_PaymentApplications_Applied DEFAULT(SYSUTCDATETIME()),
  AppliedBy nvarchar(256) NULL,
  CONSTRAINT FK_PaymentApplications_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT FK_PaymentApplications_Payment FOREIGN KEY(PaymentId) REFERENCES commerce.CommercePayments(PaymentId),
  CONSTRAINT FK_PaymentApplications_Invoice FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId)
 );
 CREATE UNIQUE INDEX UX_PaymentApplications_TenantPayment ON commerce.PaymentApplications(TenantId,PaymentId);
 CREATE UNIQUE INDEX UX_PaymentApplications_TenantInvoice ON commerce.PaymentApplications(TenantId,InvoiceId);
END;
GO

CREATE OR ALTER TRIGGER commerce.TR_CommercePayments_TenantOwnership ON commerce.CommercePayments AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @session uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @session IS NULL OR EXISTS(SELECT 1 FROM (SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted)x WHERE x.TenantId<>@session) THROW 51812,N'Commerce payment tenant context is missing or mismatched.',1;
 IF EXISTS(SELECT 1 FROM inserted p LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=p.InvoiceId AND i.TenantId=p.TenantId WHERE i.InvoiceId IS NULL)
  THROW 51810,N'Commerce payment invoice ownership is invalid.',1;
 IF EXISTS(SELECT 1 FROM inserted p LEFT JOIN core.Users u ON u.Id=p.VerifiedBy AND u.TenantId=p.TenantId WHERE p.VerifiedBy IS NOT NULL AND u.Id IS NULL)
  THROW 51813,N'Commerce payment verifier ownership is invalid.',1;
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_PaymentProviders_TenantGuard ON commerce.TenantPaymentProviders AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @session uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @session IS NULL OR EXISTS(SELECT 1 FROM (SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted)x WHERE x.TenantId<>@session)
  THROW 51811,N'Tenant payment configuration context is missing or mismatched.',1;
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_PaymentConfigurations_TenantGuard ON commerce.TenantPaymentConfigurations AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON; DECLARE @session uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @session IS NULL OR EXISTS(SELECT 1 FROM (SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted)x WHERE x.TenantId<>@session) THROW 51818,N'Tenant payment settings context is missing or mismatched.',1;
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_PaymentProviderEvents_TenantGuard ON commerce.PaymentProviderEvents AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON; DECLARE @session uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @session IS NULL OR EXISTS(SELECT 1 FROM (SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted)x WHERE x.TenantId<>@session) THROW 51814,N'Payment event tenant context is missing or mismatched.',1;
 IF EXISTS(SELECT 1 FROM inserted e LEFT JOIN commerce.CommercePayments p ON p.PaymentId=e.PaymentId AND p.TenantId=e.TenantId WHERE p.PaymentId IS NULL) THROW 51815,N'Payment event ownership is invalid.',1;
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_PaymentApplications_TenantGuard ON commerce.PaymentApplications AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON; DECLARE @session uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @session IS NULL OR EXISTS(SELECT 1 FROM (SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted)x WHERE x.TenantId<>@session) THROW 51816,N'Payment application tenant context is missing or mismatched.',1;
 IF EXISTS(SELECT 1 FROM inserted a LEFT JOIN commerce.CommercePayments p ON p.PaymentId=a.PaymentId AND p.TenantId=a.TenantId AND p.InvoiceId=a.InvoiceId WHERE p.PaymentId IS NULL) THROW 51817,N'Payment application ownership is invalid.',1;
END;
GO

MERGE sales.PaymentMethods t USING(VALUES(N'RAZORPAY',N'Razorpay',1,7))s(MethodCode,MethodName,RequiresReference,DisplayOrder)
 ON t.MethodCode=s.MethodCode WHEN MATCHED THEN UPDATE SET MethodName=s.MethodName,RequiresReference=s.RequiresReference,DisplayOrder=s.DisplayOrder,IsActive=1
 WHEN NOT MATCHED THEN INSERT(MethodCode,MethodName,RequiresReference,DisplayOrder)VALUES(s.MethodCode,s.MethodName,s.RequiresReference,s.DisplayOrder);
MERGE finance.PaymentModes t USING(VALUES(N'RAZORPAY',N'Razorpay',N'BANK'))s(ModeCode,ModeName,BookType)
 ON t.ModeCode=s.ModeCode WHEN MATCHED THEN UPDATE SET ModeName=s.ModeName,BookType=s.BookType,IsActive=1
 WHEN NOT MATCHED THEN INSERT(PaymentModeId,ModeCode,ModeName,BookType,IsActive)VALUES(NEWID(),s.ModeCode,s.ModeName,s.BookType,1);
GO
