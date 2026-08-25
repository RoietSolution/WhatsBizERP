using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Application.Features.Delivery;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/whatsapp-commerce"), RequireFeature(FeatureKeys.WhatsAppCommerce)]
public sealed class WhatsAppCommerceController(IWhatsAppCommerceService service, ICommerceAnalyticsService analytics, ICurrentUserService currentUser,IDeliveryService delivery,IFeatureService features) : ControllerBase
{
    [HttpGet("demo/setup"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.CommerceProductSearch), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public Task<WhatsAppCommerceSetup> Setup([FromQuery] Guid? warehouseId, CancellationToken token) => service.GetSetupAsync(TenantId(), warehouseId, token);
    [HttpPost("demo/cart"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.CommerceProductSearch), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public Task<WhatsAppCommerceCart> Cart(CalculateWhatsAppCartInput input, CancellationToken token) => service.CalculateCartAsync(TenantId(), input.WarehouseId, input.Items, token);
    [HttpPost("demo/orders"), HasPermission(Permissions.POS.Create), RequireFeature(FeatureKeys.CommerceOrders), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public async Task<WhatsAppCommerceOrderResult> Order(PlaceWhatsAppDemoOrderInput input, CancellationToken token)
    { var result=await service.PlaceOrderAsync(TenantId(),input,currentUser.Username,token);if(!input.FulfillmentMethod.Equals("WALK_IN",StringComparison.OrdinalIgnoreCase)&&await features.IsEnabledAsync(TenantId(),FeatureKeys.DeliveryManagement,token))await delivery.Ready(TenantId(),result.OrderId,new(DeliveryAddress:input.DeliveryAddress,CodRequired:input.PaymentType.Equals("COD",StringComparison.OrdinalIgnoreCase)),currentUser.UserId??throw new UnauthorizedAccessException("A user identity is required."),currentUser.Username??"WhatsApp Commerce",token);return result; }
    [HttpGet("demo/readiness"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.WebhookDiagnostics), RequireFeature(FeatureKeys.WhatsAppCommerceDemo)]
    public Task<WhatsAppCommerceReadiness> Readiness(CancellationToken token) => service.GetReadinessAsync(TenantId(), token);
    [HttpGet("demo/orders"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> Orders([FromQuery] Guid customerId, CancellationToken token) => service.GetOrdersAsync(TenantId(), customerId, token);
    [HttpGet("delivery-orders"), HasPermission(Permissions.POS.Edit), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> DeliveryOrders([FromQuery(Name = "from")] DateTimeOffset? fromDate, [FromQuery(Name = "to")] DateTimeOffset? toDate, [FromQuery] string? deliveryStatus, [FromQuery] string? trackingNumber, CancellationToken token) => service.GetDeliveryOrdersAsync(TenantId(), fromDate, toDate, deliveryStatus, trackingNumber, token);
    [HttpGet("demo/orders/{orderId:guid}"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<WhatsAppCommerceOrderDetails> Order(Guid orderId, [FromQuery] Guid customerId, CancellationToken token) => service.GetOrderAsync(TenantId(), customerId, orderId, token);
    [HttpPut("demo/orders/{orderId:guid}/delivery"), HasPermission(Permissions.POS.Edit), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<WhatsAppCommerceOrderSummary> Delivery(Guid orderId, UpdateWhatsAppCommerceDeliveryInput input, CancellationToken token) => service.UpdateDeliveryAsync(TenantId(), orderId, input, token);
    [HttpPost("demo/status-notifications"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.CommerceOrders)]
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> Notifications([FromQuery] Guid customerId, CancellationToken token) => service.GetStatusNotificationsAsync(TenantId(), customerId, token);
    [HttpPost("analytics"), HasPermission(Permissions.POS.View), RequireFeature(FeatureKeys.CommerceAnalytics)]
    public async Task<IActionResult> Analytics(CommerceAnalyticsEventInput input, CancellationToken token)
    { await analytics.RecordAsync(TenantId(), input, token); return NoContent(); }
    private Guid TenantId() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
}
public sealed record CalculateWhatsAppCartInput(Guid WarehouseId, IReadOnlyCollection<WhatsAppCommerceCartItem> Items);
