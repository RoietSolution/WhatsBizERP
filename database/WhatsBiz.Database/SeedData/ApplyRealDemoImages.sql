USE [WhatsBizERP];
SET NOCOUNT ON;

DECLARE @Assets TABLE (CategoryName nvarchar(200) NOT NULL, FilePath nvarchar(4000) NOT NULL);
INSERT INTO @Assets(CategoryName, FilePath) VALUES
 (N'Grocery & Staples', N'G:\Saas1\WhatsBizERP\database\WhatsBiz.Database\SeedData\Assets\grocery.png'),
 (N'Beverages', N'G:\Saas1\WhatsBizERP\database\WhatsBiz.Database\SeedData\Assets\beverages.png'),
 (N'Sarees & Ethnic Wear', N'G:\Saas1\WhatsBizERP\database\WhatsBiz.Database\SeedData\Assets\sarees.png'),
 (N'Personal Care', N'G:\Saas1\WhatsBizERP\database\WhatsBiz.Database\SeedData\Assets\personal-care.png'),
 (N'Home & Kitchen', N'G:\Saas1\WhatsBizERP\database\WhatsBiz.Database\SeedData\Assets\home-kitchen.png'),
 (N'Books & Stationery', N'G:\Saas1\WhatsBizERP\database\WhatsBiz.Database\SeedData\Assets\books-stationery.png');

DECLARE @CategoryName nvarchar(200), @FilePath nvarchar(4000), @sql nvarchar(max);
DECLARE assets CURSOR LOCAL FAST_FORWARD FOR SELECT CategoryName, FilePath FROM @Assets;
OPEN assets;
FETCH NEXT FROM assets INTO @CategoryName, @FilePath;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'UPDATE img SET ImageData = f.BulkColumn, ContentType = ''image/png'', FileName = REPLACE(RIGHT(''' + REPLACE(@FilePath,'''','''''') + N''', CHARINDEX(''\'', REVERSE(''' + REPLACE(@FilePath,'''','''''') + N''')) - 1), ''/'', ''_'')
FROM master.ProductImages img
JOIN master.Products p ON p.ProductId = img.ProductId
JOIN master.ProductCategories c ON c.ProductCategoryId = p.CategoryId
CROSS APPLY (SELECT BulkColumn FROM OPENROWSET(BULK ''' + REPLACE(@FilePath,'''','''''') + N''', SINGLE_BLOB) f) f
WHERE c.CategoryName = N''' + REPLACE(@CategoryName,'''','''''') + N''';';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM assets INTO @CategoryName, @FilePath;
END
CLOSE assets;
DEALLOCATE assets;
GO
