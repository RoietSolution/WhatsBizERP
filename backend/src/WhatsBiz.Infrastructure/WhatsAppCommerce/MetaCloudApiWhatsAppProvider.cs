using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

public sealed partial class MetaCloudApiWhatsAppProvider(IHttpClientFactory clients, IConfiguration configuration,
    ILogger<MetaCloudApiWhatsAppProvider> logger) : IWhatsAppCommerceProvider
{
    public string Mode => WhatsAppProviderModes.MetaTest;

    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendWelcomeAsync(string storeName, CancellationToken token) =>
        throw new BusinessRuleException("META_TEST commerce conversations are not implemented in WC-003.");
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderConfirmationAsync(string orderNumber, decimal amount, CancellationToken token) =>
        throw new BusinessRuleException("META_TEST commerce order messaging is not implemented in WC-003.");
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderStatusAsync(string orderNumber, string status, CancellationToken token) =>
        throw new BusinessRuleException("META_TEST commerce status messaging is not implemented in WC-003.");

    public async Task<WhatsAppProviderConnectionResult> ValidateConnectionAsync(WhatsAppProviderConnectionRequest request, CancellationToken token)
    {
        try
        {
            using var httpRequest = Create(HttpMethod.Get,
                $"{BaseUrl()}/{Uri.EscapeDataString(request.ApiVersion)}/{Uri.EscapeDataString(request.WhatsAppBusinessAccountId)}/phone_numbers?fields=id,display_phone_number,verified_name,quality_rating",
                request.AccessToken);
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(httpRequest, token);
            if (!response.IsSuccessStatusCode)
            { MetaProviderLogs.RequestRejected(logger, "VALIDATE", (int)response.StatusCode); return new(false, null, null, SafeFailure(response.StatusCode)); }
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (!document.RootElement.TryGetProperty("data", out var data)) return new(false, null, null, "Meta returned an unexpected validation response.");
            foreach (var phone in data.EnumerateArray())
                if (phone.TryGetProperty("id", out var id) && id.GetString() == request.PhoneNumberId)
                    return new(true,
                        phone.TryGetProperty("display_phone_number", out var display) ? display.GetString() : null,
                        phone.TryGetProperty("verified_name", out var name) ? name.GetString() : null,
                        "Connection validated successfully.");
            return new(false, null, null, "The phone number ID does not belong to the configured WhatsApp Business Account.");
        }
        catch (HttpRequestException) { return new(false, null, null, "Meta could not be reached. Check network connectivity and try again."); }
        catch (JsonException) { return new(false, null, null, "Meta returned an unexpected validation response."); }
    }

    public async Task<WhatsAppProviderTestMessageResult> SendTestMessageAsync(WhatsAppProviderTestMessageRequest request, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            using var httpRequest = Create(HttpMethod.Post,
                $"{BaseUrl()}/{Uri.EscapeDataString(request.ApiVersion)}/{Uri.EscapeDataString(request.PhoneNumberId)}/messages",
                request.AccessToken);
            httpRequest.Content = JsonContent.Create(new { messaging_product = "whatsapp", recipient_type = "individual",
                to = request.RecipientNumber, type = "text", text = new { preview_url = false, body = request.Message } });
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(httpRequest, token);
            if (!response.IsSuccessStatusCode)
            { MetaProviderLogs.RequestRejected(logger, "SEND_TEST", (int)response.StatusCode); return new(false, null, now, SafeFailure(response.StatusCode)); }
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var id = document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
            return string.IsNullOrWhiteSpace(id) ? new(false, null, now, "Meta accepted the request but returned no message ID.")
                : new(true, id, now, "Test message accepted by Meta. Delivery is confirmed by status webhook.");
        }
        catch (HttpRequestException) { return new(false, null, now, "Meta could not be reached. Check network connectivity and try again."); }
        catch (JsonException) { return new(false, null, now, "Meta returned an unexpected message response."); }
    }

    private static HttpRequestMessage Create(HttpMethod method, string url, string token)
    { var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
    private string BaseUrl()
    {
        var value = configuration["WhatsApp:Meta:GraphBaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("WhatsApp:Meta:GraphBaseUrl must be configured as an HTTPS URL.");
        return uri.ToString().TrimEnd('/');
    }
    private static string SafeFailure(System.Net.HttpStatusCode status) =>
        $"Meta rejected the request (HTTP {(int)status}). Check the configured IDs, API version, recipient, token, and permissions.";
}

internal static partial class MetaProviderLogs
{
    [LoggerMessage(2201, LogLevel.Warning, "META_TEST request {Operation} was rejected with HTTP {StatusCode}.")]
    public static partial void RequestRejected(ILogger logger, string operation, int statusCode);
}
