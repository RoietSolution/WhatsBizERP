/* V52 - tenant-scoped storefront demand, availability requests and notification outbox. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.ProductSearchEvents',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.ProductSearchEvents(
  SearchEventId uniqueidentifier NOT NULL CONSTRAINT PK_ProductSearchEvents PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL, CustomerId uniqueidentifier NULL, AnonymousSessionId nvarchar(64) NULL,
  OriginalKeyword nvarchar(200) NOT NULL, NormalizedKeyword nvarchar(200) NOT NULL,
  ResultCount int NOT NULL, InStockResultCount int NOT NULL, OutOfStockResultCount int NOT NULL,
  Outcome nvarchar(30) NOT NULL, Source nvarchar(40) NOT NULL CONSTRAINT DF_ProductSearchEvents_Source DEFAULT N'CUSTOMER_STOREFRONT',
  CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_ProductSearchEvents_Created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT FK_ProductSearchEvents_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT CK_ProductSearchEvents_Outcome CHECK(Outcome IN(N'RESULTS_AVAILABLE',N'RESULTS_OUT_OF_STOCK',N'NO_RESULTS',N'MIXED_RESULTS'))
 );
 CREATE INDEX IX_ProductSearchEvents_TenantDate ON commerce.ProductSearchEvents(TenantId,CreatedAtUtc,Outcome);
 CREATE INDEX IX_ProductSearchEvents_TenantKeyword ON commerce.ProductSearchEvents(TenantId,NormalizedKeyword,CreatedAtUtc);
END;

IF OBJECT_ID(N'commerce.ProductAvailabilityRequests',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.ProductAvailabilityRequests(
  AvailabilityRequestId uniqueidentifier NOT NULL CONSTRAINT PK_ProductAvailabilityRequests PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL, ProductId uniqueidentifier NOT NULL, CustomerId uniqueidentifier NULL,
  ContactMobile nvarchar(30) NULL, PreferredChannel nvarchar(20) NOT NULL CONSTRAINT DF_ProductAvailabilityRequests_Channel DEFAULT N'WHATSAPP',
  ConsentGiven bit NOT NULL CONSTRAINT DF_ProductAvailabilityRequests_Consent DEFAULT(0), RequestStatus nvarchar(20) NOT NULL CONSTRAINT DF_ProductAvailabilityRequests_Status DEFAULT N'ACTIVE',
  RequestedAtUtc datetime2 NOT NULL CONSTRAINT DF_ProductAvailabilityRequests_Requested DEFAULT SYSUTCDATETIME(), NotifiedAtUtc datetime2 NULL, CancelledAtUtc datetime2 NULL, LastAvailabilityCheckedAtUtc datetime2 NULL,
  ContactIdentity AS (ISNULL(CONVERT(nvarchar(36),CustomerId),N'')+N'|'+ISNULL(ContactMobile,N'')) PERSISTED,
  CONSTRAINT FK_ProductAvailabilityRequests_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT FK_ProductAvailabilityRequests_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId),
  CONSTRAINT CK_ProductAvailabilityRequests_Status CHECK(RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING',N'NOTIFIED',N'CANCELLED',N'EXPIRED')),
  CONSTRAINT CK_ProductAvailabilityRequests_Channel CHECK(PreferredChannel IN(N'WHATSAPP',N'SMS',N'EMAIL'))
 );
 CREATE UNIQUE INDEX UX_ProductAvailabilityRequests_Active ON commerce.ProductAvailabilityRequests(TenantId,ProductId,ContactIdentity) WHERE RequestStatus IN(N'ACTIVE',N'NOTIFICATION_PENDING');
 CREATE INDEX IX_ProductAvailabilityRequests_Due ON commerce.ProductAvailabilityRequests(TenantId,RequestStatus,ProductId);
END;

IF OBJECT_ID(N'commerce.MissingProductRequests',N'U') IS NULL
BEGIN
 CREATE TABLE commerce.MissingProductRequests(
  MissingProductRequestId uniqueidentifier NOT NULL CONSTRAINT PK_MissingProductRequests PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL, SearchEventId uniqueidentifier NULL, CustomerId uniqueidentifier NULL, AnonymousSessionId nvarchar(64) NULL,
  RequestedKeyword nvarchar(200) NOT NULL, NormalizedKeyword nvarchar(200) NOT NULL, ContactMobile nvarchar(30) NULL, Remarks nvarchar(500) NULL,
  RequestStatus nvarchar(20) NOT NULL CONSTRAINT DF_MissingProductRequests_Status DEFAULT N'NEW', CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_MissingProductRequests_Created DEFAULT SYSUTCDATETIME(), UpdatedAtUtc datetime2 NOT NULL CONSTRAINT DF_MissingProductRequests_Updated DEFAULT SYSUTCDATETIME(),
  ContactIdentity AS (ISNULL(CONVERT(nvarchar(36),CustomerId),N'')+N'|'+ISNULL(ContactMobile,N'')) PERSISTED,
  CONSTRAINT FK_MissingProductRequests_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT FK_MissingProductRequests_Search FOREIGN KEY(SearchEventId) REFERENCES commerce.ProductSearchEvents(SearchEventId),
  CONSTRAINT CK_MissingProductRequests_Status CHECK(RequestStatus IN(N'NEW',N'REVIEWING',N'SOURCING',N'AVAILABLE',N'CLOSED'))
 );
 CREATE UNIQUE INDEX UX_MissingProductRequests_Active ON commerce.MissingProductRequests(TenantId,NormalizedKeyword,ContactIdentity) WHERE RequestStatus IN(N'NEW',N'REVIEWING',N'SOURCING');
 CREATE INDEX IX_MissingProductRequests_TenantStatus ON commerce.MissingProductRequests(TenantId,RequestStatus,CreatedAtUtc);
END;
