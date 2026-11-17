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
    public void CheckoutIdempotencyFingerprintCoversMutableDeliveryAndPaymentFields()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Infrastructure", "Storefront", "StorefrontCheckoutService.cs"));
        source.Should().Contain("CheckoutIdempotencyRequest")
            .And.Contain("string DeliveryAddress")
            .And.Contain("string PaymentMethod")
            .And.Contain("string RecipientName")
            .And.Contain("string Mobile")
            .And.Contain("JsonSerializer.Serialize(idempotencyRequest)")
            .And.Contain("idempotencyFingerprint), token")
            .And.Contain("idempotencyRequest);");

        var executor = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Infrastructure", "Persistence", "SqlIdempotencyExecutor.cs"));
        executor.Should().Contain("JsonSerializer.Serialize(request, JsonOptions)")
            .And.Contain("FixedTimeEquals(existing.Value.Hash, hash)")
            .And.Contain("The idempotency key was already used for a different request.");
    }

    [Fact]
    public void StorefrontPaymentRetryReusesAnActiveAttemptUnderTheInvoiceLock()
    {
        var service = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Infrastructure", "Payments", "CommercePaymentService.cs"));
        service.Should().Contain("ReadInvoice(connection, tx, tenant, input.OrderId, true, token)")
            .And.Contain("string.Equals(actor, \"STOREFRONT\", StringComparison.Ordinal)")
            .And.Contain("Status IN(N'PENDING',N'PENDING_VERIFICATION',N'COD_PENDING')")
            .And.Contain("return new(existingPayment,existingPayment.PaymentLink);");
        service.IndexOf("activeAttempt", StringComparison.Ordinal)
            .Should().BeLessThan(service.IndexOf("INSERT commerce.CommercePayments", StringComparison.Ordinal));
    }

    [Fact]
    public void CheckoutPricesServerSideAndUsesTenantScopedPaymentCreation()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Infrastructure", "Storefront", "StorefrontCheckoutService.cs"));
        source.Should().Contain("p.SellingPrice").And.Contain("p.GSTPercentage");
        source.Should().Contain("QuantityOnHand-b.QuantityReserved");
        source.Should().Contain("CreateAttemptForTenantAsync").And.Contain("PaymentProviders.Razorpay").And.Contain("p.IsActive=1");
        source.Should().NotContain("KeySecret").And.NotContain("WebhookSecret");
    }

    [Fact]
    public void CartPreviewUsesASeparateRateLimitAndNeverAcceptsAServiceabilityPincode()
    {
        var method = typeof(StoreController).GetMethod(nameof(StoreController.CartPreview));
        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Should().ContainSingle();
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("StorefrontCartPreview");

        var source = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Api", "Controllers", "StoreController.cs"));
        source.Should().Contain("input with { Pincode = null }");
    }

    [Fact]
    public void CustomerCartUsesPreviewPathInsteadOfDeliveryCheckClient()
    {
        var root = Root();
        var cart = File.ReadAllText(Path.Combine(root, "frontend", "KhataDhari.Customer", "src", "app", "pages", "cart.page.ts"));
        cart.Should().Contain("previewQuote").And.Contain("if(!this.isCheckoutPage())return;");
        cart.Should().NotContain("data.checkDelivery");
    }

    [Fact]
    public void CheckoutStillRejectsUnavailableDeliveryBeforeCreatingAnOrder()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Infrastructure", "Storefront", "StorefrontCheckoutService.cs"));

        source.Should().Contain("if (!quote.IsDeliveryEnabled)")
            .And.Contain("if (!quote.IsPincodeServiceable)")
            .And.Contain("var order = await pos.PostForTenant");
        source.IndexOf("if (!quote.IsPincodeServiceable)", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("var order = await pos.PostForTenant", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpectedPayableIsOnlyAComparisonGuardBeforeCustomerAndOrderWrites()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "backend", "src", "WhatsBiz.Infrastructure", "Storefront", "StorefrontCheckoutService.cs"));
        var guard = source.IndexOf("StorefrontPricingPolicy.EnsureExpectedPayable(input.ExpectedPayableAmount, quote.FinalPayableAmount)", StringComparison.Ordinal);
        guard.Should().BeGreaterThan(source.IndexOf("if (!quote.IsPincodeServiceable)", StringComparison.Ordinal));
        guard.Should().BeLessThan(source.IndexOf("var customerId = authenticated?.Id ?? await FindOrCreateCustomer", StringComparison.Ordinal));
        guard.Should().BeLessThan(source.IndexOf("var order = await pos.PostForTenant", StringComparison.Ordinal));
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
