SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF SCHEMA_ID(N'commerce') IS NULL EXEC(N'CREATE SCHEMA [commerce]');
IF OBJECT_ID(N'commerce.AnalyticsEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [commerce].[AnalyticsEvents]
    (
        [AnalyticsEventId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CommerceAnalyticsEvents_Id] DEFAULT NEWSEQUENTIALID(),
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [EventType] NVARCHAR(60) NOT NULL,
        [CustomerId] UNIQUEIDENTIFIER NULL,
        [ConversationId] UNIQUEIDENTIFIER NULL,
        [ProductId] UNIQUEIDENTIFIER NULL,
        [VariantId] UNIQUEIDENTIFIER NULL,
        [CollectionId] UNIQUEIDENTIFIER NULL,
        [MetadataJson] NVARCHAR(4000) NULL,
        [CreatedOn] DATETIMEOFFSET(7) NOT NULL CONSTRAINT [DF_CommerceAnalyticsEvents_CreatedOn] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_CommerceAnalyticsEvents] PRIMARY KEY ([AnalyticsEventId]),
        CONSTRAINT [FK_CommerceAnalyticsEvents_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants]([TenantId]),
        CONSTRAINT [CK_CommerceAnalyticsEvents_Type] CHECK ([EventType] <> N'')
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_CommerceAnalyticsEvents_TenantCreated' AND object_id=OBJECT_ID(N'commerce.AnalyticsEvents'))
    CREATE INDEX [IX_CommerceAnalyticsEvents_TenantCreated] ON [commerce].[AnalyticsEvents]([TenantId],[CreatedOn] DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_CommerceAnalyticsEvents_TenantTypeCreated' AND object_id=OBJECT_ID(N'commerce.AnalyticsEvents'))
    CREATE INDEX [IX_CommerceAnalyticsEvents_TenantTypeCreated] ON [commerce].[AnalyticsEvents]([TenantId],[EventType],[CreatedOn] DESC);
COMMIT TRANSACTION;
