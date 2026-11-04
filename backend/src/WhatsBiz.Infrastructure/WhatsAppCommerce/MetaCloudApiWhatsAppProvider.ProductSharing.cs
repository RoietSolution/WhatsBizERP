using System.Net.Http.Json;
using System.Text.Json;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

public sealed partial class MetaCloudApiWhatsAppProvider
{
    public async Task<WhatsAppTransactionalMessageResult> SendSingleProductAsync(WhatsAppSingleProductRequest request, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            if (request.TemplateName is not null && !await ApprovedProductTemplate(request, token))
                return new(false, null, now, "The configured template is not approved or does not match the required image header, four body parameters and first View Product URL button.");
            object payload = request.TemplateName is null
                ? new { messaging_product = "whatsapp", to = request.RecipientNumber, type = "image",
                    image = new { link = request.ImageUrl, caption = request.Message } }
                : new { messaging_product = "whatsapp", to = request.RecipientNumber, type = "template",
                    template = new { name = request.TemplateName, language = new { code = request.LanguageCode },
                        components = new object[] {
                            new { type = "header", parameters = new[] { new { type = "image", image = new { link = request.ImageUrl } } } },
                            new { type = "body", parameters = request.BodyParameters.Select(text => new { type = "text", text }).ToArray() },
                            new { type = "button", sub_type = "url", index = "0", parameters = new[] { new { type = "text", text = request.UrlSuffix } } }
                        } } };
            using var message = Create(HttpMethod.Post,
                $"{BaseUrl()}/{Uri.EscapeDataString(request.ApiVersion)}/{Uri.EscapeDataString(request.PhoneNumberId)}/messages", request.AccessToken);
            message.Content = JsonContent.Create(payload);
            using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(message, token);
            if (!response.IsSuccessStatusCode)
                return new(false, null, now, SafeFailure(response.StatusCode));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var id = json.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var value) ? value.GetString() : null;
            return string.IsNullOrWhiteSpace(id) ? new(false, null, now, "Meta returned no message ID. Check status before retrying.")
                : new(true, id, now, request.TemplateName is null
                    ? "Image caption and clickable product link accepted by Meta; no URL button was sent. Delivery is confirmed by webhook."
                    : "Approved image template with the configured product URL button accepted by Meta. Delivery is confirmed by webhook.");
        }
        catch (HttpRequestException) { return new(false, null, now, "Meta could not be reached. Delivery is unconfirmed; do not blindly resend."); }
        catch (JsonException) { return new(false, null, now, "Unexpected Meta response. Delivery is unconfirmed; check status before retrying."); }
    }

    private async Task<bool> ApprovedProductTemplate(WhatsAppSingleProductRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.WabaId) || string.IsNullOrWhiteSpace(request.TemplateUrlPrefix)) return false;
        using var lookup = Create(HttpMethod.Get,
            $"{BaseUrl()}/{Uri.EscapeDataString(request.ApiVersion)}/{Uri.EscapeDataString(request.WabaId)}/message_templates?name={Uri.EscapeDataString(request.TemplateName!)}&fields=name,status,category,language,components", request.AccessToken);
        using var response = await clients.CreateClient("MetaWhatsApp").SendAsync(lookup, token);
        if (!response.IsSuccessStatusCode) return false;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!document.RootElement.TryGetProperty("data", out var data)) return false;
        foreach (var template in data.EnumerateArray())
        {
            if (String(template, "name") != request.TemplateName || String(template, "status") != "APPROVED" || String(template, "category") != "MARKETING"
                || String(template, "language") != request.LanguageCode || !template.TryGetProperty("components", out var components)) continue;
            var header = false; var body = false; var button = false;
            foreach (var component in components.EnumerateArray())
            {
                switch (String(component, "type"))
                {
                    case "HEADER": header = String(component, "format") == "IMAGE"; break;
                    case "BODY":
                        var text = String(component, "text") ?? "";
                        body = Enumerable.Range(1, 4).All(i => text.Contains("{{" + i + "}}", StringComparison.Ordinal))
                            && !text.Contains("{{5}}", StringComparison.Ordinal); break;
                    case "BUTTONS":
                        if (component.TryGetProperty("buttons", out var buttons) && buttons.GetArrayLength() > 0)
                            button = String(buttons[0], "type") == "URL" && String(buttons[0], "url") == request.TemplateUrlPrefix + "{{1}}";
                        break;
                }
            }
            if (header && body && button) return true;
        }
        return false;
    }
}
