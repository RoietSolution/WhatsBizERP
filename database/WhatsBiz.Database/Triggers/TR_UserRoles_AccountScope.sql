CREATE TRIGGER [core].[TR_UserRoles_AccountScope]
ON [core].[UserRoles]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN core.Users u ON u.Id=i.UserId
        JOIN core.Roles r ON r.Id=i.RoleId
        WHERE (r.NormalizedName=N'APPLICATIONOWNER' AND u.AccountType<>N'APPLICATION_OWNER')
           OR (r.NormalizedName<>N'APPLICATIONOWNER' AND u.AccountType=N'APPLICATION_OWNER')
    )
        THROW 52703, 'ApplicationOwner role and account scope cannot be mixed with retailer identities.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM deleted d
        JOIN core.Users u ON u.Id=d.UserId AND u.AccountType=N'APPLICATION_OWNER'
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM core.UserRoles ur
            JOIN core.Roles r ON r.Id=ur.RoleId AND r.NormalizedName=N'APPLICATIONOWNER'
            WHERE ur.UserId=u.Id
        )
    )
        THROW 52704, 'The ApplicationOwner role cannot be removed from a platform-owner account.', 1;
END;
