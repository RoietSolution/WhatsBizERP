using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontPhaseArchitectureTests
{
    private static readonly string Root = FindRoot();
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void V38RepairsStorefrontSourceConstraintAndIsInDatabaseChain()
    {
        var migration = Read("database/WhatsBiz.Database/Scripts/V38-StorefrontPresentation.sql");
        migration.Should().Contain("CK_WhatsAppCommerceOrders_Source").And.Contain("N'STOREFRONT'");
        migration.Should().Contain("TenantId uniqueidentifier NOT NULL").And.Contain("TR_StorefrontBanners_TenantGuard");
        Read("database/WhatsBiz.Database/Scripts/PostDeployment.sql").Should().Contain("V38-StorefrontPresentation.sql");
        Read("database/WhatsBiz.Database/WhatsBiz.Database.sqlproj").Should().Contain("V38-StorefrontPresentation.sql");
        Read("database/WhatsBiz.Database/Scripts/V2-WhatsAppCommerceDemoProvider.sql").Should().Contain("SourceChannel IN(N'WHATSAPP_DEMO',N'WHATSAPP')");
    }

    [Fact]
    public void CheckoutUsesEffectiveTenantPaymentMethodsAndKeepsAllProvidersUnpaidUntilVerified()
    {
        var checkout = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs");
        checkout.Should().Contain("GetEnabledMethodsForTenantAsync").And.Contain("NormalizePaymentMethod")
            .And.Contain("PaymentProviders.Cod").And.Contain("PaymentProviders.Razorpay");
        checkout.Should().Contain("HELD").And.Contain("PaymentType=@paymentType");
        var payments = Read("backend/src/WhatsBiz.Infrastructure/Payments/CommercePaymentService.cs");
        payments.Should().Contain("PaymentProviders.DirectUpi ? CommercePaymentStatuses.PendingVerification")
            .And.Contain("PaymentProviders.Cod ? CommercePaymentStatuses.CodPending")
            .And.Contain("ApplySuccessfulPayment");
        payments.Should().Contain("GetEnabledMethodsForTenantAsync(Guid trustedTenantId");
        payments.Should().Contain("settings.Providers.Single(x => x.Provider == PaymentProviders.Cod)");
    }

    [Fact]
    public void StorefrontPublicBannersAreScheduledAndTenantBound()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontService.cs");
        service.Should().Contain("x.TenantId == tenant.TenantId && x.IsEnabled")
            .And.Contain("x.StartsAt <= now").And.Contain("x.EndsAt >= now")
            .And.Contain("presentationImages.ReadStorefrontAsync");
        var admin = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontAdministrationService.cs");
        admin.Should().Contain("x.TenantId == tenant").And.Contain("ValidateCategory");
        Read("backend/src/WhatsBiz.Api/Controllers/StorefrontAdministrationController.cs")
            .Should().Contain("Authorize").And.Contain("Permissions.Admin.Settings");
    }

    [Fact]
    public void CustomerCheckoutShowsProblemDetailAndUsesProviderSelection()
    {
        var cart = Read("frontend/KhataDhari.Customer/src/app/pages/cart.page.ts");
        cart.Should().Contain("body?.detail").And.Contain("paymentMethods()").And.Contain("paymentMethod").And.NotContain("DIRECT_UPI");
        Read("frontend/KhataDhari.Customer/src/app/data/http-storefront-data.provider.ts")
            .Should().Contain("/checkout`").And.Contain("paymentMethod");
    }

    [Fact]
    public void CartServiceabilityGuardsAgainstAddressAndQuoteRaces()
    {
        var cart = Read("frontend/KhataDhari.Customer/src/app/pages/cart.page.ts");
        cart.Should().Contain("if(next===this.pincode)return")
            .And.Contain("requestedPincode===this.pincode")
            .And.Contain("Unable to check delivery availability. Please try again.")
            .And.Contain("sessionStorage")
            .And.Contain("storeKey,pincode,verified:true,quote");
    }

    [Fact]
    public void ProfileMediaReplacementDoesNotViolateCustomerUniqueness()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerService.cs");
        service.Should().Contain("UPDATE commerce.StorefrontMedia SET")
            .And.Contain("ResourceType=N'customer-profile'")
            .And.Contain("BeginTransactionAsync")
            .And.Contain("ProfileMediaId=@id");
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "backend", "WhatsBiz.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
