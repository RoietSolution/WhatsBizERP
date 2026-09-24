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
    public bool Supports(string mode) => mode.Equals(WhatsAppProviderModes.MetaTest, StringComparison.OrdinalIgnoreCase)
        || mode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase);

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

    public async Task<WhatsAppCommerceSendResult> SendProductCollectionAsync(WhatsAppCommerceSendRequest request, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            object payload;
            var products = request.Products.Take(10).ToArray();
            var fallback = request.Title + "\n\n" + string.Join("\n", request.Products.Select((x, i) => $"{i + 1}. {x.ProductName}  {x.SellingPrice:0.00}"));
            if (request.UseNativeProducts && products.Length == 1 && !string.IsNullOrWhiteSpace(products[0].CatalogId) && !string.IsNullOrWhiteSpace(products[0].ExternalProductId))
                payload = new Dictionary<string, object?> { ["messaging_product"] = "whatsapp", ["to"] = request.RecipientNumber, ["type"] = "interactive", ["interactive"] = new { type = "product", body = new { text = request.Title }, action = new { catalog_id = products[0].CatalogId, product_retailer_id = products[0].ExternalProductId } } };
            else if (request.UseNativeProducts && products.Length > 1 && products.All(x => !string.IsNullOrWhiteSpace(x.CatalogId) && !string.IsNullOrWhiteSpace(x.ExternalProductId)) && products.Select(x => x.CatalogId).Distinct(StringComparer.Ordinal).Count() == 1)
                payload = new Dictionary<string, object?> { ["messaging_product"] = "whatsapp", ["to"] = request.RecipientNumber, ["type"] = "interactive", ["interactive"] = new { type = "product_list", header = new { type = "text", text = request.Title }, body = new { text = "Products available now" }, action = new { catalog_id = products[0].CatalogId, sections = new[] { new { title = request.Title, product_items = products.Select(x => new { product_retailer_id = x.ExternalProductId }).ToArray() } } } } };
            else
                payload = new Dictionary<string, object?> { ["messaging_product"] = "whatsapp", ["to"] = request.RecipientNumber, ["type"] = "text", ["text"] = new { preview_url = false, body = fallback } };
            using var httpRequest = Create(HttpMethod.Post, $"{BaseUrl()}/{Uri.EscapeDataString(request.ApiVersion)}/{Uri.EscapeDataString(request.PhoneNumberId)}/messages", request.AccessToken);
            httpRequest.Content = JsonContent.Create(payload);
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(httpRequest, token);
            if (!response.IsSuccessStatusCode) { MetaProviderLogs.RequestRejected(logger, "SEND_COLLECTION", (int)response.StatusCode); return new(false, null, now, false, 0, request.RecipientNumber, SafeFailure(response.StatusCode)); }
            await using var stream = await response.Content.ReadAsStreamAsync(token); using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var id = document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0 && messages[0].TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
            return string.IsNullOrWhiteSpace(id) ? new(false, null, now, request.UseNativeProducts, 0, request.RecipientNumber, "Meta accepted the request but returned no message ID.") : new(true, id, now, request.UseNativeProducts, products.Length, request.RecipientNumber, "Collection accepted by Meta.");
        }
        catch (HttpRequestException) { return new(false, null, now, false, 0, request.RecipientNumber, "Meta could not be reached. Check network connectivity and try again."); }
            catch (JsonException) { return new(false, null, now, false, 0, request.RecipientNumber, "Meta returned an unexpected message response."); }
    }

    public async Task<WhatsAppTransactionalMessageResult> SendCommerceAsync(WhatsAppCommerceOutboundRequest request, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var payload = BuildCommercePayload(request);
            using var httpRequest = Create(HttpMethod.Post,
                $"{BaseUrl()}/{Uri.EscapeDataString(request.ApiVersion)}/{Uri.EscapeDataString(request.PhoneNumberId)}/messages",
                request.AccessToken);
            httpRequest.Content = JsonContent.Create(payload);
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(httpRequest, token);
            if (!response.IsSuccessStatusCode)
            {
                MetaProviderLogs.RequestRejected(logger, "SEND_COMMERCE", (int)response.StatusCode);
                return new(false, null, now, SafeFailure(response.StatusCode));
            }
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var id = document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
            return string.IsNullOrWhiteSpace(id)
                ? new(false, null, now, "Meta accepted the request but returned no message ID.")
                : new(true, id, now, "Commerce message accepted by Meta.");
        }
        catch (HttpRequestException) { return new(false, null, now, "Meta could not be reached. Check network connectivity and try again."); }
        catch (JsonException) { return new(false, null, now, "Meta returned an unexpected message response."); }
    }

    internal static object BuildCommercePayload(WhatsAppCommerceOutboundRequest request)
    {
        var message = request.Message;
        var actions = message.Actions?.ToArray() ?? [];
        var products = message.Products?.Take(10).ToArray() ?? [];
        object Text() => new { messaging_product = "whatsapp", to = request.RecipientNumber, type = "text",
            text = new { preview_url = false, body = message.FallbackText ?? message.Body } };

        if (message.Kind == WhatsAppCommerceMessageKinds.InteractiveMenu && actions.Length is > 0 and <= 10)
            return new { messaging_product = "whatsapp", to = request.RecipientNumber, type = "interactive",
                interactive = new { type = "list", header = new { type = "text", text = Limit(message.Header ?? "WhatsApp Store", 60) },
                    body = new { text = Limit(message.Body, 1024) }, action = new { button = "View options",
                        sections = new[] { new { title = "Shop", rows = actions.Select(x => new { id = x.Id, title = Limit(x.Title, 24), description = Limit(x.Description, 72) }).ToArray() } } } } };

        if (message.Kind == WhatsAppCommerceMessageKinds.InteractiveButtons && actions.Length is > 0 and <= 3)
            return new { messaging_product = "whatsapp", to = request.RecipientNumber, type = "interactive",
                interactive = new { type = "button", body = new { text = Limit(message.Body, 1024) },
                    action = new { buttons = actions.Select(x => new { type = "reply", reply = new { id = x.Id, title = Limit(x.Title, 20) } }).ToArray() } } };

        if (message.Kind == WhatsAppCommerceMessageKinds.Product && products.Length == 1
            && Mapped(products[0]))
            return new { messaging_product = "whatsapp", to = request.RecipientNumber, type = "interactive",
                interactive = new { type = "product", body = new { text = Limit(message.Body, 1024) },
                    action = new { catalog_id = products[0].CatalogId, product_retailer_id = products[0].ExternalProductId } } };

        if (message.Kind == WhatsAppCommerceMessageKinds.ProductList && products.Length is > 1 and <= 10
            && products.All(Mapped) && products.Select(x => x.CatalogId).Distinct(StringComparer.Ordinal).Count() == 1)
            return new { messaging_product = "whatsapp", to = request.RecipientNumber, type = "interactive",
                interactive = new { type = "product_list", header = new { type = "text", text = Limit(message.Header ?? "Products", 60) },
                    body = new { text = Limit(message.Body, 1024) }, action = new { catalog_id = products[0].CatalogId,
                        sections = new[] { new { title = Limit(message.Header ?? "Products", 24),
                            product_items = products.Select(x => new { product_retailer_id = x.ExternalProductId }).ToArray() } } } } };
        return Text();
    }

    private static bool Mapped(WhatsAppCommerceProductMessage product) =>
        !string.IsNullOrWhiteSpace(product.CatalogId) && !string.IsNullOrWhiteSpace(product.ExternalProductId);
    private static string Limit(string? value, int maximum) => string.IsNullOrWhiteSpace(value)
        ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, maximum)];

    public async Task<WhatsAppProviderSubscriptionResult> GetSubscribedAppsAsync(string apiVersion, string wabaId, string accessToken, CancellationToken token)
    {
        try
        {
            using var request = Create(HttpMethod.Get,
                $"{BaseUrl()}/{Uri.EscapeDataString(apiVersion)}/{Uri.EscapeDataString(wabaId)}/subscribed_apps",
                accessToken);
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                return new(false, Array.Empty<string>(), Array.Empty<string>(), null, SafeGraphFailure(response.StatusCode, body));

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new(false, Array.Empty<string>(), Array.Empty<string>(), null, "Meta returned an unexpected subscribed-apps response.");

            var ids = new List<string>();
            var subscribedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool? messages = null;
            foreach (var app in data.EnumerateArray())
            {
                var id = app.TryGetProperty("id", out var directId) ? directId.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) && app.TryGetProperty("whatsapp_business_api_data", out var businessApi)
                    && businessApi.TryGetProperty("id", out var nestedId)) id = nestedId.GetString();
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                if (TryReadFields(app, out var fields))
                {
                    foreach (var field in fields) subscribedFields.Add(field);
                    messages = fields.Any(x => x.Equals("messages", StringComparison.OrdinalIgnoreCase));
                }
            }
            return new(true, ids.Distinct(StringComparer.Ordinal).ToArray(), subscribedFields.ToArray(), messages, null);
        }
        catch (HttpRequestException) { return new(false, Array.Empty<string>(), Array.Empty<string>(), null, "Meta could not be reached. Check network connectivity and try again."); }
        catch (JsonException) { return new(false, Array.Empty<string>(), Array.Empty<string>(), null, "Meta returned an unexpected subscribed-apps response."); }
    }

    public async Task<WhatsAppProviderPhoneAssetsResult> GetPhoneNumbersAsync(string apiVersion, string wabaId, string accessToken, CancellationToken token)
    {
        const string fullFields = "id,display_phone_number,verified_name,quality_rating,code_verification_status,platform_type,name_status";
        const string minimalFields = "id,display_phone_number";
        try
        {
            var response = await GetPhoneNumbersResponseAsync(apiVersion, wabaId, accessToken, fullFields, token);
            var usedMinimal = false;
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                response = await GetPhoneNumbersResponseAsync(apiVersion, wabaId, accessToken, minimalFields, token);
                usedMinimal = true;
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode)
                    return new(false, Array.Empty<WhatsAppProviderPhoneAsset>(), usedMinimal, SafeGraphFailure(response.StatusCode, body));
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return new(false, Array.Empty<WhatsAppProviderPhoneAsset>(), usedMinimal, "Meta returned an unexpected phone-number response.");
                var assets = new List<WhatsAppProviderPhoneAsset>();
                foreach (var phone in data.EnumerateArray())
                {
                    var id = phone.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    assets.Add(new(id, String(phone, "display_phone_number"), String(phone, "verified_name"),
                        String(phone, "quality_rating"), String(phone, "code_verification_status"),
                        String(phone, "platform_type"), String(phone, "name_status")));
                }
                return new(true, assets, usedMinimal, null);
            }
        }
        catch (HttpRequestException) { return new(false, Array.Empty<WhatsAppProviderPhoneAsset>(), false, "Meta could not be reached. Check network connectivity and try again."); }
        catch (JsonException) { return new(false, Array.Empty<WhatsAppProviderPhoneAsset>(), false, "Meta returned an unexpected phone-number response."); }
    }

    public async Task<WhatsAppProviderPhoneDetailsResult> GetPhoneNumberDetailsAsync(string apiVersion, string phoneNumberId, string accessToken, CancellationToken token)
    {
        const string coreFields = "is_on_biz_app,platform_type";
        const string extendedFields = "is_on_biz_app,platform_type,quality_rating,code_verification_status,name_status";
        try
        {
            var response = await GetPhoneNumberDetailsResponseAsync(apiVersion, phoneNumberId, accessToken, extendedFields, token);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                response = await GetPhoneNumberDetailsResponseAsync(apiVersion, phoneNumberId, accessToken, coreFields, token);
            }
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode)
                    return new(false, null, null, SafeGraphFailure(response.StatusCode, body));
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var isOnBizApp = root.TryGetProperty("is_on_biz_app", out var bizNode) && bizNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? bizNode.GetBoolean() : (bool?)null;
                var platformType = String(root, "platform_type");
                if (isOnBizApp is null && string.IsNullOrWhiteSpace(platformType))
                    return new(false, null, null, "Meta returned an unexpected phone-number detail response.");
                return new(true, isOnBizApp, platformType, null);
            }
        }
        catch (HttpRequestException) { return new(false, null, null, "Meta could not be reached. Check network connectivity and try again."); }
        catch (JsonException) { return new(false, null, null, "Meta returned an unexpected phone-number detail response."); }
    }

    private async Task<HttpResponseMessage> GetPhoneNumberDetailsResponseAsync(string apiVersion, string phoneNumberId,
        string accessToken, string fields, CancellationToken token)
    {
        using var request = Create(HttpMethod.Get,
            $"{BaseUrl()}/{Uri.EscapeDataString(apiVersion)}/{Uri.EscapeDataString(phoneNumberId)}?fields={Uri.EscapeDataString(fields)}",
            accessToken);
        return await clients.CreateClient("MetaWhatsApp").SendAsync(request, token);
    }

    private async Task<HttpResponseMessage> GetPhoneNumbersResponseAsync(string apiVersion, string wabaId, string accessToken, string fields, CancellationToken token)
    {
        using var request = Create(HttpMethod.Get,
            $"{BaseUrl()}/{Uri.EscapeDataString(apiVersion)}/{Uri.EscapeDataString(wabaId)}/phone_numbers?fields={Uri.EscapeDataString(fields)}",
            accessToken);
        return await clients.CreateClient("MetaWhatsApp").SendAsync(request, token);
    }

    public async Task<WhatsAppTransactionalMessageResult> SendTransactionalAsync(WhatsAppTransactionalMessageRequest request,CancellationToken token)
    {
        var now=DateTimeOffset.UtcNow;
        try
        {
            object payload=string.IsNullOrWhiteSpace(request.ApprovedTemplateName)
                ? new { messaging_product="whatsapp",to=request.RecipientNumber,type="text",text=new { preview_url=false,body=request.Message } }
                : new { messaging_product="whatsapp",to=request.RecipientNumber,type="template",template=new { name=request.ApprovedTemplateName,language=new { code=request.LanguageCode },components=request.Parameters.Count==0?null:new[]{new { type="body",parameters=request.Parameters.Select(x=>new { type="text",text=x }).ToArray() }} }};
            using var httpRequest=Create(HttpMethod.Post,$"{BaseUrl()}/{Uri.EscapeDataString(request.ApiVersion)}/{Uri.EscapeDataString(request.PhoneNumberId)}/messages",request.AccessToken);
            httpRequest.Content=JsonContent.Create(payload);using var response=await clients.CreateClient("MetaWhatsApp").SendAsync(httpRequest,token);
            if(!response.IsSuccessStatusCode){MetaProviderLogs.RequestRejected(logger,request.TemplateKey,(int)response.StatusCode);return new(false,null,now,SafeFailure(response.StatusCode));}
            await using var stream=await response.Content.ReadAsStreamAsync(token);using var document=await JsonDocument.ParseAsync(stream,cancellationToken:token);
            var id=document.RootElement.TryGetProperty("messages",out var messages)&&messages.GetArrayLength()>0&&messages[0].TryGetProperty("id",out var node)?node.GetString():null;
            return string.IsNullOrWhiteSpace(id)?new(false,null,now,"Meta accepted the request but returned no message ID."):new(true,id,now,"Message accepted by Meta.");
        }
        catch(HttpRequestException){return new(false,null,now,"Meta could not be reached. Check network connectivity and try again.");}
        catch(JsonException){return new(false,null,now,"Meta returned an unexpected message response.");}
    }

    private static HttpRequestMessage Create(HttpMethod method, string url, string token)
    { var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
    private static string? String(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private string BaseUrl()
    {
        var value = configuration["WhatsApp:Meta:GraphBaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("WhatsApp:Meta:GraphBaseUrl must be configured as an HTTPS URL.");
        return uri.ToString().TrimEnd('/');
    }
    private static string SafeFailure(System.Net.HttpStatusCode status) =>
        $"Meta rejected the request (HTTP {(int)status}). Check the configured IDs, API version, recipient, token, and permissions.";

    private static string SafeGraphFailure(System.Net.HttpStatusCode status, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeNode) && codeNode.ValueKind == JsonValueKind.Number
                    && codeNode.TryGetInt32(out var numericCode) ? numericCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
                return $"Meta Graph error {code} (HTTP {(int)status}).";
            }
        }
        catch (JsonException) { }
        return $"Meta Graph request failed (HTTP {(int)status}).";
    }

    private static bool TryReadFields(JsonElement app, out string[] fields)
    {
        if (app.TryGetProperty("subscribed_fields", out var node) && node.ValueKind == JsonValueKind.Array)
        {
            fields = node.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).ToArray();
            return true;
        }
        fields = Array.Empty<string>();
        return false;
    }
}

internal static partial class MetaProviderLogs
{
    [LoggerMessage(2201, LogLevel.Warning, "WhatsApp {Operation} request was rejected with HTTP {StatusCode}.")]
    public static partial void RequestRejected(ILogger logger, string operation, int statusCode);
}
