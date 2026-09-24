using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontCheckoutArchitectureTests
{
    [Fact]
    public void CheckoutAcceptsOnlyProductIdentityAndQuantityFromTheBrowser()
    {
        typeof(StorefrontCheckoutItem).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(["ProductId", "Quantity"]);
        typeof(StorefrontCheckoutInput).GetProperties().Select(property => property.Name)
            .Should().NotContain(["Amount", "Price", "TenantId", "WarehouseId", "Provider"]);
    }

    [Fact]
    public void PublicCheckoutRequiresIdempotencyAndHasDedicatedRateLimiting()
    {
        typeof(StoreController).GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        var method = typeof(StoreController).GetMethod(nameof(StoreController.Checkout));
        method.Should().NotBeNull();
        method!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("StorefrontCheckout");

        var root = Root();
        var controller = File.ReadAllText(Path.Combine(root, "backend", "src", "WhatsBiz.Api", "Controllers", "StoreController.cs"));
        controller.Should().Contain("Idempotency-Key").And.Contain("Guid.TryParse");
    }

    [Fact]
    public void CheckoutPricesServerSideAndUsesTenantScopedPaymentCreation()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Infrastructure", "Storefront", "StorefrontCheckoutService.cs"));
        source.Should().Contain("p.SellingPrice").And.Contain("p.GSTPercentage");
        source.Should().Contain("QuantityOnHand-b.QuantityReserved");
        source.Should().Contain("CreateAttemptForTenantAsync").And.Contain("PaymentProviders.Razorpay");
        source.Should().NotContain("KeySecret").And.NotContain("WebhookSecret");
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
