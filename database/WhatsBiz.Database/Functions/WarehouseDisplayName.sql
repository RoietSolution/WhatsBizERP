CREATE FUNCTION [inventory].[WarehouseDisplayName](@WarehouseId UNIQUEIDENTIFIER)
RETURNS NVARCHAR(253)
AS
BEGIN
    DECLARE @Value NVARCHAR(253);
    SELECT @Value=CONCAT(WarehouseCode,' - ',WarehouseName) FROM inventory.Warehouses WHERE WarehouseId=@WarehouseId AND IsDeleted=0;
    RETURN @Value;
END;
