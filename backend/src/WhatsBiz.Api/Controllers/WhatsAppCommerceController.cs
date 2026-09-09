using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Application.Features.Delivery;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/whatsapp-commerce")]
public sealed partial class WhatsAppCommerceController(IWhatsAppCommerceService service, ICommerceAnalyticsService analytics,
    ICurrentUserService currentUser, IDeliveryService delivery, IFeatureService features,
    ILogger<WhatsAppCommerceController> logger) : ControllerBase
{
    [HttpGet("demo/setup"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceProductSearch), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public Task<WhatsAppCommerceSetup> SetupForRetailer([FromQuery] Guid? warehouseId, CancellationToken token) => service.GetSetupAsync(TenantId(), warehouseId, token);
    [HttpPost("demo/cart"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceProductSearch), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public Task<WhatsAppCommerceCart> CartForRetailer(CalculateWhatsAppCartInput input, CancellationToken token) => service.CalculateCartAsync(TenantId(), input.WarehouseId, input.Items, token);
    [HttpPost("demo/orders"), HasPermission(Permissions.POS.Create), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceOrders), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public Task<WhatsAppCommerceOrderResult> OrderForRetailer(PlaceWhatsAppDemoOrderInput input, CancellationToken token) => PlaceOrder(TenantId(), input, token);
    [HttpGet("demo/readiness"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.WebhookDiagnostics), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public Task<WhatsAppCommerceReadiness> ReadinessForRetailer(CancellationToken token) => service.GetReadinessAsync(TenantId(), token);
    [HttpGet("demo/orders"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> OrdersForRetailer([FromQuery] Guid customerId, CancellationToken token) => service.GetOrdersAsync(TenantId(), customerId, token);
    [HttpGet("demo/orders/{orderId:guid}"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<WhatsAppCommerceOrderDetails> OrderForRetailer(Guid orderId, [FromQuery] Guid customerId, CancellationToken token) => service.GetOrderAsync(TenantId(), customerId, orderId, token);
    [HttpPut("demo/orders/{orderId:guid}/delivery"), HasPermission(Permissions.POS.Edit), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<WhatsAppCommerceOrderSummary> DeliveryForRetailer(Guid orderId, UpdateWhatsAppCommerceDeliveryInput input, CancellationToken token) => service.UpdateDeliveryAsync(TenantId(), orderId, input, token);
    [HttpPost("demo/status-notifications"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> NotificationsForRetailer([FromQuery] Guid customerId, CancellationToken token) => service.GetStatusNotificationsAsync(TenantId(), customerId, token);
    [HttpPost("analytics"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceAnalytics)]
    public async Task<IActionResult> AnalyticsForRetailer(CommerceAnalyticsEventInput input, CancellationToken token)
    { await analytics.RecordAsync(TenantId(), input, token); return NoContent(); }

    [HttpGet("administration/tenants/{tenantId:guid}/demo/setup"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppCommerceSetup> Setup(Guid tenantId, [FromQuery] Guid? warehouseId, CancellationToken token) => await service.GetSetupAsync(await TargetTenant(tenantId, token), warehouseId, token);
    [HttpPost("administration/tenants/{tenantId:guid}/demo/cart"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppCommerceCart> Cart(Guid tenantId, CalculateWhatsAppCartInput input, CancellationToken token) => await service.CalculateCartAsync(await TargetTenant(tenantId, token), input.WarehouseId, input.Items, token);
    [HttpPost("administration/tenants/{tenantId:guid}/demo/orders"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppCommerceOrderResult> Order(Guid tenantId, PlaceWhatsAppDemoOrderInput input, CancellationToken token)
    {
        tenantId = await TargetTenant(tenantId, token);
        return await PlaceOrder(tenantId, input, token);
    }

    private async Task<WhatsAppCommerceOrderResult> PlaceOrder(Guid tenantId, PlaceWhatsAppDemoOrderInput input, CancellationToken token)
    {
        var result = await service.PlaceOrderAsync(tenantId, input, currentUser.Username, token);

        // The ERP order is already committed at this point. Delivery registration is a
        // follow-up integration and must not turn a successful checkout into an error.
        if (!input.FulfillmentMethod.Equals("WALK_IN", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (await features.IsEnabledAsync(tenantId, FeatureKeys.DeliveryManagement, token))
                {
                    await delivery.Ready(tenantId, result.OrderId,
                        new(DeliveryAddress: input.DeliveryAddress,
                            CodRequired: input.PaymentType.Equals("COD", StringComparison.OrdinalIgnoreCase)),
                        currentUser.UserId ?? throw new UnauthorizedAccessException("A user identity is required."),
                        currentUser.Username ?? "WhatsApp Commerce", token);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDeliveryRegistrationFailure(logger, exception, result.OrderId, tenantId);
            }
        }

        return result;
    }
    [HttpGet("administration/tenants/{tenantId:guid}/demo/readiness"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppCommerceReadiness> Readiness(Guid tenantId, CancellationToken token) => await service.GetReadinessAsync(await TargetTenant(tenantId, token), token);
    [HttpGet("administration/tenants/{tenantId:guid}/demo/orders"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> Orders(Guid tenantId, [FromQuery] Guid customerId, CancellationToken token) => await service.GetOrdersAsync(await TargetTenant(tenantId, token), customerId, token);
    [HttpGet("delivery-orders"), HasPermission(Permissions.POS.Edit), RequireFeature(FeatureKeys.WhatsAppCommerce), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> DeliveryOrders([FromQuery(Name = "from")] DateTimeOffset? fromDate, [FromQuery(Name = "to")] DateTimeOffset? toDate, [FromQuery] string? deliveryStatus, [FromQuery] string? trackingNumber, CancellationToken token) => service.GetDeliveryOrdersAsync(TenantId(), fromDate, toDate, deliveryStatus, trackingNumber, token);
    [HttpGet("administration/tenants/{tenantId:guid}/demo/orders/{orderId:guid}"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppCommerceOrderDetails> Order(Guid tenantId, Guid orderId, [FromQuery] Guid customerId, CancellationToken token) => await service.GetOrderAsync(await TargetTenant(tenantId, token), customerId, orderId, token);
    [HttpPut("administration/tenants/{tenantId:guid}/demo/orders/{orderId:guid}/delivery"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<WhatsAppCommerceOrderSummary> Delivery(Guid tenantId, Guid orderId, UpdateWhatsAppCommerceDeliveryInput input, CancellationToken token) => await service.UpdateDeliveryAsync(await TargetTenant(tenantId, token), orderId, input, token);
    [HttpPost("administration/tenants/{tenantId:guid}/demo/status-notifications"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<IReadOnlyCollection<WhatsAppCommerceMessage>> Notifications(Guid tenantId, [FromQuery] Guid customerId, CancellationToken token) => await service.GetStatusNotificationsAsync(await TargetTenant(tenantId, token), customerId, token);
    [HttpPost("administration/tenants/{tenantId:guid}/analytics"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<IActionResult> Analytics(Guid tenantId, CommerceAnalyticsEventInput input, CancellationToken token)
    { await analytics.RecordAsync(await TargetTenant(tenantId, token), input, token); return NoContent(); }
    private Guid TenantId() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
    private async Task<Guid> TargetTenant(Guid tenantId, CancellationToken token)
    {
        await features.GetTenantConfigurationAsync(tenantId, token);
        HttpContext.Items[AuditMiddleware.TargetTenantItemKey] = tenantId;
        return tenantId;
    }

    [LoggerMessage(2301, LogLevel.Error,
        "WhatsApp MOCK order {OrderId} was created, but delivery registration failed for tenant {TenantId}.")]
    private static partial void LogDeliveryRegistrationFailure(ILogger logger, Exception exception,
        Guid orderId, Guid tenantId);
}
public sealed record CalculateWhatsAppCartInput(Guid WarehouseId, IReadOnlyCollection<WhatsAppCommerceCartItem> Items);
