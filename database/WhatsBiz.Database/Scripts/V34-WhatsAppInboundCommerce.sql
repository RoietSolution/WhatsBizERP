SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'integration.WhatsAppCommerceConversations',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppCommerceConversations
    (
        ConversationId uniqueidentifier NOT NULL CONSTRAINT PK_WhatsAppCommerceConversations PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        WhatsAppContactId uniqueidentifier NULL,
        CustomerId uniqueidentifier NULL,
        NormalizedMobile nvarchar(15) NOT NULL,
        WarehouseId uniqueidentifier NULL,
        State nvarchar(30) NOT NULL CONSTRAINT DF_WhatsAppCommerceConversations_State DEFAULT(N'MENU'),
        CartStatus nvarchar(20) NOT NULL CONSTRAINT DF_WhatsAppCommerceConversations_CartStatus DEFAULT(N'OPEN'),
        PendingOrderStatus nvarchar(20) NOT NULL CONSTRAINT DF_WhatsAppCommerceConversations_OrderStatus DEFAULT(N'NONE'),
        OrderId uniqueidentifier NULL,
        LastInboundMessageId nvarchar(250) NULL,
        LastResult nvarchar(500) NULL,
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceConversations_Created DEFAULT SYSUTCDATETIME(),
        UpdatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceConversations_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_WhatsAppCommerceConversations_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_WhatsAppCommerceConversations_Contact FOREIGN KEY(WhatsAppContactId) REFERENCES integration.WhatsAppContacts(WhatsAppContactId),
        CONSTRAINT FK_WhatsAppCommerceConversations_Customer FOREIGN KEY(CustomerId) REFERENCES sales.Customers(CustomerId),
        CONSTRAINT UQ_WhatsAppCommerceConversations_TenantMobile UNIQUE(TenantId,NormalizedMobile),
        CONSTRAINT CK_WhatsAppCommerceConversations_State CHECK(State IN(N'MENU',N'BROWSE',N'SEARCH',N'CART',N'CONFIRM')),
        CONSTRAINT CK_WhatsAppCommerceConversations_OrderStatus CHECK(PendingOrderStatus IN(N'NONE',N'PENDING',N'PROCESSING',N'COMPLETED',N'FAILED'))
    );
END;
IF OBJECT_ID(N'integration.WhatsAppCommerceCartLines',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppCommerceCartLines
    (
        ConversationId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        Quantity decimal(18,4) NOT NULL,
        UnitPrice decimal(18,4) NOT NULL,
        TaxPercentage decimal(9,4) NOT NULL,
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceCartLines_Created DEFAULT SYSUTCDATETIME(),
        UpdatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceCartLines_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WhatsAppCommerceCartLines PRIMARY KEY(ConversationId,ProductId),
        CONSTRAINT FK_WhatsAppCommerceCartLines_Conversation FOREIGN KEY(ConversationId) REFERENCES integration.WhatsAppCommerceConversations(ConversationId),
        CONSTRAINT FK_WhatsAppCommerceCartLines_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_WhatsAppCommerceCartLines_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId),
        CONSTRAINT CK_WhatsAppCommerceCartLines_Quantity CHECK(Quantity>0)
    );
END;
IF OBJECT_ID(N'integration.WhatsAppCommerceInbound',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppCommerceInbound
    (
        TenantId uniqueidentifier NOT NULL,
        MetaMessageId nvarchar(250) NOT NULL,
        ConversationId uniqueidentifier NULL,
        Status nvarchar(20) NOT NULL,
        Action nvarchar(40) NULL,
        ResultMessage nvarchar(500) NULL,
        Attempts int NOT NULL CONSTRAINT DF_WhatsAppCommerceInbound_Attempts DEFAULT(0),
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceInbound_Created DEFAULT SYSUTCDATETIME(),
        UpdatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceInbound_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WhatsAppCommerceInbound PRIMARY KEY(TenantId,MetaMessageId),
        CONSTRAINT CK_WhatsAppCommerceInbound_Status CHECK(Status IN(N'RECEIVED',N'PROCESSING',N'PROCESSED',N'FAILED'))
    );
END;
IF OBJECT_ID(N'integration.WhatsAppCommerceOutbound',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppCommerceOutbound
    (
        WhatsAppCommerceOutboundId uniqueidentifier NOT NULL CONSTRAINT PK_WhatsAppCommerceOutbound PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        MetaMessageId nvarchar(250) NOT NULL,
        RecipientNumber nvarchar(20) NOT NULL,
        MessageText nvarchar(max) NOT NULL,
        Status nvarchar(20) NOT NULL,
        ProviderMessageId nvarchar(250) NULL,
        Attempts int NOT NULL CONSTRAINT DF_WhatsAppCommerceOutbound_Attempts DEFAULT(0),
        LastError nvarchar(500) NULL,
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceOutbound_Created DEFAULT SYSUTCDATETIME(),
        UpdatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceOutbound_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_WhatsAppCommerceOutbound_Message UNIQUE(TenantId,MetaMessageId,RecipientNumber),
        CONSTRAINT CK_WhatsAppCommerceOutbound_Status CHECK(Status IN(N'PENDING',N'SENT',N'FAILED'))
    );
END;
COMMIT TRANSACTION;
