/*
  One-time administrator bootstrap for the managed database deployment login.
  Supply WHATSBIZ_DEPLOYMENT_DATABASE and WHATSBIZ_DATABASE_DEPLOYMENT_PASSWORD
  through the approved secure sqlcmd/environment mechanism. Never commit either.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF N'$(WHATSBIZ_DEPLOYMENT_DATABASE)' NOT IN (N'WhatsBizERP_QA',N'WhatsBizERP_PROD')
    THROW 52500,N'WHATSBIZ_DEPLOYMENT_DATABASE must be WhatsBizERP_QA or WhatsBizERP_PROD.',1;

IF SUSER_ID(N'WhatsBizDatabaseDeployment') IS NULL
BEGIN
    CREATE LOGIN [WhatsBizDatabaseDeployment] WITH PASSWORD=N'$(WHATSBIZ_DATABASE_DEPLOYMENT_PASSWORD)',CHECK_POLICY=ON,CHECK_EXPIRATION=ON;
END
ELSE
BEGIN
    ALTER LOGIN [WhatsBizDatabaseDeployment] WITH PASSWORD=N'$(WHATSBIZ_DATABASE_DEPLOYMENT_PASSWORD)',CHECK_POLICY=ON,CHECK_EXPIRATION=ON;
    ALTER LOGIN [WhatsBizDatabaseDeployment] ENABLE;
END

GRANT CREATE ANY DATABASE TO [WhatsBizDatabaseDeployment];

USE $(WHATSBIZ_DEPLOYMENT_DATABASE_IDENTIFIER);
IF USER_ID(N'WhatsBizDatabaseDeployment') IS NULL
    CREATE USER [WhatsBizDatabaseDeployment] FOR LOGIN [WhatsBizDatabaseDeployment];
ELSE
    ALTER USER [WhatsBizDatabaseDeployment] WITH LOGIN=[WhatsBizDatabaseDeployment];

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members rm
    JOIN sys.database_principals role_principal ON role_principal.principal_id=rm.role_principal_id
    JOIN sys.database_principals member_principal ON member_principal.principal_id=rm.member_principal_id
    WHERE role_principal.name=N'db_owner' AND member_principal.name=N'WhatsBizDatabaseDeployment'
)
    ALTER ROLE [db_owner] ADD MEMBER [WhatsBizDatabaseDeployment];

SELECT N'DATABASE_DEPLOYMENT_PRINCIPAL_CONFIGURED' AS Result,
       DB_NAME() AS DatabaseName,
       SUSER_SNAME() AS ConfiguredBy;
