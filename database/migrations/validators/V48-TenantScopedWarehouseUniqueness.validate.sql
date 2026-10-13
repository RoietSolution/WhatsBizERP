SET NOCOUNT ON;
IF DB_NAME() NOT IN(N'WhatsBizERP_QA',N'WhatsBizERP_PROD') THROW 52490,N'Unexpected database for V48 validation.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'inventory.Warehouses') AND name=N'UX_Warehouses_Code' AND is_unique=1) THROW 52491,N'V48 code index is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'inventory.Warehouses') AND name=N'UX_Warehouses_Name' AND is_unique=1) THROW 52492,N'V48 name index is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'inventory.Warehouses') AND name=N'UX_Warehouses_Default' AND is_unique=1) THROW 52493,N'V48 default index is missing.',1;
IF EXISTS(SELECT 1 FROM inventory.Warehouses WHERE TenantId IS NOT NULL AND IsDeleted=0 GROUP BY TenantId,WarehouseCode HAVING COUNT(1)>1) THROW 52494,N'V48 duplicate tenant warehouse codes remain.',1;
SELECT N'V48_VALID' AS ValidationResult;
