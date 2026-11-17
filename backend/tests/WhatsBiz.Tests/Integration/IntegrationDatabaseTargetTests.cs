using Microsoft.Data.SqlClient;
using System.Text;

namespace WhatsBiz.Tests.Integration;

public sealed class IntegrationDatabaseTargetTests
{
    [Fact]
    public void CentralMarketplaceIntegrationTargetIsTheApprovedDisposableDatabase()
    {
        Assert.Equal("DESKTOP-DQ0868S", IntegrationDatabaseTarget.Server);
        Assert.Equal("WhatsBizERP_IntegrationTest_V82", IntegrationDatabaseTarget.Database);
    }

    [Fact]
    public void MarketplaceSqlHarnessIsPinnedToRollbackOnlyDevelopmentTarget()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var source = File.ReadAllText(Path.Combine(root, "backend/tests/WhatsBiz.Tests/Integration/MarketplaceV85V91SqlIntegrationTests.cs"), Encoding.UTF8);
        Assert.Contains("Server=lpc:DESKTOP-DQ0868S;Database=WhatsBizERP", source, StringComparison.Ordinal);
        Assert.Contains("WHATS_BIZ_DEV_MARKETPLACE_SQL_AUTHORIZATION", source, StringComparison.Ordinal);
        Assert.Contains("I_AUTHORIZE_DEV_MARKETPLACE_ROLLBACK_FIXTURES", source, StringComparison.Ordinal);
        Assert.Contains("AssertDatabaseIdentityAsync(connection, transaction)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlIntegrationDatabase.ConnectionString", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WhatsBizERP_IntegrationTest_V82", source, StringComparison.Ordinal);
        Assert.Contains("RequireFixtureAuthorization();", source, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync()", source, StringComparison.Ordinal);
        Assert.Contains("new SqlCommand(batch, connection, transaction)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConnectionString(\"DefaultConnection\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WhatsBizERP_QA", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WhatsBizERP_PROD", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceFixtureSetsGuidTenantContextBeforeWarehouseWritesAndCannotReuseSession()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var source = File.ReadAllText(Path.Combine(root, "backend/tests/WhatsBiz.Tests/Integration/MarketplaceV85V91SqlIntegrationTests.cs"), Encoding.UTF8);
        var seed = source.IndexOf("public async Task SeedAsync()", StringComparison.Ordinal);
        var tenantInsert = source.IndexOf("INSERT core.Tenants(TenantId,TenantKey,Name)", seed, StringComparison.Ordinal);
        var setContext = source.IndexOf("await SetTenantContextAsync();", tenantInsert, StringComparison.Ordinal);
        var warehouseInsert = source.IndexOf("INSERT inventory.Warehouses(", setContext, StringComparison.Ordinal);
        var inventoryInsert = source.IndexOf("INSERT inventory.InventoryBalances(", setContext, StringComparison.Ordinal);

        Assert.True(seed >= 0 && tenantInsert > seed && setContext > tenantInsert && warehouseInsert > setContext && inventoryInsert > setContext,
            "the test tenant must exist and be installed in SESSION_CONTEXT before guarded writes");
        Assert.Contains("TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'))", source, StringComparison.Ordinal);
        Assert.Contains("Pooling=False", source, StringComparison.Ordinal);
        Assert.Contains("ClearTenantContextBestEffortAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DISABLE TRIGGER", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommittedMarketplaceServiceFixtureIsPinnedToTheSyntheticV91Target()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var serviceTests = File.ReadAllText(Path.Combine(root, "backend/tests/WhatsBiz.Tests/Integration/MarketplaceV91ServiceSqlIntegrationTests.cs"), Encoding.UTF8);
        var fixture = File.ReadAllText(Path.Combine(root, "backend/tests/WhatsBiz.Tests/Integration/MarketplaceV85V91SqlIntegrationTests.cs"), Encoding.UTF8);

        Assert.Contains("WhatsBizERP_GuturGoMarketplaceTest_V91", serviceTests, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__GuturGoMarketplaceTests", serviceTests, StringComparison.Ordinal);
        Assert.Contains("WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_SQL_AUTHORIZATION", serviceTests, StringComparison.Ordinal);
        Assert.Contains("AssertSyntheticOnlyScopeAsync", serviceTests, StringComparison.Ordinal);
        Assert.Contains("Enumerable.Range(41, 51)", fixture, StringComparison.Ordinal);
        Assert.Contains("Baseline-V40.validate.sql", fixture, StringComparison.Ordinal);
        Assert.Contains("SqlTransaction transaction)", fixture, StringComparison.Ordinal);
        Assert.Contains("CreateCommittedSyntheticV91", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture(SqlConnection connection, SqlTransaction? transaction)", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConnectionString(\"DefaultConnection\")", serviceTests, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoredMarketplaceCopyUsesSeparateReadinessContractAndNeverReplaysSchema()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var serviceTests = File.ReadAllText(Path.Combine(root, "backend/tests/WhatsBiz.Tests/Integration/MarketplaceV91ServiceSqlIntegrationTests.cs"), Encoding.UTF8);
        var readiness = File.ReadAllText(Path.Combine(root, "deployment/register-marketplace-v91-restored-copy-readiness.ps1"), Encoding.UTF8);

        Assert.Contains("WhatsBiz.GuturGoMarketplaceRestoredV91Readiness", serviceTests, StringComparison.Ordinal);
        Assert.Contains("ComputeV91MigrationFingerprint", serviceTests, StringComparison.Ordinal);
        Assert.Contains("GUTURGO_SYNTHETIC_RESTORED_V91_V1", serviceTests, StringComparison.Ordinal);
        Assert.Contains("WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_READINESS_AUTHORIZATION", readiness, StringComparison.Ordinal);
        Assert.Contains("msdb.dbo.restorehistory", readiness, StringComparison.Ordinal);
        Assert.Contains("SanitizationReference", readiness, StringComparison.Ordinal);
        Assert.Contains("WhatsBiz.DisposableIntegrationDatabase", readiness, StringComparison.Ordinal);
        Assert.Contains("sp_addextendedproperty", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("RESTORE DATABASE", readiness, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("V40 DACPAC Publish", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-DatabaseMigrationWorkflow", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void CommittedMarketplaceFixturesAreRunScopedRetainedAndStaleRunsFailClosed()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var serviceTests = File.ReadAllText(Path.Combine(root, "backend/tests/WhatsBiz.Tests/Integration/MarketplaceV91ServiceSqlIntegrationTests.cs"), Encoding.UTF8);
        var fixture = File.ReadAllText(Path.Combine(root, "backend/tests/WhatsBiz.Tests/Integration/MarketplaceV85V91SqlIntegrationTests.cs"), Encoding.UTF8);

        Assert.Contains("private static readonly Guid TestRunId = Guid.NewGuid()", serviceTests, StringComparison.Ordinal);
        Assert.Contains("ReadCommittedFixtureFootprintAsync", serviceTests, StringComparison.Ordinal);
        Assert.Contains("Committed marketplace test found stale synthetic data before writing", serviceTests, StringComparison.Ordinal);
        Assert.Contains("TenantKey LIKE N'MKTSQLTEST%' AND TenantKey NOT LIKE @runPrefix", serviceTests, StringComparison.Ordinal);
        Assert.Contains("CreateCommittedSyntheticV91(connection, TestRunId)", serviceTests, StringComparison.Ordinal);
        Assert.Contains("Committed marketplace fixtures are allowed only on the dedicated synthetic V91 test database", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM commerce.MarketplaceAllocationReservations", serviceTests, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM commerce.PartnerSettlementEntries", serviceTests, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DISABLE TRIGGER", serviceTests, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedTargetConfigurationAcceptsOnlyTheReusableDatabase()
    {
        var approved = new SqlConnectionStringBuilder
        {
            DataSource = IntegrationDatabaseTarget.Server,
            InitialCatalog = IntegrationDatabaseTarget.Database,
            IntegratedSecurity = true
        };

        IntegrationDatabaseTarget.AssertApproved(approved);
        Assert.Throws<InvalidOperationException>(() => IntegrationDatabaseTarget.AssertApproved(new SqlConnectionStringBuilder
        {
            DataSource = IntegrationDatabaseTarget.Server,
            InitialCatalog = "WhatsBizERP_IntegrationTest",
            IntegratedSecurity = true
        }));
        var explicitTcp = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{IntegrationDatabaseTarget.Server},1433",
            InitialCatalog = IntegrationDatabaseTarget.Database,
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true
        };
        IntegrationDatabaseTarget.AssertApproved(explicitTcp);
        explicitTcp.DataSource = $"tcp:{IntegrationDatabaseTarget.Server},1434";
        Assert.Throws<InvalidOperationException>(() => IntegrationDatabaseTarget.AssertApproved(explicitTcp));
        Assert.Throws<InvalidOperationException>(() => IntegrationDatabaseTarget.AssertApproved(new SqlConnectionStringBuilder
        {
            DataSource = "other-server",
            InitialCatalog = IntegrationDatabaseTarget.Database,
            IntegratedSecurity = true
        }));
        Assert.Throws<InvalidOperationException>(() => IntegrationDatabaseTarget.AssertApproved(new SqlConnectionStringBuilder
        {
            DataSource = IntegrationDatabaseTarget.Server,
            InitialCatalog = "WhatsBizERP_QA",
            IntegratedSecurity = true
        }));
    }
}
