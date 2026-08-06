CREATE PROCEDURE [master].[Product_Search]
    @Search NVARCHAR(250) = NULL,
    @IsActive BIT = NULL,
    @SortBy NVARCHAR(50) = N'ProductName',
    @Descending BIT = 0,
    @PageNumber INT = 1,
    @PageSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    IF @PageNumber < 1 THROW 50001, 'PageNumber must be greater than zero.', 1;
    IF @PageSize < 1 OR @PageSize > 200 THROW 50002, 'PageSize must be between 1 and 200.', 1;

    SELECT p.[ProductId], p.[ProductCode], p.[Barcode], p.[ProductName],
           c.[CategoryName], b.[BrandName], u.[UnitName], p.[PurchasePrice],
           p.[SellingPrice], p.[GSTPercentage], p.[IsActive], p.[ImageUrl]
    FROM [master].[Products] p
    INNER JOIN [master].[ProductCategories] c ON c.[ProductCategoryId] = p.[CategoryId]
    INNER JOIN [master].[Brands] b ON b.[BrandId] = p.[BrandId]
    INNER JOIN [master].[UnitsOfMeasure] u ON u.[UnitId] = p.[UnitId]
    WHERE p.[IsDeleted] = 0
      AND (@IsActive IS NULL OR p.[IsActive] = @IsActive)
      AND (@Search IS NULL OR p.[ProductCode] LIKE N'%' + @Search + N'%' OR p.[ProductName] LIKE N'%' + @Search + N'%' OR p.[Barcode] LIKE N'%' + @Search + N'%')
    ORDER BY
      CASE WHEN @Descending = 0 AND @SortBy = N'ProductCode' THEN p.[ProductCode] END,
      CASE WHEN @Descending = 1 AND @SortBy = N'ProductCode' THEN p.[ProductCode] END DESC,
      CASE WHEN @Descending = 0 AND @SortBy = N'SellingPrice' THEN p.[SellingPrice] END,
      CASE WHEN @Descending = 1 AND @SortBy = N'SellingPrice' THEN p.[SellingPrice] END DESC,
      CASE WHEN @Descending = 0 AND @SortBy = N'CategoryName' THEN c.[CategoryName] END,
      CASE WHEN @Descending = 1 AND @SortBy = N'CategoryName' THEN c.[CategoryName] END DESC,
      CASE WHEN @Descending = 0 AND @SortBy NOT IN (N'ProductCode', N'SellingPrice', N'CategoryName') THEN p.[ProductName] END,
      CASE WHEN @Descending = 1 AND @SortBy NOT IN (N'ProductCode', N'SellingPrice', N'CategoryName') THEN p.[ProductName] END DESC,
      p.[ProductId]
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT COUNT_BIG(1) AS [TotalCount]
    FROM [master].[Products] p
    WHERE p.[IsDeleted] = 0
      AND (@IsActive IS NULL OR p.[IsActive] = @IsActive)
      AND (@Search IS NULL OR p.[ProductCode] LIKE N'%' + @Search + N'%' OR p.[ProductName] LIKE N'%' + @Search + N'%' OR p.[Barcode] LIKE N'%' + @Search + N'%');
END;
