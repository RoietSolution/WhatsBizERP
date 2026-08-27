SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

IF SCHEMA_ID(N'marketing') IS NULL EXEC(N'CREATE SCHEMA marketing');

IF OBJECT_ID(N'marketing.DemoRequests',N'U') IS NULL
BEGIN
    CREATE TABLE marketing.DemoRequests
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DemoRequests PRIMARY KEY CLUSTERED,
        ReferenceNo AS (CONVERT(varchar(3),'KD-') + CASE WHEN Id < 1000000 THEN RIGHT(CONVERT(varchar(6),'000000') + CONVERT(varchar(20),Id),6) ELSE CONVERT(varchar(20),Id) END) PERSISTED,
        Name NVARCHAR(100) NOT NULL,
        Mobile NVARCHAR(24) NOT NULL,
        Email NVARCHAR(254) NULL,
        BusinessName NVARCHAR(150) NULL,
        City NVARCHAR(100) NULL,
        BusinessType NVARCHAR(100) NULL,
        Message NVARCHAR(2000) NULL,
        Source NVARCHAR(100) NOT NULL CONSTRAINT DF_DemoRequests_Source DEFAULT N'Website',
        UtmSource NVARCHAR(100) NULL,
        UtmMedium NVARCHAR(100) NULL,
        UtmCampaign NVARCHAR(150) NULL,
        UtmContent NVARCHAR(150) NULL,
        LandingPage NVARCHAR(2048) NULL,
        Referrer NVARCHAR(2048) NULL,
        Status NVARCHAR(30) NOT NULL CONSTRAINT DF_DemoRequests_Status DEFAULT N'NEW',
        IpAddress NVARCHAR(64) NULL,
        UserAgent NVARCHAR(512) NULL,
        NotificationStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_DemoRequests_NotificationStatus DEFAULT N'PENDING',
        NotificationAttemptedOn DATETIMEOFFSET NULL,
        CreatedOn DATETIMEOFFSET NOT NULL CONSTRAINT DF_DemoRequests_CreatedOn DEFAULT SYSUTCDATETIME(),
        ModifiedOn DATETIMEOFFSET NULL,
        ModifiedBy NVARCHAR(256) NULL,
        CONSTRAINT UQ_DemoRequests_ReferenceNo UNIQUE(ReferenceNo),
        CONSTRAINT CK_DemoRequests_Status CHECK(Status IN(N'NEW',N'CONTACTED',N'FOLLOW_UP',N'DEMO_SCHEDULED',N'DEMO_COMPLETED',N'TRIAL_STARTED',N'CONVERTED',N'NOT_INTERESTED',N'LOST')),
        CONSTRAINT CK_DemoRequests_NotificationStatus CHECK(NotificationStatus IN(N'PENDING',N'SENT',N'FAILED',N'SKIPPED'))
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'marketing.DemoRequests') AND name=N'IX_DemoRequests_MobileCreatedOn')
    CREATE INDEX IX_DemoRequests_MobileCreatedOn ON marketing.DemoRequests(Mobile,CreatedOn DESC) INCLUDE(ReferenceNo,IpAddress);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'marketing.DemoRequests') AND name=N'IX_DemoRequests_StatusCreatedOn')
    CREATE INDEX IX_DemoRequests_StatusCreatedOn ON marketing.DemoRequests(Status,CreatedOn DESC) INCLUDE(ReferenceNo,Name,Mobile,BusinessName,BusinessType,City,Source);

COMMIT TRANSACTION;
