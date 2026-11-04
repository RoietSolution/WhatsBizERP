/* V67: tenant-scoped scheduled storefront product availability. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.StorefrontAvailabilitySchedules', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontAvailabilitySchedules(
        ScheduleId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontAvailabilitySchedules PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        ScheduleName nvarchar(150) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_Enabled DEFAULT(1),
        -- Legacy fields are retained as a compatibility bridge for an early V67 draft.
        StartTime time(0) NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_Start DEFAULT('15:00'),
        EndTime time(0) NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_End DEFAULT('17:00'),
        OrderStartTime time(0) NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_OrderStart DEFAULT('15:00'),
        OrderEndTime time(0) NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_OrderEnd DEFAULT('17:00'),
        DeliveryStartTime time(0) NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_DeliveryStart DEFAULT('15:00'),
        DeliveryEndTime time(0) NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_DeliveryEnd DEFAULT('17:00'),
        WeekdayMask tinyint NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_WeekdayMask DEFAULT(127),
        TimeZoneId nvarchar(100) NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_TimeZone DEFAULT(N'Asia/Kolkata'),
        CustomerTitle nvarchar(150) NULL,
        CustomerMessage nvarchar(500) NULL,
        ShowAnnouncement bit NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_Announcement DEFAULT(0),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontAvailabilitySchedules_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_StorefrontAvailabilitySchedules_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_StorefrontAvailabilitySchedules_WeekdayMask CHECK(WeekdayMask BETWEEN 1 AND 127),
        CONSTRAINT CK_StorefrontAvailabilitySchedules_Name CHECK(LEN(LTRIM(RTRIM(ScheduleName))) > 0),
        CONSTRAINT CK_StorefrontAvailabilitySchedules_TimeZone CHECK(LEN(LTRIM(RTRIM(TimeZoneId))) > 0)
    );
    CREATE INDEX IX_StorefrontAvailabilitySchedules_TenantEnabled ON commerce.StorefrontAvailabilitySchedules(TenantId,IsEnabled,StartTime,EndTime);
END;

-- Upgrade an early or partially-created V67 table without rewriting schedule data.
IF OBJECT_ID(N'commerce.StorefrontAvailabilitySchedules', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'commerce.StorefrontAvailabilitySchedules', N'OrderStartTime') IS NULL
        ALTER TABLE commerce.StorefrontAvailabilitySchedules ADD OrderStartTime time(0) NULL;
    IF COL_LENGTH(N'commerce.StorefrontAvailabilitySchedules', N'OrderEndTime') IS NULL
        ALTER TABLE commerce.StorefrontAvailabilitySchedules ADD OrderEndTime time(0) NULL;
    IF COL_LENGTH(N'commerce.StorefrontAvailabilitySchedules', N'DeliveryStartTime') IS NULL
        ALTER TABLE commerce.StorefrontAvailabilitySchedules ADD DeliveryStartTime time(0) NULL;
    IF COL_LENGTH(N'commerce.StorefrontAvailabilitySchedules', N'DeliveryEndTime') IS NULL
        ALTER TABLE commerce.StorefrontAvailabilitySchedules ADD DeliveryEndTime time(0) NULL;

    UPDATE commerce.StorefrontAvailabilitySchedules
    SET OrderStartTime=COALESCE(OrderStartTime,StartTime), OrderEndTime=COALESCE(OrderEndTime,EndTime),
        DeliveryStartTime=COALESCE(DeliveryStartTime,StartTime), DeliveryEndTime=COALESCE(DeliveryEndTime,EndTime)
    WHERE OrderStartTime IS NULL OR OrderEndTime IS NULL OR DeliveryStartTime IS NULL OR DeliveryEndTime IS NULL;

    ALTER TABLE commerce.StorefrontAvailabilitySchedules ALTER COLUMN OrderStartTime time(0) NOT NULL;
    ALTER TABLE commerce.StorefrontAvailabilitySchedules ALTER COLUMN OrderEndTime time(0) NOT NULL;
    ALTER TABLE commerce.StorefrontAvailabilitySchedules ALTER COLUMN DeliveryStartTime time(0) NOT NULL;
    ALTER TABLE commerce.StorefrontAvailabilitySchedules ALTER COLUMN DeliveryEndTime time(0) NOT NULL;
END;

IF OBJECT_ID(N'commerce.StorefrontAvailabilityScheduleProducts', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontAvailabilityScheduleProducts(
        ScheduleId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        CONSTRAINT PK_StorefrontAvailabilityScheduleProducts PRIMARY KEY(ScheduleId,ProductId),
        CONSTRAINT FK_StorefrontAvailabilityScheduleProducts_Schedule FOREIGN KEY(ScheduleId) REFERENCES commerce.StorefrontAvailabilitySchedules(ScheduleId),
        CONSTRAINT FK_StorefrontAvailabilityScheduleProducts_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontAvailabilityScheduleProducts_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId)
    );
    CREATE INDEX IX_StorefrontAvailabilityScheduleProducts_TenantProduct ON commerce.StorefrontAvailabilityScheduleProducts(TenantId,ProductId);
END;

IF OBJECT_ID(N'commerce.StorefrontAvailabilityScheduleCategories', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontAvailabilityScheduleCategories(
        ScheduleId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        ProductCategoryId uniqueidentifier NOT NULL,
        CONSTRAINT PK_StorefrontAvailabilityScheduleCategories PRIMARY KEY(ScheduleId,ProductCategoryId),
        CONSTRAINT FK_StorefrontAvailabilityScheduleCategories_Schedule FOREIGN KEY(ScheduleId) REFERENCES commerce.StorefrontAvailabilitySchedules(ScheduleId),
        CONSTRAINT FK_StorefrontAvailabilityScheduleCategories_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontAvailabilityScheduleCategories_Category FOREIGN KEY(ProductCategoryId) REFERENCES master.ProductCategories(ProductCategoryId)
    );
    CREATE INDEX IX_StorefrontAvailabilityScheduleCategories_TenantCategory ON commerce.StorefrontAvailabilityScheduleCategories(TenantId,ProductCategoryId);
END;
