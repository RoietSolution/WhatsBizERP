using FluentAssertions;

namespace WhatsBiz.Tests.Features;

public sealed class V31ConfigurationTests
{
    private static string Script()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "database", "WhatsBiz.Database", "Scripts", "V31-TenantEntityCodesAndV1Printing.sql"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void EntityCodeFormatsAreIndependentAndSequenceCountersAreInternal()
    {
        var sql = Script();
        sql.Should().Contain("CUSTOMER_CODE_PREFIX").And.Contain("SUPPLIER_CODE_PREFIX").And.Contain("PRODUCT_CODE_PREFIX");
        sql.Should().Contain("CUSTOMER_CODE_NEXT_NUMBER").And.Contain("N'Internal'");
    }

    [Fact]
    public void ConcurrentGenerationUsesSerializableTenantScopedRowLocks()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "backend", "src", "WhatsBiz.Infrastructure", "Persistence", "EntityCodeService.cs"));
        var source = File.ReadAllText(sourcePath);
        source.Should().Contain("IsolationLevel.Serializable")
            .And.Contain("UPDLOCK,HOLDLOCK")
            .And.Contain("CompanyId=@company AND SettingKey=@nextKey");
    }

    [Fact]
    public void PrintingRemainsAPlanEntitlementAndOnlyInitializedTenantRowsAreReconciled()
    {
        var sql = Script();
        sql.Should().Contain("PlanKey=N'V1_DEFAULT'");
        sql.Should().Contain("FeatureKey=N'PRINTING'");
        sql.Should().Contain("tf.Reason=N'Initialized from active subscription plan'");
        sql.Should().NotContain("WHATSAPP_COMMERCE',N'1");
    }
}
