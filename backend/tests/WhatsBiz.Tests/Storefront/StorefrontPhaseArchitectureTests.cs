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
        cart.Should().Contain("deliverySignature(this.cart.storeKey(),this.cart.lines(),next)")
            .And.Contain("deliveryInFlight?.signature===signature")
            .And.Contain("signature!==this.deliverySignature(this.cart.storeKey(),this.cart.lines(),this.pincode)")
            .And.Contain("this.invalidateDelivery()")
            .And.Contain("Unable to check delivery availability. Please try again.")
            .And.Contain("sessionStorage")
            .And.Contain("verified:true,verifiedAtUtc:Date.now(),quote");
    }

    [Fact]
    public void CartAndWishlistStorageIsOwnedByStoreAndCustomer()
    {
        var cart = Read("frontend/KhataDhari.Customer/src/app/cart/cart.service.ts");
        cart.Should().Contain("private storageKey(storeKey: string, customerId: string)")
            .And.Contain("khata-dhari-cart:${storeKey}:${customerId || 'guest'}")
            .And.Contain("this.storageKey(storeKey, nextCustomer)")
            .And.Contain("customerId || 'guest'")
            .And.Contain("switchCustomer(customerId: string | null)")
            .And.Contain("adoptedGuest");
        var wishlist = Read("frontend/KhataDhari.Customer/src/app/wishlist.service.ts");
        wishlist.Should().Contain("localKey(customerId = this.customerId)")
            .And.Contain("this.storeKey + ':' + (customerId || 'guest')")
            .And.Contain("switchCustomer(storeKey: string, customerId: string | null)")
            .And.Contain("if (!token || !this.customerId) return []");
    }

    [Fact]
    public void CustomerTransitionsCannotReusePreviousAuthenticatedState()
    {
        var shell = Read("frontend/KhataDhari.Customer/src/app/store-shell.ts");
        shell.Should().Contain("this.cart.switchCustomer(customerId)")
            .And.Contain("this.wishlist.switchCustomer(key, customerId)");
        shell.Replace(" ", "").Replace("\r", "").Replace("\n", "")
            .Should().Contain("this.session.restore(storeKey);this.cart.useStore(storeKey)");
        var session = Read("frontend/KhataDhari.Customer/src/app/customer-session.service.ts");
        session.Should().Contain("this.customer.set(null)").And.Contain("this.active.set(false)");
        var auth = Read("frontend/KhataDhari.Customer/src/app/customer-auth.service.ts");
        auth.Should().Contain("this.wishlist.switchCustomer(storeKey,result.customer.id)");
    }
    [Fact]
    public void CustomerSessionRestorePreservesIdentityUntilValidationCompletes()
    {
        var session = Read("frontend/KhataDhari.Customer/src/app/customer-session.service.ts");
        session.Should().Contain("readonly customerId=signal<string|null>(null)")
            .And.Contain("readonly restoring=signal(false)")
            .And.Contain("readonly restoreError=signal(false)")
            .And.Contain("this.active.set(true);this.restoring.set(true)")
            .And.Contain("this.customerId.set(mapped.id)")
            .And.Contain("error.status===401")
            .And.Contain("this.clear(normalized)")
            .And.Contain("this.restoreError.set(true)")
            .And.NotContain("else this.clear");

        var guard = Read("frontend/KhataDhari.Customer/src/app/customer-auth.guard.ts");
        guard.Should().Contain("await session.restore(storeKey)")
            .And.Contain("session.isAuthenticated(storeKey)");

        var profile = Read("frontend/KhataDhari.Customer/src/app/pages/account-profile.page.ts")
            + Read("frontend/KhataDhari.Customer/src/app/pages/account-profile.page.html");
        profile.Should().Contain("await this.session.restore(this.storeKey)")
            .And.Contain("session.restoring()")
            .And.Contain("session.restoreError()");

        var shell = Read("frontend/KhataDhari.Customer/src/app/store-shell.ts");
        shell.Should().Contain("const customerId = this.session.customerId()")
            .And.Contain("this.cart.switchCustomer(customerId)")
            .And.Contain("this.wishlist.switchCustomer(key, customerId)");
        shell.Replace(" ", "").Replace("\r", "").Replace("\n", "")
            .Should().Contain("this.session.restore(storeKey);this.cart.useStore(storeKey)");
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

    [Fact]
    public void ProfileMediaBinaryColumnsUseTypedVarbinaryParameters()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerService.cs");
        service.Should().Contain("command.Parameters.Add(\"@data\",SqlDbType.VarBinary,-1)")
            .And.Contain("command.Parameters.Add(\"@thumbdata\",SqlDbType.VarBinary,-1)")
            .And.NotContain("command.Parameters.AddWithValue(\"@data\"")
            .And.NotContain("command.Parameters.AddWithValue(\"@thumbdata\"");
    }

    [Fact]
    public void StorefrontActivityRecordsProductAndKeepsAnalyticsTenantScoped()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontActivityService.cs");
        service.Should().Contain("ProductId,MetadataJson,VisitorSessionId")
            .And.Contain("INSERT commerce.AnalyticsEvents(TenantId,EventType,CustomerId,ProductId,MetadataJson,VisitorSessionId)")
            .And.NotContain("VALUES(NEWSEQUENTIALID(),@tenant")
            .And.Contain("WHERE TenantId=@tenant AND CreatedOn>=@from AND CreatedOn<@to")
            .And.Contain("Anonymous visitor")
            .And.Contain("FirstSignIn");
        var activity = Read("frontend/KhataDhari.Customer/src/app/storefront-activity.service.ts");
        activity.Should().Contain("sessionStorage")
            .And.Contain("STORE_VISIT")
            .And.Contain("PRODUCT_VIEW")
            .And.Contain("lastSearch")
            .And.Contain("queued")
            .And.Contain("hasConsent(storeKey)");
        Read("backend/src/WhatsBiz.Api/Controllers/StoreController.cs")
            .Should().Contain("[HttpPost(\"activity\")]")
            .And.Contain("activity.RecordAsync");
        Read("frontend/KhataDhari.Customer/src/app/cart/cart.service.ts")
            .Should().Contain("ADD_TO_CART").And.Contain("REMOVE_FROM_CART");
        Read("frontend/KhataDhari.Customer/src/app/pages/cart.page.ts")
            .Should().Contain("CHECKOUT_STARTED").And.Contain("ORDER_PLACED");
        Read("frontend/KhataDhari.Customer/src/app/customer-auth.service.ts")
            .Should().Contain("CUSTOMER_SIGN_IN");
    }

    [Fact]
    public void ProductDetailsHidesEmptyRichTextUsesOneReturnLabelAndSeparatesGalleryThumbnails()
    {
        var page = Read("frontend/KhataDhari.Customer/src/app/pages/product-details.page.ts");
        page.Should().Contain("hasMeaningfulHtml")
            .And.Contain("returnPolicyLabel")
            .And.Contain("gallery-stage")
            .And.Contain("aspect-ratio:var(--gallery-ratio,1)")
            .And.Contain(".gallery-thumbnails{flex-direction:row")
            .And.NotContain("{{item.returnWindowDays}}-day return policy");
    }

    [Fact]
    public void StorefrontActivityReportValidatesAndLogsFailuresWithoutFabricatingZeros()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/StorefrontAnalyticsController.cs");
        controller.Should().Contain("end <= start").And.Contain("StatusCodes.Status500InternalServerError")
            .And.Contain("ReportFailure");
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontActivityService.cs");
        service.Should().Contain("CONVERT(int,COALESCE(SUM(CASE WHEN e.EventType=N'PRODUCT_VIEW'")
            .And.Contain("Storefront activity report could not be loaded.")
            .And.Contain("ReportSqlFailure");
    }

    [Fact]
    public void CustomerSearchTrackingUsesSchemaDefaultsAndTenantScopedWrites()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/CustomerDemandService.cs");
        service.Should().Contain("INSERT commerce.ProductSearchEvents(SearchEventId,TenantId,CustomerId,AnonymousSessionId")
            .And.Contain("var searchEventId = Guid.NewGuid()")
            .And.Contain("insert.Parameters.AddWithValue(\"@id\", searchEventId)")
            .And.NotContain("NEWSEQUENTIALID()");
        Read("frontend/KhataDhari.Customer/src/app/storefront-activity.service.ts")
            .Should().Contain("/search-events").And.Contain("recordDemandSearch");
    }

    [Fact]
    public void StorefrontCustomersUseExistingIdentityAndTenantScopedStorefrontRecords()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/StorefrontCustomersController.cs");
        controller.Should().Contain("RequireFeature(FeatureKeys.CustomerDemand)")
            .And.Contain("Permissions.Customer.View")
            .And.Contain("currentUser.TenantId");
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerManagementService.cs");
        service.Should().Contain("c.TenantId=@tenant")
            .And.Contain("CreatedBy IN(N'STOREFRONT',N'STOREFRONT_OTP')")
            .And.Contain("SourceChannel=N'STOREFRONT'")
            .And.Contain("CustomerId=@customer");
        var routes = Read("frontend/WhatsBiz.Web/src/app/app.routes.ts");
        routes.Should().Contain("path: 'storefront/customers'").And.Contain("feature: 'CUSTOMER_DEMAND'");
        Read("frontend/WhatsBiz.Web/src/app/shared/layout/sidebar/sidebar.component.ts")
            .Should().Contain("Storefront Customers").And.Contain("/storefront/customers");
        Read("frontend/WhatsBiz.Web/src/app/app.routes.ts")
            .Should().Contain("path: 'storefront/search-analytics'").And.Contain("feature: 'CUSTOMER_DEMAND'");
        Read("frontend/WhatsBiz.Web/src/app/shared/layout/sidebar/sidebar.component.ts")
            .Should().Contain("label: 'Search Analytics'").And.Contain("feature: 'CUSTOMER_DEMAND'");
    }

    [Fact]
    public void StorefrontNavigationKeepsRequiredEntriesInOneGuardedOrder()
    {
        var sidebar = Read("frontend/WhatsBiz.Web/src/app/shared/layout/sidebar/sidebar.component.ts");
        var entries = new[] { "Storefront Dashboard", "Storefront Customers", "Visitors & Customer Activity", "Storefront Orders", "Customer Demand" };
        var positions = entries.Select(entry => sidebar.IndexOf($"label: '{entry}'", StringComparison.Ordinal)).ToArray();
        positions.Should().OnlyContain(position => position >= 0);
        positions.Should().BeInAscendingOrder();
        sidebar.Should().Contain("label: 'Storefront',").And.Contain("route: '/dashboard'").And.Contain("route: '/storefront/activity'");
        sidebar.Should().NotContain("label: 'Storefront activity'").And.NotContain("label: 'Storefront Orders', icon: 'shopping_cart', route: '/orders/storefront', permission: 'pos.view' },\n  {\n    label: 'Application Owner'");
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "backend", "WhatsBiz.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
