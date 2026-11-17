using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class SupplierCatalogV83SqlIntegrationTests
{
    private const string WriteAuthorizationVariable = "WHATS_BIZ_V83_ROLLBACK_TEST_AUTHORIZATION";
    private const string WriteAuthorizationValue = "I_AUTHORIZE_ROLLBACK_ONLY_V83_TESTS";

    [WhatsBiz.Tests.Integration.SqlIntegrationFact(Timeout = 90_000)]
    public async Task V83MigrationAndSupplierCatalogConstraintsAreTenantScopedAndRollbackOnly()
    {
        var rawConnectionString = SqlIntegrationDatabase.ConnectionString;
        var builder = new SqlConnectionStringBuilder(rawConnectionString);
        IntegrationDatabaseTarget.AssertApproved(builder);
        if (!string.Equals(Environment.GetEnvironmentVariable(WriteAuthorizationVariable), WriteAuthorizationValue, StringComparison.Ordinal))
            throw new InvalidOperationException($"Set {WriteAuthorizationVariable} to its exact rollback-only authorization value before running this SQL test.");

        var root = FindRepositoryRoot();
        var migrationPath = Path.Combine(root, "database/migrations/V83-SupplierShopifyCatalogueImport.sql");
        var validatorPath = Path.Combine(root, "database/migrations/validators/V83-SupplierShopifyCatalogueImport.validate.sql");
        var connection = new SqlConnection(rawConnectionString);
        await using (connection)
        {
            await connection.OpenAsync();
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
            await AssertExactServerAsync(connection);
            await ExecuteScriptAsync(connection, null, Path.Combine(root, "database/migrations/validators/V82-GuestVisitorAnalytics.validate.sql"));
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var rollbackBrandId = Guid.NewGuid();
            try
            {
                await EnsureV83SchemaAsync(connection, transaction, migrationPath, validatorPath);
                var supplierA = Guid.NewGuid();
                var supplierB = Guid.NewGuid();
                var categoryId = Guid.NewGuid();
                var brandId = Guid.NewGuid();
                var unitId = Guid.NewGuid();
                var productId = Guid.NewGuid();
                var draftProductId = Guid.NewGuid();
                var catalogA = Guid.NewGuid();
                var catalogB = Guid.NewGuid();
                var importId = Guid.NewGuid();
                var tenantKeyA = "V83_SQL_" + tenantA.ToString("N");
                var tenantKeyB = "V83_SQL_" + tenantB.ToString("N");

                await ExecuteAsync(connection, transaction, "INSERT core.Tenants(TenantId,TenantKey,Name,IsActive,CreatedOn,CreatedBy) VALUES(@a,@keyA,N'V83 rollback fixture',1,SYSUTCDATETIME(),N'v83-test'),(@b,@keyB,N'V83 rollback fixture',1,SYSUTCDATETIME(),N'v83-test');",
                    ("@a", tenantA), ("@keyA", tenantKeyA), ("@b", tenantB), ("@keyB", tenantKeyB));
                await ExecuteAsync(connection, transaction, "INSERT purchase.Suppliers(SupplierId,SupplierCode,SupplierName,SupplierType,TenantId) VALUES(@a,@codeA,N'V83 supplier A',N'GENERAL',@tenantA),(@b,@codeB,N'V83 supplier B',N'GENERAL',@tenantB);",
                    ("@a", supplierA), ("@codeA", "V83A" + tenantA.ToString("N")[..12]), ("@tenantA", tenantA), ("@b", supplierB), ("@codeB", "V83B" + tenantB.ToString("N")[..12]), ("@tenantB", tenantB));
                await ExecuteAsync(connection, transaction, "INSERT master.ProductCategories(ProductCategoryId,CategoryCode,CategoryName,DisplayOrder,CreatedOn,CreatedBy,IsActive,IsDeleted) VALUES(@id,@code,N'V83 category',0,SYSUTCDATETIME(),N'v83-test',1,0); INSERT master.TenantProductCategories(TenantId,ProductCategoryId) VALUES(@tenant,@id);",
                    ("@id", categoryId), ("@code", "V83C" + categoryId.ToString("N")[..12]), ("@tenant", tenantA));
                await ExecuteAsync(connection, transaction, "INSERT master.Brands(BrandId,BrandCode,BrandName,CreatedOn,CreatedBy,IsActive,IsDeleted) VALUES(@id,@code,N'V83 brand',SYSUTCDATETIME(),N'v83-test',1,0); INSERT master.TenantBrands(TenantId,BrandId) VALUES(@tenant,@id);",
                    ("@id", brandId), ("@code", "V83B" + brandId.ToString("N")[..12]), ("@tenant", tenantA));
                await ExecuteAsync(connection, transaction, "INSERT master.Brands(BrandId,BrandCode,BrandName,CreatedOn,CreatedBy,IsActive,IsDeleted) VALUES(@id,@code,N'V83 rollback-only Shopify brand',SYSUTCDATETIME(),N'v83-test',1,0); INSERT master.TenantBrands(TenantId,BrandId) VALUES(@tenant,@id);",
                    ("@id", rollbackBrandId), ("@code", "V83R" + rollbackBrandId.ToString("N")[..12]), ("@tenant", tenantA));
                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM master.Brands b JOIN master.TenantBrands tb ON tb.BrandId=b.BrandId WHERE tb.TenantId=@tenant AND b.BrandId=@brand;", ("@tenant", tenantA), ("@brand", rollbackBrandId))).Should().Be(1);
                await ExecuteAsync(connection, transaction, "INSERT master.UnitsOfMeasure(UnitId,UnitCode,UnitName,ShortName,DecimalPlaces,CreatedOn,CreatedBy,IsActive,IsDeleted) VALUES(@id,@code,N'V83 Piece',N'PCS',0,SYSUTCDATETIME(),N'v83-test',1,0); INSERT master.TenantUnitsOfMeasure(TenantId,UnitId) VALUES(@tenant,@id);",
                    ("@id", unitId), ("@code", "V83U" + unitId.ToString("N")[..12]), ("@tenant", tenantA));
                await ExecuteAsync(connection, transaction, "INSERT master.Products(ProductId,ProductCode,BarcodeType,ProductName,CategoryId,BrandId,UnitId,GSTPercentage,GstPricingType,PurchasePrice,SellingPrice,MRP,MinimumStock,MaximumStock,ReorderLevel,IsBatchManaged,IsSerialManaged,IsWhatsAppVisible,TenantId,CreatedOn,CreatedBy,IsActive,IsDeleted,ReturnPolicyMode) VALUES(@id,@code,N'CODE128',N'V83 retailer-owned product',@category,@brand,@unit,18,N'EXCLUSIVE',100,250,300,0,0,0,0,0,0,@tenant,SYSUTCDATETIME(),N'v83-test',1,0,N'INHERIT_DEFAULT');",
                    ("@id", productId), ("@code", "V83P" + productId.ToString("N")[..12]), ("@category", categoryId), ("@brand", brandId), ("@unit", unitId), ("@tenant", tenantA));
                await ExecuteAsync(connection, transaction, "INSERT master.Products(ProductId,ProductCode,BarcodeType,ProductName,CategoryId,BrandId,UnitId,GSTPercentage,GstPricingType,PurchasePrice,SellingPrice,MRP,MinimumStock,MaximumStock,ReorderLevel,IsBatchManaged,IsSerialManaged,IsWhatsAppVisible,TenantId,CreatedOn,CreatedBy,IsActive,IsDeleted,ReturnPolicyMode) VALUES(@id,@code,N'CODE128',N'V83 draft product',@category,@brand,@unit,18,N'EXCLUSIVE',100,0,0,0,0,0,0,0,0,@tenant,SYSUTCDATETIME(),N'v83-test',0,0,N'INHERIT_DEFAULT');",
                    ("@id", draftProductId), ("@code", "V83D" + draftProductId.ToString("N")[..12]), ("@category", categoryId), ("@brand", brandId), ("@unit", unitId), ("@tenant", tenantA));

                await ExecuteAsync(connection, transaction, "INSERT purchase.SupplierCatalogImportProfiles(TenantId,SupplierId,Format,FieldMappingsJson,SupplierOwnedFieldsJson,AutoCreateCategories,DefaultUnitId,DefaultBrandId,DefaultGstPercentage) VALUES(@tenant,@supplier,N'SHOPIFY',N'{\"title\":\"Title\"}',N'{\"cost\":true}',0,@unit,@brand,18);",
                    ("@tenant", tenantA), ("@supplier", supplierA), ("@unit", unitId), ("@brand", brandId));
                await AssertSqlConstraintRejectsAsync(connection, transaction,
                    "INSERT purchase.SupplierCatalogImportProfiles(TenantId,SupplierId,Format,FieldMappingsJson,SupplierOwnedFieldsJson,AutoCreateCategories) VALUES(@tenant,@supplier,N'SHOPIFY',N'{}',N'{}',0);",
                    ("@tenant", tenantA), ("@supplier", supplierA));

                await InsertCatalogRowAsync(connection, transaction, catalogA, tenantA, supplierA, productId, "SHARED-SKU", "sku:SHARED-SKU", 19m);
                await InsertCatalogRowAsync(connection, transaction, catalogB, tenantB, supplierB, null, "SHARED-SKU", "sku:SHARED-SKU", 7m);
                await ExecuteAsync(connection, transaction, "UPDATE purchase.SupplierCatalogProducts SET ProductCost=110,LastImportedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND SupplierId=@supplier AND SupplierSku=N'SHARED-SKU';",
                    ("@tenant", tenantA), ("@supplier", supplierA));
                await AssertSqlConstraintRejectsAsync(connection, transaction,
                    "INSERT purchase.SupplierCatalogProducts(SupplierCatalogProductId,TenantId,SupplierId,SupplierSku,SourceRowIdentity,OriginalTagsJson,ImageUrlsJson,LastImportedOn) VALUES(NEWID(),@tenant,@supplier,N'SHARED-SKU',N'duplicate',N'[]',N'[]',SYSUTCDATETIME());",
                    ("@tenant", tenantA), ("@supplier", supplierA));

                await ExecuteAsync(connection, transaction, "INSERT master.ProductExternalImages(ExternalImageId,TenantId,ProductId,SupplierId,ImageUrl,DisplayOrder,IsPrimary) VALUES(NEWID(),@tenant,@product,@supplier,N'https://cdn.example.test/primary.jpg',0,1),(NEWID(),@tenant,@product,@supplier,N'https://cdn.example.test/alternate.jpg',1,0);",
                    ("@tenant", tenantA), ("@product", productId), ("@supplier", supplierA));
                await ExecuteAsync(connection, transaction, "INSERT purchase.SupplierCatalogImportHistory(ImportId,TenantId,SupplierId,Format,SourceFileHash,ImportedRows,ErrorRows,ImportedOn,ImportedBy) VALUES(@id,@tenant,@supplier,N'SHOPIFY',REPLICATE('A',64),1,1,SYSUTCDATETIME(),N'v83-test');",
                    ("@id", importId), ("@tenant", tenantA), ("@supplier", supplierA));

                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM purchase.SupplierCatalogProducts WHERE TenantId=@tenant AND SupplierId=@supplier AND SupplierSku=N'SHARED-SKU';", ("@tenant", tenantA), ("@supplier", supplierA))).Should().Be(1);
                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM purchase.SupplierCatalogProducts WHERE TenantId=@tenant AND SupplierId=@supplier AND SupplierAvailableQuantity=19;", ("@tenant", tenantA), ("@supplier", supplierA))).Should().Be(1);
                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM inventory.InventoryBalances WHERE TenantId=@tenant AND ProductId=@product;", ("@tenant", tenantA), ("@product", productId))).Should().Be(0);
                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM master.ProductExternalImages WHERE TenantId=@tenant AND ProductId=@product AND ImageUrl LIKE N'https://%';", ("@tenant", tenantA), ("@product", productId))).Should().Be(2);
                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM purchase.SupplierCatalogImportHistory WHERE TenantId=@tenant AND ImportId=@id AND ImportedRows=1 AND ErrorRows=1;", ("@tenant", tenantA), ("@id", importId))).Should().Be(1);
                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM master.Products WHERE TenantId=@tenant AND ProductId=@product AND SellingPrice=250 AND MRP=300 AND IsWhatsAppVisible=0;", ("@tenant", tenantA), ("@product", productId))).Should().Be(1);
                (await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM master.Products WHERE TenantId=@tenant AND ProductId=@product AND SellingPrice=0 AND MRP=0 AND IsActive=0 AND IsWhatsAppVisible=0;", ("@tenant", tenantA), ("@product", draftProductId))).Should().Be(1);
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            (await ScalarAsync(connection, null, "SELECT COUNT(*) FROM core.Tenants WHERE TenantId IN(@a,@b);", ("@a", tenantA), ("@b", tenantB))).Should().Be(0);
            (await ScalarAsync(connection, null, "SELECT (SELECT COUNT(*) FROM master.Brands WHERE BrandId=@brand)+(SELECT COUNT(*) FROM master.TenantBrands WHERE BrandId=@brand);", ("@brand", rollbackBrandId))).Should().Be(0, "brand and tenant association are committed only with the product import transaction");
        }
    }

    private static async Task EnsureV83SchemaAsync(SqlConnection connection, SqlTransaction transaction, string migrationPath, string validatorPath)
    {
        var stateCommand = new SqlCommand("IF OBJECT_ID(N'deployment.DatabaseMigrationHistory',N'U') IS NULL THROW 83020,N'Canonical migration history is missing.',1; SELECT CASE WHEN OBJECT_ID(N'master.ProductExternalImages',N'U') IS NULL THEN 0 ELSE 1 END + CASE WHEN OBJECT_ID(N'purchase.SupplierCatalogImportProfiles',N'U') IS NULL THEN 0 ELSE 1 END + CASE WHEN OBJECT_ID(N'purchase.SupplierCatalogProducts',N'U') IS NULL THEN 0 ELSE 1 END + CASE WHEN OBJECT_ID(N'purchase.SupplierCatalogImportHistory',N'U') IS NULL THEN 0 ELSE 1 END + CASE WHEN COL_LENGTH(N'master.Products',N'PackagingCost') IS NULL THEN 0 ELSE 1 END + CASE WHEN COL_LENGTH(N'master.Products',N'ShippingCost') IS NULL THEN 0 ELSE 1 END + CASE WHEN COL_LENGTH(N'master.Products',N'CombinedShippingPackagingCost') IS NULL THEN 0 ELSE 1 END;", connection, transaction);
        await using (stateCommand)
        {
            var count = Convert.ToInt32(await stateCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            if (count != 7) throw new InvalidOperationException(count == 0
                ? "V83 is not canonically applied. Apply it through the guarded migration runner before running this SQL test."
                : "V83 schema is partially present. Refusing to execute or repair the migration from an integration test.");
        }

        var ledgerCount = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM deployment.DatabaseMigrationHistory WHERE MigrationVersion=83;");
        if (ledgerCount != 1) throw new InvalidOperationException("V83 requires exactly one canonical migration ledger entry before SQL integration tests.");
        var ledger = new SqlCommand("SELECT ScriptHash,ValidatorHash FROM deployment.DatabaseMigrationHistory WHERE MigrationVersion=83;", connection, transaction);
        await using (ledger)
        await using (var reader = await ledger.ExecuteReaderAsync())
        {
            if (!reader.Read()) throw new InvalidOperationException("V83 migration ledger row could not be read.");
            var expectedScript = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(migrationPath)));
            var expectedValidator = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(validatorPath)));
            if (!string.Equals(reader.GetString(0), expectedScript, StringComparison.OrdinalIgnoreCase) || !string.Equals(reader.GetString(1), expectedValidator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("V83 source or validator hash does not match the applied migration ledger.");
            if (reader.Read()) throw new InvalidOperationException("Multiple V83 migration ledger rows exist.");
        }
        await ExecuteScriptAsync(connection, transaction, validatorPath);
    }

    private static async Task InsertCatalogRowAsync(SqlConnection connection, SqlTransaction transaction, Guid id, Guid tenant, Guid supplier, Guid? product, string sku, string identity, decimal quantity)
    {
        await ExecuteAsync(connection, transaction, "INSERT purchase.SupplierCatalogProducts(SupplierCatalogProductId,TenantId,SupplierId,SupplierSku,SourceRowIdentity,ProductId,OriginalTagsJson,ProductCost,CombinedShippingPackagingCost,SupplierAvailableQuantity,ImageUrlsJson,LastImportedOn) VALUES(@id,@tenant,@supplier,@sku,@identity,@product,N'[\"Original\",\"Tag\"]',100,19,@quantity,N'[\"https://cdn.example.test/primary.jpg\"]',SYSUTCDATETIME());",
            ("@id", id), ("@tenant", tenant), ("@supplier", supplier), ("@sku", sku), ("@identity", identity), ("@product", product is null ? DBNull.Value : product.Value), ("@quantity", quantity));
    }

    private static async Task AssertSqlConstraintRejectsAsync(SqlConnection connection, SqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var exception = await Assert.ThrowsAsync<SqlException>(async () => await command.ExecuteNonQueryAsync());
        exception.Number.Should().BeOneOf(2601, 2627);
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteScriptAsync(SqlConnection connection, SqlTransaction? transaction, string path)
    {
        var script = await File.ReadAllTextAsync(path);
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            await using var command = new SqlCommand(batch, connection, transaction) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task AssertExactServerAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'));", connection);
        var actual = (string?)await command.ExecuteScalarAsync();
        if (!string.Equals(actual, IntegrationDatabaseTarget.Server, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("V83 SQL test connected to an unapproved SQL Server.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "database/migrations/V83-SupplierShopifyCatalogueImport.sql"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root for the V83 SQL integration test.");
    }
}
