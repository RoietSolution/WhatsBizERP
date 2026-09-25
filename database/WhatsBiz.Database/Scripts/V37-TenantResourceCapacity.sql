SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'core.TenantResourceLimits',N'U') IS NULL
BEGIN
    CREATE TABLE core.TenantResourceLimits
    (
        TenantId uniqueidentifier NOT NULL,
        ResourceType nvarchar(30) NOT NULL,
        LimitValue int NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_TenantResourceLimits_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(256) NULL,
        UpdatedAt datetimeoffset NULL,
        UpdatedBy nvarchar(256) NULL,
        CONSTRAINT PK_TenantResourceLimits PRIMARY KEY(TenantId,ResourceType),
        CONSTRAINT FK_TenantResourceLimits_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_TenantResourceLimits_ResourceType CHECK(ResourceType IN(N'USERS',N'BRANCHES')),
        CONSTRAINT CK_TenantResourceLimits_LimitValue CHECK(LimitValue IS NULL OR LimitValue>=1)
    );
END;
GO

CREATE OR ALTER TRIGGER core.TR_TenantResourceLimits_Ownership
ON core.TenantResourceLimits AFTER INSERT,UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @tenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
    DECLARE @platform bit=COALESCE(TRY_CONVERT(bit,SESSION_CONTEXT(N'PlatformOperation')),0);
    IF @platform<>1 AND (@tenant IS NULL OR EXISTS(SELECT 1 FROM(SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted)x WHERE x.TenantId<>@tenant))
        THROW 51850,N'Tenant resource-limit context is missing or mismatched.',1;
END;
GO

CREATE OR ALTER TRIGGER admin.TR_Branches_ResourceCapacity
ON admin.Branches AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.BranchId=i.BranchId WHERE i.IsActive=1 AND (d.BranchId IS NULL OR d.IsActive=0 OR d.CompanyId<>i.CompanyId)) RETURN;
    DECLARE @limits TABLE(TenantId uniqueidentifier PRIMARY KEY,LimitValue int NOT NULL);
    INSERT @limits(TenantId,LimitValue)
    SELECT l.TenantId,l.LimitValue
    FROM core.TenantResourceLimits l WITH(UPDLOCK,HOLDLOCK)
    JOIN(SELECT DISTINCT c.TenantId FROM inserted i LEFT JOIN deleted d ON d.BranchId=i.BranchId JOIN admin.Companies c ON c.CompanyId=i.CompanyId WHERE i.IsActive=1 AND (d.BranchId IS NULL OR d.IsActive=0 OR d.CompanyId<>i.CompanyId))x ON x.TenantId=l.TenantId
    WHERE l.ResourceType=N'BRANCHES' AND l.LimitValue IS NOT NULL;
    IF EXISTS(SELECT 1 FROM @limits l CROSS APPLY(SELECT COUNT_BIG(*) Used FROM admin.Branches b JOIN admin.Companies c ON c.CompanyId=b.CompanyId WHERE c.TenantId=l.TenantId AND b.IsActive=1)n WHERE n.Used>l.LimitValue)
        THROW 51852,N'The tenant branch limit has been reached.',1;
END;
GO
