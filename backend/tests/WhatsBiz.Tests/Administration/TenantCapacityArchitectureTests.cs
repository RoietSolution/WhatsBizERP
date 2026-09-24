using FluentAssertions;

namespace WhatsBiz.Tests.Administration;

public sealed class TenantCapacityArchitectureTests
{
    private static readonly string Root=FindRoot();

    [Fact]
    public void V37DefinesSafeGenericLimitsAndConcurrencyBackstops()
    {
        var sql=File.ReadAllText(Path.Combine(Root,"database","WhatsBiz.Database","Scripts","V37-TenantResourceCapacity.sql"));
        sql.Should().Contain("core.TenantResourceLimits").And.Contain("ResourceType IN(N'USERS',N'BRANCHES')").And.Contain("LimitValue IS NULL OR LimitValue>=1");
        sql.Should().Contain("PRIMARY KEY(TenantId,ResourceType)").And.Contain("FK_TenantResourceLimits_Tenants");
        sql.Should().Contain("TR_Users_ResourceCapacity").And.Contain("TR_Branches_ResourceCapacity").And.Contain("WITH(UPDLOCK,HOLDLOCK)");
        sql.Should().NotContain("INSERT core.TenantResourceLimits");
    }

    [Fact]
    public void CapacityApiSeparatesPlatformUpdatesFromTenantReadOnlyAccess()
    {
        var source=File.ReadAllText(Path.Combine(Root,"backend","src","WhatsBiz.Api","Controllers","TenantCapacityController.cs"));
        source.Should().Contain("api/capacity").And.Contain("api/system/tenants/{tenantId:guid}/capacity");
        source.Should().Contain("PlatformAuthorize").And.Contain("Permissions.Features.Manage");
        source.Split("[HttpPut",StringSplitOptions.None).Should().HaveCount(2);
    }

    [Fact]
    public void CapacityServiceCountsOnlyUsableRetailerUsersAndActiveBranches()
    {
        var source=File.ReadAllText(Path.Combine(Root,"backend","src","WhatsBiz.Infrastructure","Features","TenantResourceLimitService.cs"));
        source.Should().Contain("u.AccountType=N'RETAILER' AND u.IsActive=1 AND u.IsDeleted=0");
        source.Should().Contain("b.IsActive=1").And.Contain("CAPACITY_UPDATE").And.Contain("oldLimit").And.Contain("newLimit");
        source.Should().Contain("NOT_CONFIGURED").And.Contain("TENANT_OVERRIDE");
    }

    [Fact]
    public void V37IsInCanonicalDeploymentChain()
    {
        File.ReadAllText(Path.Combine(Root,"database","WhatsBiz.Database","Scripts","PostDeployment.sql")).Should().Contain("V37-TenantResourceCapacity.sql");
        File.ReadAllText(Path.Combine(Root,"database","WhatsBiz.Database","WhatsBiz.Database.sqlproj")).Should().Contain("V37-TenantResourceCapacity.sql");
    }

    private static string FindRoot(){var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!Directory.Exists(Path.Combine(directory.FullName,"backend")))directory=directory.Parent;return directory?.FullName??throw new DirectoryNotFoundException();}
}
