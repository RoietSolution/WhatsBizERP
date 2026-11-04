/*
   One-time administrator procedure for the centrally approved disposable DB.
   Review and execute manually as an authorized SQL Server administrator.
   This script only creates the exact database when absent; it grants no rights,
   changes no authentication settings, and never drops or resets a database.
*/
USE [master];
IF CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) <> N'DESKTOP-DQ0868S'
    THROW 59100, N'Unexpected SQL Server; provisioning stopped.', 1;
IF DB_NAME() <> N'master'
    THROW 59101, N'Provisioning must execute in master.', 1;
IF DB_ID(N'WhatsBizERP_IntegrationTest_V82') IS NOT NULL
    THROW 59102, N'The approved database already exists; this script will not modify it.', 1;
EXEC(N'CREATE DATABASE [WhatsBizERP_IntegrationTest_V82]');
IF DB_ID(N'WhatsBizERP_IntegrationTest_V82') IS NULL
    THROW 59103, N'Database creation was not confirmed.', 1;
SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS VerifiedServer,
       name AS CreatedDatabase,
       state_desc AS DatabaseState
FROM sys.databases
WHERE name = N'WhatsBizERP_IntegrationTest_V82';
