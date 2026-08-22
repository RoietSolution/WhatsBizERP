using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/whatsapp-commerce"), RequireFeature(FeatureKeys.WhatsAppCommerce)]
public sealed class WhatsAppCommerceController(IWhatsAppCommerceService service, ICommerceAnalyticsService analytics, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("demo/setup"), HasPermission(Permissions.POS.View)]
    public Task<WhatsAppCommerceSetup> Setup([FromQuery] Guid? warehouseId, CancellationToken token) => service.GetSetupAsync(TenantId(), warehouseId, token);
    [HttpPost("demo/cart"), HasPermission(Permissions.POS.View)]
    public Task<WhatsAppCommerceCart> Cart(CalculateWhatsAppCartInput input, CancellationToken token) => service.CalculateCartAsync(TenantId(), input.WarehouseId, input.Items, token);
    [HttpPost("demo/orders"), HasPermission(Permissions.POS.Create)]
    public Task<WhatsAppCommerceOrderResult> Order(PlaceWhatsAppDemoOrderInput input, CancellationToken token) => service.PlaceOrderAsync(TenantId(), input, currentUser.Username, token);
    [HttpGet("demo/readiness"), HasPermission(Permissions.POS.View)]
    public Task<WhatsAppCommerceReadiness> Readiness(CancellationToken token) => service.GetReadinessAsync(TenantId(), token);
    [HttpGet("demo/orders"), HasPermission(Permissions.POS.View)]
    public Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> Orders([FromQuery] Guid customerId, CancellationToken token) => service.GetOrdersAsync(TenantId(), customerId, token);
    [HttpGet("delivery-orders"), HasPermission(Permissions.POS.Edit)]
    public Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> DeliveryOrders([FromQuery(Name = "from")] DateTimeOffset? fromDate, [FromQuery(Name = "to")] DateTimeOffset? toDate, [FromQuery] string? deliveryStatus, [FromQuery] string? trackingNumber, CancellationToken token) => service.GetDeliveryOrdersAsync(TenantId(), fromDate, toDate, deliveryStatus, trackingNumber, token);
    [HttpGet("demo/orders/{orderId:guid}"), HasPermission(Permissions.POS.View)]
    public Task<WhatsAppCommerceOrderDetails> Order(Guid orderId, [FromQuery] Guid customerId, CancellationToken token) => service.GetOrderAsync(TenantId(), customerId, orderId, token);
    [HttpPut("demo/orders/{orderId:guid}/delivery"), HasPermission(Permissions.POS.Edit)]
    public Task<WhatsAppCommerceOrderSummary> Delivery(Guid orderId, UpdateWhatsAppCommerceDeliveryInput input, CancellationToken token) => service.UpdateDeliveryAsync(TenantId(), orderId, input, token);
    [HttpPost("demo/status-notifications"), HasPermission(Permissions.POS.View)]
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> Notifications([FromQuery] Guid customerId, CancellationToken token) => service.GetStatusNotificationsAsync(TenantId(), customerId, token);
    [HttpPost("analytics"), HasPermission(Permissions.POS.View)]
    public async Task<IActionResult> Analytics(CommerceAnalyticsEventInput input, CancellationToken token)
    { await analytics.RecordAsync(TenantId(), input, token); return NoContent(); }
    private Guid TenantId() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
}
public sealed record CalculateWhatsAppCartInput(Guid WarehouseId, IReadOnlyCollection<WhatsAppCommerceCartItem> Items);
