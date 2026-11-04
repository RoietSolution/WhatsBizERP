/* Durable send claims and per-message retailer consent attestations. No product/price/order changes. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'integration.WhatsAppConfigurations',N'U') IS NULL OR OBJECT_ID(N'integration.WhatsAppContacts',N'U') IS NULL
    THROW 70000,N'V70 requires the existing WhatsApp integration and contacts schema.',1;
IF OBJECT_ID(N'integration.WhatsAppProductShareRequests',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppProductShareRequests(
        TenantId uniqueidentifier NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        ConsentConfirmedBy uniqueidentifier NOT NULL,
        ConsentConfirmedAtUtc datetime2(7) NOT NULL,
        Status varchar(16) NOT NULL,
        ProviderMode varchar(16) NOT NULL,
        ProviderMessageId nvarchar(256) NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_ProductShare_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WhatsAppProductShareRequests PRIMARY KEY(TenantId,RequestId),
        CONSTRAINT FK_ProductShare_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_ProductShare_Status CHECK(Status IN('PENDING','ACCEPTED','UNCONFIRMED')),
        CONSTRAINT CK_ProductShare_Mode CHECK(ProviderMode IN('MOCK','META_TEST','LIVE'))
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppProductShareRequests') AND name=N'IX_ProductShare_TenantMessage')
    CREATE INDEX IX_ProductShare_TenantMessage ON integration.WhatsAppProductShareRequests(TenantId,ProviderMessageId) INCLUDE(Status,ProductId,CustomerId);
