using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

public sealed class MockWhatsAppProvider : IWhatsAppCommerceProvider
{
    public string Mode => WhatsAppProviderModes.Mock;
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendWelcomeAsync(string storeName, CancellationToken token) =>
        Task.FromResult<IReadOnlyCollection<WhatsAppCommerceMessage>>([new("WHATS_BIZ", "TEXT", $"Welcome to {storeName}\nWhat would you like to do?")]);
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderConfirmationAsync(string orderNumber, decimal amount, CancellationToken token) =>
        Task.FromResult<IReadOnlyCollection<WhatsAppCommerceMessage>>([new("WHATS_BIZ", "ORDER_CONFIRMATION", $"Order confirmed ✓\n\nOrder: #{orderNumber}\nAmount: ₹{amount:0.00}\n\nThank you for shopping with us.")]);
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderStatusAsync(string orderNumber, string status, CancellationToken token)
    {
        var text = status switch
        {
            "COMPLETED" => $"Order #{orderNumber} completed successfully.\nThank you for shopping with us.",
            "CANCELLED" => $"Order #{orderNumber} has been cancelled.",
            "RETURNED" => $"Order #{orderNumber} has been returned.",
            "PARTIALLY RETURNED" => $"Order #{orderNumber} has been partially returned.",
            _ => $"Your order #{orderNumber} is now {status.ToLowerInvariant()}."
        };
        return Task.FromResult<IReadOnlyCollection<WhatsAppCommerceMessage>>([new("WHATS_BIZ", "ORDER_STATUS", text)]);
    }
    public Task<WhatsAppProviderConnectionResult> ValidateConnectionAsync(WhatsAppProviderConnectionRequest request, CancellationToken token) =>
        throw new BusinessRuleException("MOCK mode does not use a Meta connection.");
    public Task<WhatsAppProviderTestMessageResult> SendTestMessageAsync(WhatsAppProviderTestMessageRequest request, CancellationToken token) =>
        throw new BusinessRuleException("MOCK mode does not send external messages.");
    public Task<WhatsAppCommerceSendResult> SendProductCollectionAsync(WhatsAppCommerceSendRequest request, CancellationToken token)
    {
        var text = $"{request.Title}\n\n" + string.Join("\n", request.Products.Select((x, i) => $"{i + 1}. {x.ProductName}  ₹{x.SellingPrice:0.00}"));
        return Task.FromResult(new WhatsAppCommerceSendResult(true, $"mock-collection-{Guid.NewGuid():N}", DateTimeOffset.UtcNow, false, request.Products.Count, request.RecipientNumber, text));
    }
}

public sealed class WhatsAppCommerceProviderResolver(IEnumerable<IWhatsAppCommerceProvider> providers) : IWhatsAppCommerceProviderResolver
{
    public IWhatsAppCommerceProvider Resolve(string mode) => providers.FirstOrDefault(x => x.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))
        ?? throw new BusinessRuleException($"WhatsApp provider mode {mode} is configured but is not implemented for commerce yet.");
}
