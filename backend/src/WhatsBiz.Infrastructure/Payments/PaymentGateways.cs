using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Payments;

namespace WhatsBiz.Infrastructure.Payments;

public sealed class PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways) : IPaymentGatewayResolver
{
    public IPaymentGateway Resolve(string provider) => gateways.FirstOrDefault(x => x.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase))
        ?? throw new BusinessRuleException("The selected payment provider is not supported.");
}

public sealed class RazorpayPaymentGateway(IHttpClientFactory clients) : IPaymentGateway
{
    public string Provider => PaymentProviders.Razorpay;

    public async Task<GatewayCreateResult> CreatePaymentAsync(PaymentGatewayConfiguration configuration, GatewayCreateRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(configuration.KeyId) || string.IsNullOrWhiteSpace(configuration.KeySecret))
            throw new BusinessRuleException("Razorpay is not fully configured.");
        var minorUnits = ToMinorUnits(request.Amount, request.Currency);
        var payload = new
        {
            amount = minorUnits,
            currency = request.Currency,
            accept_partial = false,
            reference_id = request.TransactionReference,
            description = $"Payment for order {request.OrderNumber}",
            customer = new { name = request.CustomerName, contact = request.CustomerMobile },
            notify = new { sms = false, email = false },
            reminder_enable = false,
            notes = new { payment_id = request.PaymentId.ToString("N"), order_number = request.OrderNumber }
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, "payment_links");
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{configuration.KeyId}:{configuration.KeySecret}")));
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient("Razorpay").SendAsync(message, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new BusinessRuleException("Razorpay could not create the payment link. Verify the retailer configuration and retry.");
        using var json = JsonDocument.Parse(body); var root = json.RootElement;
        var linkId = root.GetProperty("id").GetString() ?? throw new BusinessRuleException("Razorpay returned an invalid payment-link response.");
        var shortUrl = root.GetProperty("short_url").GetString() ?? throw new BusinessRuleException("Razorpay returned no payment URL.");
        return new(null, linkId, shortUrl, shortUrl);
    }

    public async Task<GatewayStatusResult> GetPaymentStatusAsync(PaymentGatewayConfiguration configuration, string providerReference, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"payment_links/{Uri.EscapeDataString(providerReference)}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{configuration.KeyId}:{configuration.KeySecret}")));
        using var response = await clients.CreateClient("Razorpay").SendAsync(message, token);
        if (!response.IsSuccessStatusCode) return new(CommercePaymentStatuses.Pending, null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var status = json.RootElement.GetProperty("status").GetString();
        string? paymentId = null;
        if (json.RootElement.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array && payments.GetArrayLength() > 0)
            paymentId = payments[0].TryGetProperty("payment_id", out var value) ? value.GetString() : null;
        return new(status == "paid" ? CommercePaymentStatuses.Paid : status == "cancelled" || status == "expired" ? CommercePaymentStatuses.Failed : CommercePaymentStatuses.Pending, paymentId);
    }

    public GatewayWebhookResult VerifyWebhook(PaymentGatewayConfiguration configuration, ReadOnlyMemory<byte> rawBody, string signature, string? eventId)
    {
        if (string.IsNullOrWhiteSpace(configuration.WebhookSecret) || string.IsNullOrWhiteSpace(signature)) return new(false, eventId, null, null, null, null, null, null, false, false);
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature.Trim()); } catch (FormatException) { return new(false, eventId, null, null, null, null, null, null, false, false); }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(configuration.WebhookSecret), rawBody.Span);
        if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(supplied, expected)) return new(false, eventId, null, null, null, null, null, null, false, false);
        using var json = JsonDocument.Parse(rawBody); var root = json.RootElement;
        var eventType = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        var payload = root.TryGetProperty("payload", out var p) ? p : default;
        var payment = Entity(payload, "payment"); var link = Entity(payload, "payment_link"); var order = Entity(payload, "order");
        var providerPaymentId = Text(payment, "id");
        var providerOrderId = Text(payment, "order_id") ?? Text(order, "id");
        var providerReference = Text(link, "id");
        var amountMinor = Long(payment, "amount") ?? Long(link, "amount_paid");
        var currency = Text(payment, "currency") ?? Text(link, "currency");
        var paid = eventType is "payment.captured" or "order.paid" or "payment_link.paid";
        var failed = eventType is "payment.failed" or "payment_link.cancelled" or "payment_link.expired";
        return new(true, eventId, eventType, providerOrderId, providerReference, providerPaymentId,
            amountMinor is null ? null : amountMinor.Value / 100m, currency, paid, failed);
    }

    public Task RefundPaymentAsync(PaymentGatewayConfiguration configuration, string providerPaymentId, decimal amount, string currency, CancellationToken token)
        => throw new NotSupportedException("Razorpay refunds are intentionally outside this initial implementation.");

    internal static long ToMinorUnits(decimal amount, string currency)
    {
        if (!currency.Equals("INR", StringComparison.OrdinalIgnoreCase)) throw new BusinessRuleException("Only INR Razorpay payments are currently supported.");
        var value = amount * 100m;
        if (value != decimal.Truncate(value) || value <= 0 || value > long.MaxValue) throw new BusinessRuleException("The order amount cannot be represented safely in currency subunits.");
        return decimal.ToInt64(value);
    }
    private static JsonElement Entity(JsonElement payload, string name) => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var wrapper) && wrapper.TryGetProperty("entity", out var entity) ? entity : default;
    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static long? Long(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.TryGetInt64(out var result) ? result : null;
}

public sealed class DirectUpiPaymentGateway : IPaymentGateway
{
    public string Provider => PaymentProviders.DirectUpi;
    public Task<GatewayCreateResult> CreatePaymentAsync(PaymentGatewayConfiguration configuration, GatewayCreateRequest request, CancellationToken token)
    {
        if (!PaymentValidation.IsValidVpa(configuration.UpiVpa) || string.IsNullOrWhiteSpace(configuration.PayeeName)) throw new BusinessRuleException("Direct UPI is not fully configured.");
        var values = new Dictionary<string,string> { ["pa"] = configuration.UpiVpa!, ["pn"] = configuration.PayeeName!, ["am"] = request.Amount.ToString("0.00", CultureInfo.InvariantCulture), ["cu"] = request.Currency, ["tr"] = request.TransactionReference, ["tn"] = $"Order {request.OrderNumber}" };
        var uri = "upi://pay?" + string.Join('&', values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        return Task.FromResult(new GatewayCreateResult(null, null, uri, uri));
    }
    public Task<GatewayStatusResult> GetPaymentStatusAsync(PaymentGatewayConfiguration configuration, string providerReference, CancellationToken token) => Task.FromResult(new GatewayStatusResult(CommercePaymentStatuses.PendingVerification, null));
    public GatewayWebhookResult VerifyWebhook(PaymentGatewayConfiguration configuration, ReadOnlyMemory<byte> rawBody, string signature, string? eventId) => new(false, eventId, null, null, null, null, null, null, false, false);
}

public sealed class CashOnDeliveryPaymentGateway : IPaymentGateway
{
    public string Provider => PaymentProviders.Cod;
    public Task<GatewayCreateResult> CreatePaymentAsync(PaymentGatewayConfiguration configuration, GatewayCreateRequest request, CancellationToken token) => Task.FromResult(new GatewayCreateResult(null, null, null, "Cash on Delivery"));
    public Task<GatewayStatusResult> GetPaymentStatusAsync(PaymentGatewayConfiguration configuration, string providerReference, CancellationToken token) => Task.FromResult(new GatewayStatusResult(CommercePaymentStatuses.CodPending, null));
    public GatewayWebhookResult VerifyWebhook(PaymentGatewayConfiguration configuration, ReadOnlyMemory<byte> rawBody, string signature, string? eventId) => new(false, eventId, null, null, null, null, null, null, false, false);
}

public static class PaymentValidation
{
    public static bool IsValidVpa(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsWhiteSpace)) return false;
        var at = value.IndexOf('@'); return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1;
    }
    public static string MaskKeyId(string value) => value.Length <= 4 ? new('*', value.Length) : $"{value[..Math.Min(4,value.Length)]}{new string('*', Math.Max(4,value.Length-8))}{value[^4..]}";
}
