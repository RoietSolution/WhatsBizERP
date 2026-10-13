using FluentAssertions;

namespace WhatsBiz.Tests.Security;

public sealed class WarehouseTenantIsolationArchitectureTests
{
    private static readonly string Root = FindRoot();
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void WarehouseIndexesScopeCodeNameAndDefaultByTenant()
    {
        var schema = Read("database/WhatsBiz.Database/Tables/Warehouses.sql");
        schema.Should().Contain("UX_Warehouses_Code] ON [inventory].[Warehouses]([TenantId],[WarehouseCode])")
            .And.Contain("UX_Warehouses_Name] ON [inventory].[Warehouses]([TenantId],[WarehouseName])")
            .And.Contain("UX_Warehouses_Default] ON [inventory].[Warehouses]([TenantId],[IsDefault])");
    }

    [Fact]
    public void WarehouseRepositoryUsesAuthenticatedTenantForSameTenantConflictChecks()
    {
        var repository = Read("backend/src/WhatsBiz.Infrastructure/Persistence/WarehouseRepository.cs");
        repository.Should().Contain("EF.Property<Guid?>(x, \"TenantId\") == Tenant")
            .And.Contain("x.WarehouseCode == code.Trim()")
            .And.Contain("x.WarehouseName == name.Trim()");
    }

    [Fact]
    public void WarehouseCreateUsesTenantContextAndReturnsWarehouseSpecificConflict()
    {
        var context = Read("backend/src/WhatsBiz.Infrastructure/Persistence/ApplicationDbContext.cs");
        var middleware = Read("backend/src/WhatsBiz.Api/Middleware/GlobalExceptionMiddleware.cs");
        context.Should().Contain("TenantOwnedTypes").And.Contain("entry.Property(\"TenantId\").CurrentValue = tenant.Value");
        middleware.Should().Contain("/api/warehouses")
            .And.Contain("warehouse with the same code, name, or default setting");
    }

    [Fact]
    public void NewTenantEnrollmentDoesNotInsertAnAutomaticWarehouse()
    {
        var enrollment = Read("backend/src/WhatsBiz.Infrastructure/Features/TenantEnrollmentService.cs");
        enrollment.Should().Contain("INSERT core.Tenants")
            .And.Contain("INSERT admin.Companies")
            .And.NotContain("INSERT inventory.Warehouses");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "frontend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
