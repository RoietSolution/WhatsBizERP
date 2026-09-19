CREATE TABLE [marketing].[DemoRequests]
(
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [ReferenceNo] AS (CONVERT(varchar(3),'KD-') + CASE WHEN [Id] < (1000000) THEN RIGHT(CONVERT(varchar(6),'000000') + CONVERT(varchar(20),[Id]),(6)) ELSE CONVERT(varchar(20),[Id]) END) PERSISTED,
    [Name] NVARCHAR(100) NOT NULL,
    [Mobile] NVARCHAR(24) NOT NULL,
    [Email] NVARCHAR(254) NULL,
    [BusinessName] NVARCHAR(150) NULL,
    [City] NVARCHAR(100) NULL,
    [BusinessType] NVARCHAR(100) NULL,
    [Message] NVARCHAR(2000) NULL,
    [Source] NVARCHAR(100) NOT NULL CONSTRAINT [DF_DemoRequests_Source] DEFAULT N'Website',
    [UtmSource] NVARCHAR(100) NULL,
    [UtmMedium] NVARCHAR(100) NULL,
    [UtmCampaign] NVARCHAR(150) NULL,
    [UtmContent] NVARCHAR(150) NULL,
    [LandingPage] NVARCHAR(2048) NULL,
    [Referrer] NVARCHAR(2048) NULL,
    [Status] NVARCHAR(30) NOT NULL CONSTRAINT [DF_DemoRequests_Status] DEFAULT N'NEW',
    [IpAddress] NVARCHAR(64) NULL,
    [UserAgent] NVARCHAR(512) NULL,
    [NotificationStatus] NVARCHAR(20) NOT NULL CONSTRAINT [DF_DemoRequests_NotificationStatus] DEFAULT N'PENDING',
    [NotificationAttemptedOn] DATETIMEOFFSET NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_DemoRequests_CreatedOn] DEFAULT SYSUTCDATETIME(),
    [ModifiedOn] DATETIMEOFFSET NULL,
    [ModifiedBy] NVARCHAR(256) NULL,
    CONSTRAINT [PK_DemoRequests] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_DemoRequests_ReferenceNo] UNIQUE ([ReferenceNo]),
    CONSTRAINT [CK_DemoRequests_Status] CHECK ([Status]=N'LOST' OR [Status]=N'NOT_INTERESTED' OR [Status]=N'CONVERTED' OR [Status]=N'TRIAL_STARTED' OR [Status]=N'DEMO_COMPLETED' OR [Status]=N'DEMO_SCHEDULED' OR [Status]=N'FOLLOW_UP' OR [Status]=N'CONTACTED' OR [Status]=N'NEW'),
    CONSTRAINT [CK_DemoRequests_NotificationStatus] CHECK ([NotificationStatus]=N'SKIPPED' OR [NotificationStatus]=N'FAILED' OR [NotificationStatus]=N'SENT' OR [NotificationStatus]=N'PENDING')
);
GO
CREATE INDEX [IX_DemoRequests_MobileCreatedOn]
    ON [marketing].[DemoRequests] ([Mobile], [CreatedOn] DESC)
    INCLUDE ([ReferenceNo], [IpAddress]);
GO
CREATE INDEX [IX_DemoRequests_StatusCreatedOn]
    ON [marketing].[DemoRequests] ([Status], [CreatedOn] DESC)
    INCLUDE ([ReferenceNo], [Name], [Mobile], [BusinessName], [BusinessType], [City], [Source]);
GO
