using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontDeliveryCoverageArchitectureTests
{
    private static readonly string Root = FindRoot();
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void V60AddsBackwardCompatibleCoverageModeAndTenantScopedExclusions()
    {
        var migration = Read("database/migrations/V60-StorefrontDeliveryCoverage.sql");
        var validator = Read("database/migrations/validators/V60-StorefrontDeliveryCoverage.validate.sql");
        migration.Should().Contain("DEFAULT('ALLOWLIST')").And.Contain("IsExcluded").And.Contain("TenantId, IsExcluded, IsActive, Pincode")
            .And.Contain("sys.sp_executesql").And.NotContain("ADD CONSTRAINT CK_StorefrontConfigurations_DeliveryCoverageMode\n        CHECK (DeliveryCoverageMode");
        validator.Should().Contain("DeliveryCoverageMode").And.Contain("IsExcluded").And.Contain("IX_StorefrontServiceablePincodes_Coverage");
    }

    [Fact]
    public void CheckoutAndAddressValidationUseTheSameCoveragePrecedence()
    {
        var checkout = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs");
        var customer = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerService.cs");
        checkout.Should().Contain("@mode='PAN_INDIA'").And.Contain("IsExcluded=1").And.Contain("IsExcluded=0")
            .And.Contain("CASE WHEN s.DeliveryEnabled=1 THEN 1 ELSE 0 END");
        customer.Should().Contain("COALESCE(s.DeliveryCoverageMode,'ALLOWLIST')='PAN_INDIA'")
            .And.Contain("IsExcluded=1").And.Contain("IsExcluded=0");
    }

    [Fact]
    public void CustomerLoginUsesValidatedReturnUrlAndCatalogFallback()
    {
        var auth = Read("frontend/KhataDhari.Customer/src/app/pages/customer-auth.page.ts");
        auth.Should().Contain("safeReturn??('/'+this.storeKey)")
            .And.Contain("!returnUrl.startsWith('//')")
            .And.Contain("!returnUrl.includes('://')");
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "backend"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
