CREATE PROCEDURE [inventory].[Warehouse_Search]
    @Search NVARCHAR(200) = NULL,
    @IsActive BIT = NULL,
    @WarehouseTypeId UNIQUEIDENTIFIER = NULL,
    @SortBy NVARCHAR(30) = 'warehouseName',
    @Descending BIT = 0,
    @PageNumber INT = 1,
    @PageSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SET @PageNumber = CASE WHEN @PageNumber < 1 THEN 1 ELSE @PageNumber END;
    SET @PageSize = CASE WHEN @PageSize < 1 THEN 20 WHEN @PageSize > 200 THEN 200 ELSE @PageSize END;
    SELECT w.WarehouseId,w.WarehouseCode,w.WarehouseName,w.WarehouseTypeId,t.TypeName,w.ManagerName,w.Email,w.Mobile,w.Capacity,w.IsDefault,w.IsActive
    FROM inventory.Warehouses w INNER JOIN inventory.WarehouseTypes t ON t.WarehouseTypeId=w.WarehouseTypeId
    WHERE w.IsDeleted=0 AND (@IsActive IS NULL OR w.IsActive=@IsActive) AND (@WarehouseTypeId IS NULL OR w.WarehouseTypeId=@WarehouseTypeId)
      AND (@Search IS NULL OR w.WarehouseCode LIKE '%'+@Search+'%' OR w.WarehouseName LIKE '%'+@Search+'%' OR w.ManagerName LIKE '%'+@Search+'%')
    ORDER BY CASE WHEN @Descending=0 AND @SortBy='warehouseCode' THEN w.WarehouseCode END,
             CASE WHEN @Descending=0 AND @SortBy<>'warehouseCode' THEN w.WarehouseName END,
             CASE WHEN @Descending=1 AND @SortBy='warehouseCode' THEN w.WarehouseCode END DESC,
             CASE WHEN @Descending=1 AND @SortBy<>'warehouseCode' THEN w.WarehouseName END DESC
    OFFSET (@PageNumber-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    SELECT COUNT_BIG(*) TotalCount FROM inventory.Warehouses w
    WHERE w.IsDeleted=0 AND (@IsActive IS NULL OR w.IsActive=@IsActive) AND (@WarehouseTypeId IS NULL OR w.WarehouseTypeId=@WarehouseTypeId)
      AND (@Search IS NULL OR w.WarehouseCode LIKE '%'+@Search+'%' OR w.WarehouseName LIKE '%'+@Search+'%' OR w.ManagerName LIKE '%'+@Search+'%');
END;
