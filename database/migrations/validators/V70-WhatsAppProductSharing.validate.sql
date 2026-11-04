SET NOCOUNT ON;
IF OBJECT_ID(N'integration.WhatsAppProductShareRequests',N'U') IS NULL
    THROW 70010,N'Missing product sharing send ledger.',1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'integration.WhatsAppProductShareRequests') AND name IN(N'TenantId',N'RequestId',N'ProductId',N'CustomerId',N'ConsentConfirmedBy',N'ConsentConfirmedAtUtc',N'Status',N'ProviderMode',N'ProviderMessageId',N'UpdatedAtUtc'))<>10
    THROW 70011,N'Incomplete product sharing send ledger.',1;
IF NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'integration.WhatsAppProductShareRequests') AND name=N'PK_WhatsAppProductShareRequests')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'integration.WhatsAppProductShareRequests') AND name=N'FK_ProductShare_Tenant' AND is_disabled=0 AND is_not_trusted=0)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppProductShareRequests') AND name=N'IX_ProductShare_TenantMessage' AND is_disabled=0)
    THROW 70012,N'Missing product sharing tenant/key/index guards.',1;
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'integration.WhatsAppProductShareRequests') AND name IN(N'CK_ProductShare_Status',N'CK_ProductShare_Mode') AND is_disabled=0 AND is_not_trusted=0)<>2
    THROW 70013,N'Missing product sharing validation constraints.',1;
