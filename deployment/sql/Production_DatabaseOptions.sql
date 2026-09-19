SET NOCOUNT ON;

IF DB_NAME() <> N'WhatsBizERP_PROD' OR DB_ID(N'WhatsBizERP_PROD') IS NULL
    THROW 51920, N'Production database options may be applied only while connected to WhatsBizERP_PROD.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID(N'WhatsBizERP_PROD')
      AND page_verify_option_desc = N'CHECKSUM'
)
    ALTER DATABASE [WhatsBizERP_PROD] SET PAGE_VERIFY CHECKSUM;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID(N'WhatsBizERP_PROD')
      AND target_recovery_time_in_seconds = 60
)
    ALTER DATABASE [WhatsBizERP_PROD] SET TARGET_RECOVERY_TIME = 60 SECONDS;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID(N'WhatsBizERP_PROD')
      AND page_verify_option_desc = N'CHECKSUM'
      AND target_recovery_time_in_seconds = 60
)
    THROW 51921, N'Production database options did not reach the required values.', 1;
