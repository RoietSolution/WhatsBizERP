CREATE VIEW [inventory].[InventoryStockSummary] AS
SELECT b.ProductId,p.ProductCode,p.ProductName,b.WarehouseId,w.WarehouseCode,w.WarehouseName,SUM(b.QuantityOnHand) QuantityOnHand,SUM(b.QuantityReserved) QuantityReserved,SUM(b.QuantityAvailable) QuantityAvailable,SUM(b.QuantityOnHand*b.AverageCost) TotalStockValue,p.ReorderLevel
FROM inventory.InventoryBalances b INNER JOIN master.Products p ON p.ProductId=b.ProductId INNER JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId
GROUP BY b.ProductId,p.ProductCode,p.ProductName,b.WarehouseId,w.WarehouseCode,w.WarehouseName,p.ReorderLevel;
