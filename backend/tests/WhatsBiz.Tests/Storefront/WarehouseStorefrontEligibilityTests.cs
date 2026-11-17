using FluentAssertions;
using WhatsBiz.Application.Features.Warehouses;

namespace WhatsBiz.Tests.Storefront;

public sealed class WarehouseStorefrontEligibilityTests
{
    [Fact]
    public void ConfiguredActiveWarehouseIsEligibleWithinRetailerServiceArea()
    {
        var result = WarehouseStorefrontEligibilityPolicy.Evaluate(true, true, true, true, true, true, true);
        result.IsEligible.Should().BeTrue();
        result.Code.Should().Be("ELIGIBLE");
    }

    [Fact]
    public void InactiveWarehouseIsRejected()
    {
        var result = WarehouseStorefrontEligibilityPolicy.Evaluate(true, false, true, true, true, true, true);
        result.IsEligible.Should().BeFalse();
        result.Code.Should().Be("WAREHOUSE_INACTIVE");
    }

    [Fact]
    public void UnsupportedRetailerLocationIsRejected()
    {
        var result = WarehouseStorefrontEligibilityPolicy.Evaluate(true, true, true, true, true, false, true);
        result.IsEligible.Should().BeFalse();
        result.Code.Should().Be("OUTSIDE_RETAILER_SERVICE_AREA");
    }

    [Fact]
    public void WarehouseOutsideItsExplicitServiceAreaIsRejected()
    {
        var result = WarehouseStorefrontEligibilityPolicy.Evaluate(true, true, true, true, true, true, false);
        result.IsEligible.Should().BeFalse();
        result.Code.Should().Be("WAREHOUSE_SERVICE_AREA_NOT_CONFIGURED");
    }

    [Fact]
    public void MissingWarehouseConfigurationFailsClosed()
    {
        var result = WarehouseStorefrontEligibilityPolicy.Evaluate(true, true, false, true, true, true, false);
        result.IsEligible.Should().BeFalse();
        result.Code.Should().Be("WAREHOUSE_SERVICE_AREA_NOT_CONFIGURED");
    }

    [Fact]
    public void CrossTenantWarehouseIsNotFoundAndPartnerMismatchIsRejected()
    {
        var crossTenant = WarehouseStorefrontEligibilityPolicy.Evaluate(false, false, false, false, false, false, false);
        crossTenant.IsEligible.Should().BeFalse();
        crossTenant.Code.Should().Be("WAREHOUSE_NOT_FOUND");

        var sellerMismatch = WarehouseStorefrontEligibilityPolicy.Evaluate(true, true, true, false, true, true, true);
        sellerMismatch.IsEligible.Should().BeFalse();
        sellerMismatch.Code.Should().Be("SELLER_INELIGIBLE");
    }

    [Fact]
    public void EligibilityQueryScopesWarehouseSellerProductAndCityToOperatingTenant()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var service = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Warehouses/WarehouseStorefrontFulfilmentService.cs"));
        service.Should().Contain("WHERE w.TenantId=@tenant AND w.WarehouseId=@warehouse")
            .And.Contain("l.OperatingTenantId=@tenant AND l.ProductId=@product AND l.PartnerSellerId=@seller")
            .And.Contain("s.CityId=@city")
            .And.Contain("a.Pincode=@pincode AND a.IsEnabled=1")
            .And.Contain("CheckWithinTransactionAsync")
            .And.NotContain("QuantityAvailable");
    }

    [Fact]
    public void V86MigrationUsesAnExplicitFailClosedTenantSellerPincodeAllowlist()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V86-WarehouseStorefrontFulfilmentEligibility.sql"));
        migration.Should().Contain("IsEnabled bit NOT NULL")
            .And.Contain("DEFAULT(0)")
            .And.Contain("FOREIGN KEY(TenantId,WarehouseId)")
            .And.Contain("FOREIGN KEY(TenantId,PartnerSellerId)")
            .And.Contain("Pincode char(6) NOT NULL")
            .And.NotContain("INSERT commerce.WarehouseStorefrontServiceAreas");
        var validator = File.ReadAllText(Path.Combine(root, "database/migrations/validators/V86-WarehouseStorefrontFulfilmentEligibility.validate.sql"));
        validator.Should().Contain("is_disabled=0 AND is_not_trusted=0");
    }
}
