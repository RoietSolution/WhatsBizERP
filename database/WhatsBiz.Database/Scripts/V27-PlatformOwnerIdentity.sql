SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* The DACPAC publishes the final nullable TenantId, non-null AccountType,
   default, check constraint, and role-scope trigger before post-deployment. */
IF COL_LENGTH(N'core.Users', N'AccountType') IS NULL
   OR EXISTS
   (
       SELECT 1
       FROM sys.columns
       WHERE object_id=OBJECT_ID(N'core.Users') AND name=N'TenantId' AND is_nullable=0
   )
    THROW 52700, 'The modeled ApplicationOwner identity schema must be published before V27 data migration.', 1;

IF EXISTS
(
    SELECT 1
    FROM core.Users u
    JOIN core.UserRoles ur ON ur.UserId=u.Id
    JOIN core.Roles r ON r.Id=ur.RoleId AND r.NormalizedName=N'APPLICATIONOWNER'
    JOIN core.UserRoles otherUr ON otherUr.UserId=u.Id AND otherUr.RoleId<>ur.RoleId
)
    THROW 52701, 'ApplicationOwner accounts must not also have retailer roles. Remove the additional role before applying V27.', 1;

UPDATE u
SET AccountType=N'APPLICATION_OWNER', TenantId=NULL, ModifiedOn=SYSUTCDATETIME(), ModifiedBy=N'V27 platform-owner migration'
FROM core.Users u
JOIN core.UserRoles ur ON ur.UserId=u.Id
JOIN core.Roles r ON r.Id=ur.RoleId AND r.NormalizedName=N'APPLICATIONOWNER'
WHERE u.AccountType<>N'APPLICATION_OWNER' OR u.TenantId IS NOT NULL;

IF EXISTS (SELECT 1 FROM core.Users WHERE AccountType IS NULL OR (AccountType=N'APPLICATION_OWNER' AND TenantId IS NOT NULL) OR (AccountType=N'RETAILER' AND TenantId IS NULL) OR AccountType NOT IN(N'APPLICATION_OWNER',N'RETAILER'))
    THROW 52702, 'Existing user account scope is invalid; V27 cannot safely continue.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'core.Users')
      AND name=N'CK_Users_AccountScope'
      AND is_disabled=0
      AND is_not_trusted=0
)
    THROW 52705, 'CK_Users_AccountScope must remain enabled and trusted.', 1;

COMMIT TRANSACTION;
GO

IF OBJECT_ID(N'admin.AuditLogs', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'admin.AuditLogs', N'UserId') IS NULL ALTER TABLE admin.AuditLogs ADD UserId uniqueidentifier NULL;
    IF COL_LENGTH(N'admin.AuditLogs', N'TargetTenantId') IS NULL ALTER TABLE admin.AuditLogs ADD TargetTenantId uniqueidentifier NULL;
END;
GO
