using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Controllers;

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

    [Fact]
    public void ShippingLabelUsesTenantScopedOrderSnapshotAndNeverReadsCurrentAddress()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/StorefrontOrdersController.cs");
        controller.Should().Contain("{orderId:guid}/shipping-label")
            .And.Contain("d.DeliveryAddressSnapshot")
            .And.Contain("w.DeliveryAddress")
            .And.Contain("i.ServicePincode")
            .And.Contain("i.TenantId=@tenant AND i.InvoiceId=@order")
            .And.NotContain("CustomerAddresses");
    }

    [Fact]
    public void ShippingLabelPreviewSupportsPaperAndThermalFormatsWithoutProductPrices()
    {
        var models = Read("frontend/WhatsBiz.Web/src/app/features/delivery/shipping-label.models.ts");
        var preview = Read("frontend/WhatsBiz.Web/src/app/features/delivery/shipping-label-preview.component.ts");
        models.Should().Contain("'A5'").And.Contain("'A4_SINGLE'").And.Contain("'A4_DOUBLE'").And.Contain("'THERMAL_80'").And.Contain("'THERMAL_58'");
        preview.Should().Contain("window.print()").And.Contain("shippingLabelPaymentIndicator").And.Contain("148mm").And.Contain("72mm").And.Contain("50mm");
        models.Should().Contain("COD PAID").And.Contain("COD TO COLLECT").And.Contain("PREPAID").And.Contain("Payment pending");
        preview.Should().NotContain("itemCount").And.NotContain("sellingPrice").And.NotContain("GrandTotal");
    }

    [Fact]
    public void StorefrontOrdersExposeIndividualAndBulkLabelActionsWithCancelledOrderProtection()
    {
        var orders = Read("frontend/WhatsBiz.Web/src/app/features/delivery/storefront-orders.component.ts");
        orders.Should().Contain("Print shipping label")
            .And.Contain("selectAll()")
            .And.Contain("clearSelection()")
            .And.Contain("slice(0,50)")
            .And.Contain("status!=='CANCELLED'")
            .And.Contain("window.confirm");
    }

    [Fact]
    public void ShippingLabelPrintingKeepsTenantAndRejectsCancelledOrUnknownOrders()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/StorefrontOrdersController.cs");
        controller.Should().Contain("i.TenantId=@tenant AND i.InvoiceId=@order")
            .And.Contain("w.SourceChannel=N'STOREFRONT'")
            .And.Contain("i.Status");
    }

    [Fact]
    public void ProfessionalLabelUsesOrderIdentifierCodesAndOptionalPackingData()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/StorefrontOrdersController.cs");
        var preview = Read("frontend/WhatsBiz.Web/src/app/features/delivery/professional-shipping-label-preview.component.ts");
        controller.Should().Contain("var codeValue = label.OrderNumber")
            .And.Contain("printing.QrCode")
            .And.Contain("printing.Barcode")
            .And.Contain("sales.SalesInvoiceItems")
            .And.Contain("ShowProductDetailsOnShippingLabel");
        preview.Should().Contain("COURIER_4X6")
            .And.Contain("PACKING_SLIP")
            .And.Contain("4in")
            .And.Contain("148mm")
            .And.Contain("72mm")
            .And.Contain("50mm")
            .And.Contain("page-break-inside:avoid")
            .And.NotContain("AWB-");
    }

    [Fact]
    public void CustomerDemandRoutesUseTheControllerStoreKeyExactlyOnce()
    {
        var controller = Read("backend/src/WhatsBiz.Api/Controllers/StoreController.cs");
        controller.Should().Contain("[HttpPost(\"search-events\")]").And.Contain("[HttpPost(\"products/{productId:guid}/availability-request\")]")
            .And.Contain("[HttpDelete(\"products/{productId:guid}/availability-request\")]").And.Contain("[HttpPost(\"missing-product-requests\")]")
            .And.NotContain("[HttpPost(\"{storeKey}/search-events\")]").And.NotContain("[HttpPost(\"{storeKey}/missing-product-requests\")]");
    }

    [Fact]
    public void CustomerDemandActionMetadataMatchesFrontendPaths()
    {
        ((HttpPostAttribute)typeof(StoreController).GetMethod("SearchEvent")!.GetCustomAttributes(typeof(HttpPostAttribute), false).Single()).Template.Should().Be("search-events");
        ((HttpPostAttribute)typeof(StoreController).GetMethod("Availability")!.GetCustomAttributes(typeof(HttpPostAttribute), false).Single()).Template.Should().Be("products/{productId:guid}/availability-request");
        ((HttpDeleteAttribute)typeof(StoreController).GetMethod("CancelAvailability")!.GetCustomAttributes(typeof(HttpDeleteAttribute), false).Single()).Template.Should().Be("products/{productId:guid}/availability-request");
        ((HttpPostAttribute)typeof(StoreController).GetMethod("MissingProduct")!.GetCustomAttributes(typeof(HttpPostAttribute), false).Single()).Template.Should().Be("missing-product-requests");
    }

    [Fact]
    public void CustomerReturnsUseTenantScopedRequestStorageAndDoNotMutateStockOnRequest()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontReturnService.cs");
        service.Should().Contain("r.TenantId=@tenant").And.Contain("i.CustomerId=@customer").And.Contain("Status IN(N'REQUESTED',N'APPROVED',N'RECEIVED')").And.NotContain("POS_ReturnInvoice");
        Read("database/migrations/V54-CustomerReturnRequests.sql").Should().Contain("UX_StorefrontReturnRequests_ActiveItem");
    }

    [Fact]
    public void CustomerReturnInsertNormalizesLegacyDeliveryTimestampAndMapsKnownConflicts()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontReturnService.cs");
        service.Should().Contain("CONVERT(datetimeoffset(7),d.DeliveredAt)")
            .And.Contain("exception.Number is 2601 or 2627")
            .And.Contain("The order item is no longer available for a return request.");
    }

    [Fact]
    public void CustomerReturnEmptyHistoryTreatsNullablePendingAggregateAsNoPendingRequest()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontReturnService.cs");
        service.Should().Contain("COALESCE(MAX(CASE WHEN Status IN(N'REQUESTED',N'APPROVED',N'RECEIVED') THEN 1 ELSE 0 END),0)")
            .And.Contain("!er.IsDBNull(1)&&er.GetInt32(1)==1");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "frontend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
