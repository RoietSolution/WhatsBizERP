CREATE TRIGGER [core].[TR_Users_ResourceCapacity]
ON [core].[Users]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS
    (
        SELECT 1
        FROM inserted i
        LEFT JOIN deleted d ON d.Id=i.Id
        WHERE i.TenantId IS NOT NULL
          AND i.AccountType=N'RETAILER'
          AND i.IsActive=1
          AND i.IsDeleted=0
          AND (d.Id IS NULL OR d.IsActive=0 OR d.IsDeleted=1 OR d.TenantId<>i.TenantId)
    ) RETURN;

    DECLARE @limits TABLE(TenantId uniqueidentifier PRIMARY KEY,LimitValue int NOT NULL);

    INSERT @limits(TenantId,LimitValue)
    SELECT l.TenantId,l.LimitValue
    FROM core.TenantResourceLimits l WITH(UPDLOCK,HOLDLOCK)
    JOIN
    (
        SELECT DISTINCT i.TenantId
        FROM inserted i
        LEFT JOIN deleted d ON d.Id=i.Id
        WHERE i.TenantId IS NOT NULL
          AND i.AccountType=N'RETAILER'
          AND i.IsActive=1
          AND i.IsDeleted=0
          AND (d.Id IS NULL OR d.IsActive=0 OR d.IsDeleted=1 OR d.TenantId<>i.TenantId)
    ) i ON i.TenantId=l.TenantId
    WHERE l.ResourceType=N'USERS' AND l.LimitValue IS NOT NULL;

    IF EXISTS
    (
        SELECT 1
        FROM @limits l
        CROSS APPLY
        (
            SELECT COUNT_BIG(*) Used
            FROM core.Users u
            WHERE u.TenantId=l.TenantId
              AND u.AccountType=N'RETAILER'
              AND u.IsActive=1
              AND u.IsDeleted=0
        ) c
        WHERE c.Used>l.LimitValue
    )
        THROW 51851,N'The tenant user limit has been reached.',1;
END;
