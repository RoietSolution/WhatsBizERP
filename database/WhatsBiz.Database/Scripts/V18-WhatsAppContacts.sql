SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

IF SCHEMA_ID(N'integration') IS NULL EXEC(N'CREATE SCHEMA integration AUTHORIZATION dbo');

IF OBJECT_ID(N'integration.WhatsAppContacts',N'U') IS NULL
CREATE TABLE integration.WhatsAppContacts(
    WhatsAppContactId uniqueidentifier NOT NULL CONSTRAINT PK_WhatsAppContacts PRIMARY KEY CONSTRAINT DF_WhatsAppContacts_Id DEFAULT NEWSEQUENTIALID(),
    TenantId uniqueidentifier NOT NULL,
    NormalizedMobile nvarchar(15) NOT NULL,
    DisplayMobile nvarchar(20) NOT NULL,
    ProfileName nvarchar(250) NULL,
    Status nvarchar(20) NOT NULL CONSTRAINT DF_WhatsAppContacts_Status DEFAULT(N'NEW'),
    CustomerId uniqueidentifier NULL,
    FirstMessageAt datetimeoffset NOT NULL,
    LastMessageAt datetimeoffset NOT NULL,
    MessageCount int NOT NULL CONSTRAINT DF_WhatsAppContacts_MessageCount DEFAULT(1),
    LastMessageType nvarchar(30) NULL,
    LastMetaMessageId nvarchar(200) NULL,
    CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppContacts_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppContacts_Updated DEFAULT SYSUTCDATETIME(),
    RowVersion rowversion NOT NULL,
    CONSTRAINT FK_WhatsAppContacts_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
    CONSTRAINT FK_WhatsAppContacts_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
    CONSTRAINT UQ_WhatsAppContacts_TenantMobile UNIQUE(TenantId,NormalizedMobile),
    CONSTRAINT CK_WhatsAppContacts_Mobile CHECK(LEN(NormalizedMobile) BETWEEN 8 AND 15 AND NormalizedMobile NOT LIKE N'%[^0-9]%'),
    CONSTRAINT CK_WhatsAppContacts_Status CHECK(Status IN(N'NEW',N'MATCHED',N'CONVERTED')),
    CONSTRAINT CK_WhatsAppContacts_MessageCount CHECK(MessageCount>=1)
);

IF OBJECT_ID(N'integration.WhatsAppContactEvents',N'U') IS NULL
CREATE TABLE integration.WhatsAppContactEvents(
    EventId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_WhatsAppContactEvents PRIMARY KEY,
    TenantId uniqueidentifier NOT NULL,
    WhatsAppContactId uniqueidentifier NOT NULL,
    EventType nvarchar(30) NOT NULL,
    PreviousCustomerId uniqueidentifier NULL,
    CustomerId uniqueidentifier NULL,
    Actor nvarchar(256) NULL,
    CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppContactEvents_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_WhatsAppContactEvents_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
    CONSTRAINT FK_WhatsAppContactEvents_Contact FOREIGN KEY(WhatsAppContactId) REFERENCES integration.WhatsAppContacts(WhatsAppContactId),
    CONSTRAINT FK_WhatsAppContactEvents_PreviousCustomer FOREIGN KEY(PreviousCustomerId) REFERENCES sales.Customers(CustomerId),
    CONSTRAINT FK_WhatsAppContactEvents_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
    CONSTRAINT CK_WhatsAppContactEvents_Type CHECK(EventType IN(N'CREATED',N'AUTO_MATCHED',N'LINKED'))
);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppContacts') AND name=N'IX_WhatsAppContacts_TenantLastMessage')
    CREATE INDEX IX_WhatsAppContacts_TenantLastMessage ON integration.WhatsAppContacts(TenantId,LastMessageAt DESC) INCLUDE(Status,CustomerId,DisplayMobile,ProfileName,MessageCount);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppContacts') AND name=N'IX_WhatsAppContacts_TenantStatus')
    CREATE INDEX IX_WhatsAppContacts_TenantStatus ON integration.WhatsAppContacts(TenantId,Status,LastMessageAt DESC) INCLUDE(CustomerId,DisplayMobile,ProfileName);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppContacts') AND name=N'IX_WhatsAppContacts_TenantCustomer')
    CREATE INDEX IX_WhatsAppContacts_TenantCustomer ON integration.WhatsAppContacts(TenantId,CustomerId) WHERE CustomerId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppContactEvents') AND name=N'IX_WhatsAppContactEvents_Contact')
    CREATE INDEX IX_WhatsAppContactEvents_Contact ON integration.WhatsAppContactEvents(TenantId,WhatsAppContactId,CreatedAt DESC);

COMMIT TRANSACTION;
