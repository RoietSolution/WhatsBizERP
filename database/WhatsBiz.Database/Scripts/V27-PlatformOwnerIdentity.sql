SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'core.Users', N'AccountType') IS NULL
    ALTER TABLE core.Users ADD AccountType nvarchar(30) NULL;

IF EXISTS
(
    SELECT 1
    FROM core.Users u
    JOIN core.UserRoles ur ON ur.UserId=u.Id
    JOIN core.Roles r ON r.Id=ur.RoleId AND r.NormalizedName=N'APPLICATIONOWNER'
    JOIN core.UserRoles otherUr ON otherUr.UserId=u.Id AND otherUr.RoleId<>ur.RoleId
)
    THROW 52701, 'ApplicationOwner accounts must not also have retailer roles. Remove the additional role before applying V27.', 1;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'core.Users') AND name=N'TenantId' AND is_nullable=0)
    ALTER TABLE core.Users ALTER COLUMN TenantId uniqueidentifier NULL;

UPDATE u
SET AccountType=N'APPLICATION_OWNER', TenantId=NULL, ModifiedOn=SYSUTCDATETIME(), ModifiedBy=N'V27 platform-owner migration'
FROM core.Users u
JOIN core.UserRoles ur ON ur.UserId=u.Id
JOIN core.Roles r ON r.Id=ur.RoleId AND r.NormalizedName=N'APPLICATIONOWNER'
WHERE u.AccountType<>N'APPLICATION_OWNER' OR u.TenantId IS NOT NULL;

UPDATE core.Users SET AccountType=N'RETAILER' WHERE AccountType IS NULL;

IF EXISTS (SELECT 1 FROM core.Users WHERE (AccountType=N'APPLICATION_OWNER' AND TenantId IS NOT NULL) OR (AccountType=N'RETAILER' AND TenantId IS NULL) OR AccountType NOT IN(N'APPLICATION_OWNER',N'RETAILER'))
    THROW 52702, 'Existing user account scope is invalid; V27 cannot safely continue.', 1;

ALTER TABLE core.Users ALTER COLUMN AccountType nvarchar(30) NOT NULL;

IF OBJECT_ID(N'core.DF_Users_AccountType', N'D') IS NULL
    ALTER TABLE core.Users ADD CONSTRAINT DF_Users_AccountType DEFAULT N'RETAILER' FOR AccountType;
IF OBJECT_ID(N'core.CK_Users_AccountScope', N'C') IS NULL
    ALTER TABLE core.Users WITH CHECK ADD CONSTRAINT CK_Users_AccountScope CHECK ((AccountType=N'APPLICATION_OWNER' AND TenantId IS NULL) OR (AccountType=N'RETAILER' AND TenantId IS NOT NULL));

COMMIT TRANSACTION;
GO

CREATE OR ALTER TRIGGER core.TR_UserRoles_AccountScope
ON core.UserRoles
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1 FROM inserted i
        JOIN core.Users u ON u.Id=i.UserId
        JOIN core.Roles r ON r.Id=i.RoleId
        WHERE (r.NormalizedName=N'APPLICATIONOWNER' AND u.AccountType<>N'APPLICATION_OWNER')
           OR (r.NormalizedName<>N'APPLICATIONOWNER' AND u.AccountType=N'APPLICATION_OWNER')
    )
        THROW 52703, 'ApplicationOwner role and account scope cannot be mixed with retailer identities.', 1;

    IF EXISTS
    (
        SELECT 1 FROM deleted d
        JOIN core.Users u ON u.Id=d.UserId AND u.AccountType=N'APPLICATION_OWNER'
        WHERE NOT EXISTS
        (
            SELECT 1 FROM core.UserRoles ur
            JOIN core.Roles r ON r.Id=ur.RoleId AND r.NormalizedName=N'APPLICATIONOWNER'
            WHERE ur.UserId=u.Id
        )
    )
        THROW 52704, 'The ApplicationOwner role cannot be removed from a platform-owner account.', 1;
END;
GO

IF OBJECT_ID(N'admin.AuditLogs', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'admin.AuditLogs', N'UserId') IS NULL ALTER TABLE admin.AuditLogs ADD UserId uniqueidentifier NULL;
    IF COL_LENGTH(N'admin.AuditLogs', N'TargetTenantId') IS NULL ALTER TABLE admin.AuditLogs ADD TargetTenantId uniqueidentifier NULL;
END;
GO
