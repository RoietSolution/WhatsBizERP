using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class OrderIntakeArchitectureTests
{
    private static string Read(string path) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../..", path)));

    [Fact]
    public void IntakeStorageIsTenantScopedAndProviderRetryIsIdempotent()
    {
        var migration = Read("database/migrations/V79-MultiChannelOrderIntake.sql");
        migration.Should().Contain("FK_OrderIntakeRequests_Tenant")
            .And.Contain("UX_OrderIntakeRequests_ProviderMessage")
            .And.Contain("UNIQUE INDEX UX_OrderIntakeRequests_ProviderMessage ON commerce.OrderIntakeRequests(TenantId,ProviderMessageId)")
            .And.Contain("FK_OrderIntakeItems_Request")
            .And.Contain("FK_OrderIntakeEvents_Request");
        var service = Read("backend/src/WhatsBiz.Infrastructure/OrderIntake/OrderIntakeService.cs");
        service.Should().Contain("WHERE r.TenantId=@tenant")
            .And.Contain("WHERE TenantId=@tenant AND OrderIntakeRequestId=@id")
            .And.Contain("IsolationLevel.Serializable")
            .And.Contain("ex.Number is 2601 or 2627");
    }

    [Fact]
    public void IntakeFeatureIsServerEnforcedAndWhatsAppDoesNotStealCommerceActions()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/OrderIntakeController.cs");
        controller.Should().Contain("RequireFeature(FeatureKeys.MultiChannelOrderIntake)")
            .And.Contain("current.TenantId")
            .And.Contain("Permissions.Customer.View");
        var webhook = Read("backend/src/WhatsBiz.Infrastructure/WhatsApp/WhatsAppService.cs");
        webhook.Should().Contain("intakeEnabled")
            .And.Contain("item.InteractiveActionId is null")
            .And.Contain("item.Products.Count == 0")
            .And.Contain("if (!capturedOrderRequest && commerceEnabled");
    }

    [Fact]
    public void ConfirmationAndConversionUseVersionedQuoteRoutesAndCannotBypassConfirmation()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/OrderIntake/OrderIntakeService.cs");
        service.Should().Contain("if (target == OrderIntakeStatuses.AwaitingCustomer) throw new InvalidOperationException")
            .And.Contain("if (target == OrderIntakeStatuses.Confirmed) throw new InvalidOperationException")
            .And.Contain("checkout.QuoteIntakeAsync")
            .And.Contain("input.QuoteRevision")
            .And.Contain("CreateQuoteAsync")
            .And.Contain("ConvertToSalesOrderAsync")
            .And.Contain("if (parts.Length > 100) throw new ArgumentException");
    }

    [Fact]
    public void V80CreatesTenantScopedNonPostingOrdersWithRevisionAndIdempotencyConstraints()
    {
        var migration = Read("database/migrations/V80-OrderIntakeQuotesAndSalesOrders.sql");
        migration.Should().Contain("UQ_OrderIntakeQuotes_Revision")
            .And.Contain("UQ_IntakeSalesOrders_Request")
            .And.Contain("FK_IntakeSalesOrders_Quote")
            .And.Contain("FK_OrderIntakeRequests_ConvertedSalesOrder")
            .And.Contain("PricingSnapshotJson")
            .And.Contain("Version rowversion");
        var service = Read("backend/src/WhatsBiz.Infrastructure/OrderIntake/OrderIntakeService.cs");
        service.Should().Contain("INSERT commerce.IntakeSalesOrders")
            .And.Contain("INSERT commerce.IntakeSalesOrderItems")
            .And.Contain("message was sent")
            .And.Contain("Completion remains gated")
            .And.NotContain("pos.PostForTenant")
            .And.NotContain("payments.CreateAttempt");
    }

    [Fact]
    public void QuoteCalculationReusesExistingStorefrontPricingAndRevalidatesStockAndSchedule()
    {
        var checkout = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCheckoutService.cs");
        checkout.Should().Contain("StorefrontPricingPolicy.Calculate")
            .And.Contain("availability.EnsureOrderEligibleAsync")
            .And.Contain("The selected products are not currently available together")
            .And.Contain("RejectPartnerSellerCheckout");
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/OrderIntakeController.cs");
        controller.Should().Contain("{id:guid}/quote")
            .And.Contain("{id:guid}/confirmation")
            .And.Contain("{id:guid}/sales-order");
    }

    [Fact]
    public void DeliveryAddressRequiresCompleteSnapshotAndV81KeepsItOnQuoteAndSalesOrder()
    {
        var migration = Read("database/migrations/V81-OrderIntakeDeliveryAddressSnapshots.sql");
        migration.Should().Contain("DeliveryAddressDraftJson")
            .And.Contain("OBJECT_ID(N'commerce.OrderIntakeQuotes',N'U') IS NULL")
            .And.Contain("DeliveryAddressSnapshotJson")
            .And.Contain("ISJSON(DeliveryAddressSnapshotJson)=1")
            .And.Contain("CK_OrderIntakeQuotes_DeliveryAddressRequired")
            .And.Contain("CK_IntakeSalesOrders_DeliveryAddressRequired");
        var snapshotColumn = migration.IndexOf("ADD DeliveryAddressSnapshotJson", StringComparison.Ordinal);
        migration.IndexOf("\nGO", snapshotColumn, StringComparison.Ordinal).Should().BeGreaterThan(snapshotColumn);
        var validator = Read("database/migrations/validators/V81-OrderIntakeDeliveryAddressSnapshots.validate.sql");
        validator.Should().Contain("is_not_trusted=0")
            .And.Contain("complete matching address snapshot")
            .And.Contain("$.RecipientName")
            .And.Contain("$.Pincode");
        var service = Read("backend/src/WhatsBiz.Infrastructure/OrderIntake/OrderIntakeService.cs");
        service.Should().Contain("SavedAddressId")
            .And.Contain("JOIN sales.CustomerAddresses a ON a.CustomerId=c.CustomerId")
            .And.Contain("WHERE r.TenantId=@tenant AND r.OrderIntakeRequestId=@request AND a.AddressId=@address")
            .And.Contain("quote.DeliveryAddress != detail.DeliveryAddress")
            .And.Contain("QuoteStatus='SUPERSEDED'")
            .And.Contain("checkout.QuoteIntakeAsync");
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/OrderIntakeController.cs");
        controller.Should().Contain("Permissions.Customer.View")
            .And.Contain("delivery-details")
            .And.Contain("saved-addresses");
    }
}
