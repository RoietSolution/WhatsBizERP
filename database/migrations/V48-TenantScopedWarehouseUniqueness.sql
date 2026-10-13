/* V48 - Make warehouse code, name, and default uniqueness tenant-scoped. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (
    SELECT 1 FROM inventory.Warehouses
    WHERE TenantId IS NOT NULL AND IsDeleted=0
    GROUP BY TenantId,WarehouseCode HAVING COUNT(1)>1
) THROW 52480,N'V48 cannot proceed: duplicate warehouse codes exist within a tenant.',1;
IF EXISTS (
    SELECT 1 FROM inventory.Warehouses
    WHERE TenantId IS NOT NULL AND IsDeleted=0
    GROUP BY TenantId,WarehouseName HAVING COUNT(1)>1
) THROW 52481,N'V48 cannot proceed: duplicate warehouse names exist within a tenant.',1;
IF EXISTS (
    SELECT 1 FROM inventory.Warehouses
    WHERE TenantId IS NOT NULL AND IsDefault=1 AND IsDeleted=0
    GROUP BY TenantId HAVING COUNT(1)>1
) THROW 52482,N'V48 cannot proceed: multiple default warehouses exist within a tenant.',1;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'inventory.Warehouses') AND name=N'UX_Warehouses_Code') DROP INDEX UX_Warehouses_Code ON inventory.Warehouses;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'inventory.Warehouses') AND name=N'UX_Warehouses_Name') DROP INDEX UX_Warehouses_Name ON inventory.Warehouses;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'inventory.Warehouses') AND name=N'UX_Warehouses_Default') DROP INDEX UX_Warehouses_Default ON inventory.Warehouses;

CREATE UNIQUE INDEX UX_Warehouses_Code ON inventory.Warehouses(TenantId,WarehouseCode) WHERE TenantId IS NOT NULL AND IsDeleted=0;
CREATE UNIQUE INDEX UX_Warehouses_Name ON inventory.Warehouses(TenantId,WarehouseName) WHERE TenantId IS NOT NULL AND IsDeleted=0;
CREATE UNIQUE INDEX UX_Warehouses_Default ON inventory.Warehouses(TenantId,IsDefault) WHERE TenantId IS NOT NULL AND IsDefault=1 AND IsDeleted=0;
