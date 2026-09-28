using FluentAssertions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontCancellationPolicyTests
{
    [Theory]
    [InlineData(990, 990, 0, 990)]
    [InlineData(990, 500, 0, 500)]
    [InlineData(990, 990, 400, 590)]
    [InlineData(440, 0, 0, 0)]
    [InlineData(500, 500, 0, 500)]
    [InlineData(990, 990, 990, 0)]
    public void AvailableRefundUsesCommittedTotalAndActualCollection(
        decimal committed, decimal collected, decimal reserved, decimal expected)
        => StorefrontCancellationPolicy.AvailableRefund(committed, collected, reserved).Should().Be(expected);

    [Fact]
    public void UnpaidHeldCodCanCancelWithoutFinancialReversal()
        => StorefrontCancellationPolicy.CanCancelWithoutFinancialReversal(
            "HELD", 0, 0, "COD", null, false, false).Should().BeTrue();

    [Theory]
    [InlineData("COMPLETED", 0, 0, "COD", null, false, false)]
    [InlineData("HELD", 440, 440, "DIRECT_UPI", null, false, false)]
    [InlineData("HELD", 990, 990, "RAZORPAY", null, false, false)]
    [InlineData("HELD", 0, 0, "COD", "DELIVERED", false, false)]
    [InlineData("HELD", 0, 0, "COD", null, true, false)]
    [InlineData("HELD", 0, 0, "COD", null, false, true)]
    public void FinanciallyUnsafeOrDeliveredOrdersCannotUseCodCancellation(
        string status, decimal paid, decimal collected, string provider,
        string? delivery, bool hasReturns, bool pending)
        => StorefrontCancellationPolicy.CanCancelWithoutFinancialReversal(
            status, paid, collected, provider, delivery, hasReturns, pending).Should().BeFalse();

    [Fact]
    public void CustomerAndRetailerEndpointsPreserveAuthorityBoundary()
    {
        var root = FindRoot();
        var customer = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Api/Controllers/StoreController.cs"));
        var retailer = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Api/Controllers/StorefrontOrdersController.cs"));
        var service = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCancellationService.cs"));
        customer.Should().Contain("CustomerSessionToken()");
        retailer.Should().Contain("HasPermission(Permissions.POS.Void)");
        service.Should().Contain("i.TenantId=@tenant AND i.InvoiceId=@invoice AND (@customer IS NULL OR i.CustomerId=@customer)");
        service.Should().Contain("SourceChannel=N'STOREFRONT'");
        service.Should().Contain("sales.POS_TransitionHeldInvoice");
        service.Should().NotContain("RefundPaymentAsync(");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "frontend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}