CREATE VIEW [inventory].[WarehouseSummary] AS
SELECT w.WarehouseId,w.WarehouseCode,w.WarehouseName,w.WarehouseTypeId,t.TypeCode,t.TypeName,w.BranchId,w.ManagerName,w.Email,w.Phone,w.Mobile,w.Capacity,w.IsDefault,w.IsActive,
       a.AddressLine1,a.City,a.State,a.Country,a.PostalCode,
       (SELECT COUNT_BIG(*) FROM inventory.WarehouseZones z WHERE z.WarehouseId=w.WarehouseId AND z.IsActive=1) ActiveZoneCount,
       (SELECT COUNT_BIG(*) FROM inventory.WarehouseBins b WHERE b.WarehouseId=w.WarehouseId AND b.IsActive=1) ActiveBinCount
FROM inventory.Warehouses w
INNER JOIN inventory.WarehouseTypes t ON t.WarehouseTypeId=w.WarehouseTypeId
LEFT JOIN inventory.WarehouseAddresses a ON a.AddressId=w.AddressId
WHERE w.IsDeleted=0;
