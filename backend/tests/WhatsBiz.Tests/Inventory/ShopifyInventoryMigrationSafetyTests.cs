using FluentAssertions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.RegularExpressions;

namespace WhatsBiz.Tests.Inventory;

/// <summary>Source contracts only; SQL behavior requires the authorized DEV V92 deployment and SQL tests.</summary>
public sealed class ShopifyInventoryMigrationSafetyTests
{
    [Fact]
    public void V92MigrationAndEmbeddedDynamicSqlParseForSqlServer2022()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V92-RetailerShopifyImports.sql"));

        AssertParses(migration, "V92 migration");

        var dynamicSql = Regex.Matches(migration, @"EXEC\s*\(\s*N'(?<sql>(?:''|[^'])*)'\s*\)\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Groups["sql"].Value.Replace("''", "'", StringComparison.Ordinal))
            .ToList();
        var snapshotProcedure = Regex.Match(migration, @"DECLARE\s+@shopifySnapshotProc\s+nvarchar\(max\)\s*=\s*N'(?<sql>(?:''|[^'])*)';\s*EXEC\s*\(@shopifySnapshotProc\)\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        snapshotProcedure.Success.Should().BeTrue("the migration's generated snapshot procedure must be found and parsed independently");
        dynamicSql.Add(snapshotProcedure.Groups["sql"].Value.Replace("''", "'", StringComparison.Ordinal));
        dynamicSql.Should().HaveCount(5, "the schema, three tenant triggers and snapshot procedure must all be validated");

        for (var index = 0; index < dynamicSql.Count; index++)
            AssertParses(dynamicSql[index], $"V92 embedded SQL block {index + 1}");
    }

    [Fact]
    public void V92ConstrainsImportIdentityAndHistoryByTenantAndSourceHash()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V92-RetailerShopifyImports.sql"));
        var validator = File.ReadAllText(Path.Combine(root, "database/migrations/validators/V92-RetailerShopifyImports.validate.sql"));

        migration.Should().Contain("UNIQUE INDEX UX_RetailerShopifyProductSources_Identity ON integration.RetailerShopifyProductSources(TenantId,SourceStore,Handle,VariantSku)")
            .And.Contain("UNIQUE INDEX UX_RetailerShopifyProductImportHistory_File ON integration.RetailerShopifyProductImportHistory(TenantId,SourceStore,SourceFileHash)")
            .And.Contain("UNIQUE INDEX UX_ShopifyInventoryImportHistory_File ON integration.ShopifyInventoryImportHistory(TenantId,SourceStore,SourceFileHash)")
            .And.Contain("TR_RetailerShopifyProductSources_TenantGuard")
            .And.Contain("TR_ShopifyInventoryImportHistory_TenantGuard")
            .And.Contain("StockAdjustment_Post') AND p.name=N'@TenantId'");
        validator.Should().Contain("inventory.ShopifySnapshot_Post")
            .And.Contain("@TenantId");
        migration.Should().NotContain("MARKETPLACE_CHECKOUT");
    }

    [Fact]
    public void SnapshotLocksBalancesAndNeverReconcilesBelowReservedQuantity()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V92-RetailerShopifyImports.sql"));
        var procedure = migration[(migration.IndexOf("CREATE OR ALTER PROCEDURE inventory.ShopifySnapshot_Post", StringComparison.Ordinal))..];

        procedure.Should().Contain("WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("x.TargetQuantity<(SELECT ISNULL(SUM(b.QuantityReserved),0)")
            .And.Contain("Stock changed after preview")
            .And.Contain("inventory.StockAdjustment_Post")
            .And.Contain("COMMIT;");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static void AssertParses(string sql, string name)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        _ = parser.Parse(reader, out IList<ParseError> errors);
        errors.Should().BeEmpty("{0} must parse as SQL Server 2022 T-SQL: {1}", name,
            string.Join(Environment.NewLine, errors.Select(error => $"Line {error.Line}, column {error.Column}: {error.Message}")));
    }
}
