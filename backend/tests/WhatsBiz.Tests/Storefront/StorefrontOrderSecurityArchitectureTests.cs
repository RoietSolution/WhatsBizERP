using FluentAssertions;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontOrderSecurityArchitectureTests
{
    private static readonly string Root = FindRoot();
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void CustomerListAndDetailsRequireProtectedSessionCustomerAndStorefrontSource()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerService.cs");
        service.Should().Contain("Resolve(storeKey, sessionToken, token)");
        service.Should().Contain("i.TenantId=@tenant AND i.CustomerId=@customer");
        service.Should().Contain("w.SourceChannel=N'STOREFRONT'");
        service.Should().Contain("i.InvoiceId=@order");
    }

    [Fact]
    public void RetailerQueryUsesAuthenticatedTenantAndExistingDeliveryAndPaymentTables()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/StorefrontOrdersController.cs");
        controller.Should().Contain("HasPermission(Permissions.POS.View)");
        controller.Should().Contain("current.TenantId");
        controller.Should().Contain("i.TenantId=@tenant");
        controller.Should().Contain("commerce.OrderDeliveries");
        controller.Should().Contain("commerce.CommercePayments");
        controller.Should().Contain("w.SourceChannel=N'STOREFRONT'");
    }

    [Fact]
    public void PreparingDeliveryPreservesStorefrontSourceAndTenant()
    {
        var delivery = Read("backend/src/WhatsBiz.Infrastructure/Delivery/DeliveryService.cs");
        delivery.Should().Contain("ELSE w.SourceChannel END");
        delivery.Should().Contain("w.TenantId=i.TenantId");
        delivery.Should().Contain("i.InvoiceId=@order AND i.TenantId=@tenant");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "frontend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}