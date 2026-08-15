namespace WhatsBiz.Application.Features.WhatsAppCommerce;

public sealed record WhatsAppCommerceMessage(string Sender, string Kind, string Text);
public sealed record WhatsAppCommerceProduct(Guid ProductId, string ProductCode, string? Barcode,
    string ProductName, string? Description, string? ImageUrl, decimal SellingPrice,
    decimal TaxPercentage, decimal AvailableQuantity);
public sealed record WhatsAppCommerceCustomer(Guid CustomerId, string CustomerCode, string CustomerName, string? Mobile);
public sealed record WhatsAppCommerceWarehouse(Guid WarehouseId, string WarehouseCode, string WarehouseName);
public sealed record WhatsAppCommerceSetup(string ProviderMode, string StoreName,
    IReadOnlyCollection<WhatsAppCommerceCustomer> Customers,
    IReadOnlyCollection<WhatsAppCommerceWarehouse> Warehouses,
    IReadOnlyCollection<WhatsAppCommerceProduct> Products,
    IReadOnlyCollection<WhatsAppCommerceMessage> Messages);
public sealed record WhatsAppCommerceCartItem(Guid ProductId, decimal Quantity);
public sealed record WhatsAppCommerceCartLine(Guid ProductId, string ProductCode, string ProductName,
    string? ImageUrl, decimal Quantity, decimal UnitPrice, decimal TaxPercentage,
    decimal TaxAmount, decimal LineTotal, decimal AvailableQuantity);
public sealed record WhatsAppCommerceCart(Guid WarehouseId, IReadOnlyCollection<WhatsAppCommerceCartLine> Items,
    decimal Subtotal, decimal TaxAmount, decimal GrandTotal);
public sealed record PlaceWhatsAppDemoOrderInput(Guid CustomerId, Guid WarehouseId,
    IReadOnlyCollection<WhatsAppCommerceCartItem> Items);
public sealed record WhatsAppCommerceOrderResult(Guid OrderId, string OrderNumber, string ErpStatus,
    decimal GrandTotal, IReadOnlyCollection<WhatsAppCommerceMessage> Messages);
public sealed record WhatsAppCommerceReadinessCheck(string Key, string Label, bool Ready, string? SetupRoute, string? Detail);
public sealed record WhatsAppCommerceReadiness(bool Ready, IReadOnlyCollection<WhatsAppCommerceReadinessCheck> Checks);
public sealed record WhatsAppCommerceOrderSummary(Guid OrderId, string OrderNumber, DateTimeOffset OrderDate,
    decimal GrandTotal, string ErpStatus, string DisplayStatus, string SourceChannel, string ProviderMode);
public sealed record WhatsAppCommerceOrderDetails(WhatsAppCommerceOrderSummary Order,
    IReadOnlyCollection<WhatsAppCommerceCartLine> Items);

public interface IWhatsAppCommerceProvider
{
    string Mode { get; }
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendWelcomeAsync(string storeName, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderConfirmationAsync(string orderNumber, decimal amount, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderStatusAsync(string orderNumber, string status, CancellationToken token);
}
public interface IWhatsAppCommerceProviderResolver { IWhatsAppCommerceProvider Resolve(string mode); }
public interface IWhatsAppCommerceService
{
    Task<WhatsAppCommerceSetup> GetSetupAsync(Guid tenantId, Guid? warehouseId, CancellationToken token);
    Task<WhatsAppCommerceCart> CalculateCartAsync(Guid tenantId, Guid warehouseId, IReadOnlyCollection<WhatsAppCommerceCartItem> items, CancellationToken token);
    Task<WhatsAppCommerceOrderResult> PlaceOrderAsync(Guid tenantId, PlaceWhatsAppDemoOrderInput input, string? actor, CancellationToken token);
    Task<WhatsAppCommerceReadiness> GetReadinessAsync(Guid tenantId, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> GetOrdersAsync(Guid tenantId, Guid customerId, CancellationToken token);
    Task<WhatsAppCommerceOrderDetails> GetOrderAsync(Guid tenantId, Guid customerId, Guid orderId, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> GetStatusNotificationsAsync(Guid tenantId, Guid customerId, CancellationToken token);
}
