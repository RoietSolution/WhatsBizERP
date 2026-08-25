namespace WhatsBiz.Application.Features.WhatsAppCommerce;
#pragma warning disable CA1711

public sealed record WhatsAppCommerceMessage(string Sender, string Kind, string Text);
public sealed record WhatsAppProviderConnectionRequest(string ApiVersion, string WhatsAppBusinessAccountId,
    string PhoneNumberId, string AccessToken);
public sealed record WhatsAppProviderConnectionResult(bool Succeeded, string? DisplayPhoneNumber,
    string? BusinessDisplayName, string? SafeMessage);
public sealed record WhatsAppProviderTestMessageRequest(string ApiVersion, string PhoneNumberId,
    string AccessToken, string RecipientNumber, string Message);
public sealed record WhatsAppProviderTestMessageResult(bool Succeeded, string? ProviderMessageId,
    DateTimeOffset AttemptedAt, string? SafeMessage);
public sealed record WhatsAppCommerceProductMessage(Guid ProductId, string ProductName, string ProductCode,
    decimal SellingPrice, string? ImageUrl, string? CatalogId, string? ExternalProductId);
public sealed record WhatsAppCommerceSendRequest(string ApiVersion, string PhoneNumberId, string AccessToken,
    string RecipientNumber, string Title, IReadOnlyCollection<WhatsAppCommerceProductMessage> Products,
    bool UseNativeProducts);
public sealed record WhatsAppCommerceSendResult(bool Succeeded, string? ProviderMessageId,
    DateTimeOffset AttemptedAt, bool NativeUsed, int ProductsSent, string Recipient, string? SafeMessage);
public sealed record WhatsAppTransactionalMessageRequest(string ApiVersion,string PhoneNumberId,string AccessToken,string RecipientNumber,string TemplateKey,string? ApprovedTemplateName,string LanguageCode,string Message,IReadOnlyCollection<string> Parameters);
public sealed record WhatsAppTransactionalMessageResult(bool Succeeded,string? ProviderMessageId,DateTimeOffset AttemptedAt,string? SafeMessage);
public sealed record SendCollectionInput(Guid CustomerId);
public sealed record WhatsAppCommerceProduct(Guid ProductId, string ProductCode, string? Barcode,
    string ProductName, string? Description, string? ImageUrl, decimal SellingPrice, decimal Mrp,
    decimal TaxPercentage, decimal AvailableQuantity, Guid CategoryId, string CategoryName,
    IReadOnlyCollection<string> ImageUrls);
public sealed record WhatsAppCommerceCategory(Guid CategoryId, string CategoryName, string? Description,
    int ProductCount, string? ImageProductId);
public sealed record WhatsAppCommerceCollection(Guid CollectionId, string Name, string Slug,
    IReadOnlyCollection<Guid> ProductIds);
public sealed record WhatsAppCommerceCustomer(Guid CustomerId, string CustomerCode, string CustomerName, string? Mobile);
public sealed record WhatsAppCommerceWarehouse(Guid WarehouseId, string WarehouseCode, string WarehouseName);
public sealed record WhatsAppCommerceSetup(string ProviderMode, string StoreName,
    IReadOnlyCollection<WhatsAppCommerceCustomer> Customers,
    IReadOnlyCollection<WhatsAppCommerceWarehouse> Warehouses,
    IReadOnlyCollection<WhatsAppCommerceCategory> Categories,
    IReadOnlyCollection<WhatsAppCommerceCollection> Collections,
    IReadOnlyCollection<WhatsAppCommerceProduct> Products,
    IReadOnlyCollection<WhatsAppCommerceMessage> Messages);
public sealed record WhatsAppCommerceCartItem(Guid ProductId, decimal Quantity);
public sealed record WhatsAppCommerceCartLine(Guid ProductId, string ProductCode, string ProductName,
    string? ImageUrl, decimal Quantity, decimal UnitPrice, decimal TaxPercentage,
    decimal TaxAmount, decimal LineTotal, decimal AvailableQuantity);
public sealed record WhatsAppCommerceCart(Guid WarehouseId, IReadOnlyCollection<WhatsAppCommerceCartLine> Items,
    decimal Subtotal, decimal TaxAmount, decimal GrandTotal);
public sealed record PlaceWhatsAppDemoOrderInput(Guid CustomerId, Guid WarehouseId,
    IReadOnlyCollection<WhatsAppCommerceCartItem> Items, string DeliveryAddress, string FulfillmentMethod, string PaymentType,
    int RedeemCoins = 0);
public sealed record WhatsAppCommerceOrderResult(Guid OrderId, string OrderNumber, string ErpStatus,
    decimal GrandTotal, int RedeemedCoins, decimal CoinDiscount, IReadOnlyCollection<WhatsAppCommerceMessage> Messages);
public sealed record WhatsAppCommerceReadinessCheck(string Key, string Label, bool Ready, string? SetupRoute, string? Detail);
public sealed record WhatsAppCommerceReadiness(bool Ready, IReadOnlyCollection<WhatsAppCommerceReadinessCheck> Checks);
public sealed record WhatsAppCommerceOrderSummary(Guid OrderId, string OrderNumber, DateTimeOffset OrderDate,
    decimal GrandTotal, string ErpStatus, string DisplayStatus, string SourceChannel, string ProviderMode,
    string DeliveryStatus, string? CourierName, string? TrackingNumber, DateTimeOffset? DispatchedOn, DateTimeOffset? DeliveredOn,
    string? CustomerName, string? CustomerMobile, string? DeliveryAddress, string? FulfillmentMethod, string? PaymentType);
public sealed record UpdateWhatsAppCommerceDeliveryInput(string DeliveryStatus, string? CourierName, string? TrackingNumber);
public sealed record WhatsAppCommerceOrderDetails(WhatsAppCommerceOrderSummary Order,
    IReadOnlyCollection<WhatsAppCommerceCartLine> Items);

public interface IWhatsAppCommerceProvider
{
    string Mode { get; }
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendWelcomeAsync(string storeName, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderConfirmationAsync(string orderNumber, decimal amount, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderStatusAsync(string orderNumber, string status, CancellationToken token);
    Task<WhatsAppProviderConnectionResult> ValidateConnectionAsync(WhatsAppProviderConnectionRequest request, CancellationToken token);
    Task<WhatsAppProviderTestMessageResult> SendTestMessageAsync(WhatsAppProviderTestMessageRequest request, CancellationToken token);
    Task<WhatsAppCommerceSendResult> SendProductCollectionAsync(WhatsAppCommerceSendRequest request, CancellationToken token);
    Task<WhatsAppTransactionalMessageResult> SendTransactionalAsync(WhatsAppTransactionalMessageRequest request,CancellationToken token) => Task.FromResult(new WhatsAppTransactionalMessageResult(false,null,DateTimeOffset.UtcNow,"Transactional messaging is not supported by this provider."));
}
public interface IWhatsAppCommerceProviderResolver { IWhatsAppCommerceProvider Resolve(string mode); }
public interface IWhatsAppCommerceService
{
    Task<WhatsAppCommerceSetup> GetSetupAsync(Guid tenantId, Guid? warehouseId, CancellationToken token);
    Task<WhatsAppCommerceCart> CalculateCartAsync(Guid tenantId, Guid warehouseId, IReadOnlyCollection<WhatsAppCommerceCartItem> items, CancellationToken token);
    Task<WhatsAppCommerceOrderResult> PlaceOrderAsync(Guid tenantId, PlaceWhatsAppDemoOrderInput input, string? actor, CancellationToken token);
    Task<WhatsAppCommerceReadiness> GetReadinessAsync(Guid tenantId, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> GetOrdersAsync(Guid tenantId, Guid customerId, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> GetDeliveryOrdersAsync(Guid tenantId, DateTimeOffset? fromDate, DateTimeOffset? toDate, string? deliveryStatus, string? trackingNumber, CancellationToken token);
    Task<WhatsAppCommerceOrderDetails> GetOrderAsync(Guid tenantId, Guid customerId, Guid orderId, CancellationToken token);
    Task<IReadOnlyCollection<WhatsAppCommerceMessage>> GetStatusNotificationsAsync(Guid tenantId, Guid customerId, CancellationToken token);
    Task<WhatsAppCommerceOrderSummary> UpdateDeliveryAsync(Guid tenantId, Guid orderId, UpdateWhatsAppCommerceDeliveryInput input, CancellationToken token);
    Task<WhatsAppCommerceSendResult> SendCollectionAsync(Guid tenantId, Guid collectionId, Guid customerId, CancellationToken token);
}
