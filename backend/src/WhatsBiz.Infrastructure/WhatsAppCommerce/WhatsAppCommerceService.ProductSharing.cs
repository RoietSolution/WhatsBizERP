using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

public sealed partial class WhatsAppCommerceService
{
    private string? ShareSetting(Guid tenant, string key) => configuration[$"WhatsApp:ProductSharing:Tenants:{tenant:D}:{key}"];
    private string? ShareOrigin(string key) => configuration[$"Storefront:Stores:{key}:PublicBaseUrl"]
        ?? configuration["Storefront:PublicBaseUrl"] ?? configuration["Storefront:BaseUrl"];
    private string ShareApiOrigin() => ProductSharingPolicy.HttpsRoot(configuration["Storefront:PublicApiBaseUrl"]
        ?? configuration["WhatsApp:PublicBaseUrl"]).AbsoluteUri;

    public async Task<ProductSharePreview> GetProductShareAsync(Guid tenantId, Guid productId, CancellationToken token)
    {
        tenantId = AuthenticatedTenant(tenantId);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        string storeKey;
        await using (var command = new SqlCommand("SELECT TenantKey FROM core.Tenants WHERE TenantId=@tenant AND IsActive=1", connection))
        {
            command.Parameters.AddWithValue("@tenant", tenantId);
            storeKey = await command.ExecuteScalarAsync(token) as string ?? throw new EntityNotFoundException("Storefront was not found.");
        }
        var publicStore = storefront ?? throw new BusinessRuleException("Storefront product sharing is unavailable.");
        // Reuse public product eligibility, tenant category/unit mappings and current V67 availability.
        var product = await publicStore.GetProductAsync(storeKey, productId, token)
            ?? throw new EntityNotFoundException("This product is not active and visible in this storefront.");
        var store = await publicStore.GetStoreAsync(storeKey, token) ?? throw new EntityNotFoundException("Storefront was not found.");
        var url = ProductSharingPolicy.ProductUrl(ShareOrigin(store.StoreKey), storeKey, productId);
        var description = ProductSharingPolicy.PlainDescription(product.Description);
        var price = ProductSharingPolicy.PriceLabel(product.SellingPrice, product.GstPercentage, product.GstPricingType);
        // Manual sharing must point to server HTML, not the generic Angular index document.
        var apiOrigin = ShareApiOrigin();
        var media = await publicStore.GetProductImageAsync(storeKey, productId, null, false, token);
        var imageVersion = media is null ? null : ProductSharingPolicy.ImageVersion(media.Content);
        var previewVersion = ProductSharingPolicy.PreviewVersion(product.Name, price, description, store.Name, url, imageVersion);
        var shareUrl = ProductSharingPolicy.PreviewUrl(apiOrigin, storeKey, productId, previewVersion);
        var message = ProductSharingPolicy.Message(product.Name, price, description, store.Name, shareUrl);
        string? image = null;
        string? imageProblem = null;
        if (media is not null)
        {
            image = new Uri(new Uri(apiOrigin), $"api/store/{Uri.EscapeDataString(storeKey)}/products/{productId:D}/share-image?v={imageVersion}").AbsoluteUri;
        }
        else imageProblem = "Add a public product image before using API Send. Manual link sharing is available.";
        var config = await SendConfiguration(connection, tenantId, token);
        var entitled = await features.IsEnabledAsync(tenantId, FeatureKeys.WhatsAppCommerce, token);
        var authorized = currentUser.Permissions.Contains(Permissions.Admin.Settings, StringComparer.Ordinal);
        await using var ledger = new SqlCommand("SELECT CASE WHEN COL_LENGTH(N'integration.WhatsAppProductShareRequests',N'SentContent') IS NOT NULL THEN 1 ELSE 0 END", connection);
        var ledgerReady = Convert.ToInt32(await ledger.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture) == 1;
        var available = authorized && entitled && config is not null && image is not null && ledgerReady;
        return new(productId, product.Name, product.SellingPrice, price, description, store.Name, url, image, message,
            "https://wa.me/?text=" + Uri.EscapeDataString(message), available,
            available ? null : !authorized ? "API Send requires Storefront/WhatsApp administrator permission. Manual sharing remains available."
                : !ledgerReady ? "API Send audit setup (V71) is not ready. Manual sharing remains available."
                : imageProblem ?? "API Send requires the tenant's enabled WhatsApp Commerce integration and entitlement. Manual sharing remains available.",
            shareUrl);
    }

    public async Task<ProductShareSendResult> SendProductShareAsync(Guid tenantId, Guid productId, SendProductShareInput input, CancellationToken token)
    {
        tenantId = AuthenticatedTenant(tenantId);
        if (input.CustomerId == Guid.Empty || input.RequestId == Guid.Empty || !input.ConsentConfirmed)
            throw new BusinessRuleException("Select a customer and explicitly confirm their consent to receive this product message.");
        var actor = currentUser.UserId ?? throw new WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException("An authenticated retailer identity is required.");
        var preview = await GetProductShareAsync(tenantId, productId, token);
        if (!preview.ApiSendAvailable) throw new BusinessRuleException(preview.ApiSendUnavailableReason ?? "API Send is unavailable.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        var config = await SendConfiguration(connection, tenantId, token) ?? throw new BusinessRuleException("WhatsApp is not configured.");
        if (config.Mode != "MOCK" && !await features.IsEnabledAsync(tenantId, FeatureKeys.MetaWhatsAppIntegration, token))
            throw new BusinessRuleException("Meta WhatsApp integration is not enabled for this tenant.");
        var customer = await Customer(connection, tenantId, input.CustomerId, token) ?? throw new EntityNotFoundException("Active customer was not found.");
        var recipient = ProductSharingPolicy.Recipient(customer.Mobile);
        if (config.Mode == "META_TEST" && recipient != ProductSharingPolicy.Recipient(config.TestRecipient))
            throw new BusinessRuleException("META_TEST can send only to the configured test recipient.");
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        DateTimeOffset? inbound = null;
        await using (var last = new SqlCommand("SELECT MAX(LastMessageAt) FROM integration.WhatsAppContacts WHERE TenantId=@tenant AND CustomerId=@customer AND NormalizedMobile=@recipient", connection))
        {
            last.Parameters.AddWithValue("@tenant", tenantId); last.Parameters.AddWithValue("@customer", input.CustomerId); last.Parameters.AddWithValue("@recipient", recipient);
            var value = await last.ExecuteScalarAsync(token);
            inbound = value is DateTimeOffset offset ? offset : value is DateTime date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null;
        }
        var session = config.Mode == "MOCK" || ProductSharingPolicy.WithinSession(inbound, now);
        var comment = ProductSharingPolicy.CommentForFormat(input.Comment, session);
        var template = session ? null : ShareSetting(tenantId, "TemplateName");
        var prefix = ShareSetting(tenantId, "TemplateUrlPrefix");
        if (!session && string.IsNullOrWhiteSpace(template))
            throw new BusinessRuleException("The customer-service window is closed. Configure an approved image-header product template with a View Product URL button.");
        var suffix = session ? "" : ProductSharingPolicy.TemplateSuffix(preview.ProductUrl, prefix ?? "");
        var caption = ProductSharingPolicy.Message(preview.ProductName, preview.PriceLabel, preview.Description, preview.StoreName, preview.ProductUrl, comment);
        if (session && caption.Length > 1024) throw new BusinessRuleException("The image caption is too long. Shorten the comment or use manual sharing.");
        var templateDescription = string.IsNullOrWhiteSpace(preview.Description) ? "View product details" : preview.Description;
        var contentSnapshot = session ? caption : System.Text.Json.JsonSerializer.Serialize(new { template,
            language = ShareSetting(tenantId, "LanguageCode") ?? "en_US", product = preview.ProductName, price = preview.PriceLabel,
            description = templateDescription, store = preview.StoreName, urlSuffix = suffix, image = preview.ImageUrl });
        string? waba;
        await using (var command = new SqlCommand("SELECT WhatsAppBusinessAccountId FROM integration.WhatsAppConfigurations WHERE TenantId=@tenant", connection))
        { command.Parameters.AddWithValue("@tenant", tenantId); waba = await command.ExecuteScalarAsync(token) as string; }
        // Durable tenant/request key is claimed before provider I/O. Even uncertain outcomes are never automatically resent.
        await using (var claim = new SqlCommand("""
            INSERT integration.WhatsAppProductShareRequests(TenantId,RequestId,ProductId,CustomerId,ConsentConfirmedBy,ConsentConfirmedAtUtc,Status,ProviderMode,SentContent)
            SELECT @tenant,@request,@product,@customer,@actor,SYSUTCDATETIME(),'PENDING',@mode,@content
            WHERE NOT EXISTS(SELECT 1 FROM integration.WhatsAppProductShareRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND RequestId=@request);
            SELECT CONVERT(int,@@ROWCOUNT);
            """, connection))
        {
            claim.Parameters.AddWithValue("@tenant", tenantId); claim.Parameters.AddWithValue("@request", input.RequestId);
            claim.Parameters.AddWithValue("@product", productId); claim.Parameters.AddWithValue("@customer", input.CustomerId);
            claim.Parameters.AddWithValue("@actor", actor); claim.Parameters.AddWithValue("@mode", config.Mode);
            claim.Parameters.AddWithValue("@content", contentSnapshot);
            if (Convert.ToInt32(await claim.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture) == 0)
                return await ExistingShare(connection, tenantId, productId, input, token);
        }
        var access = config.Mode == "MOCK" ? "" : protection.CreateProtector("WhatsBiz.WhatsApp.Secrets.v1")
            .Unprotect(config.ProtectedToken ?? throw new BusinessRuleException("WhatsApp credential is unavailable."));
        var result = await providers.Resolve(config.Mode).SendSingleProductAsync(new(config.ApiVersion ?? "", config.PhoneNumberId ?? "",
            access, recipient, caption,
            preview.ImageUrl!, preview.ProductUrl, suffix, template,
            ShareSetting(tenantId, "LanguageCode") ?? "en_US", [preview.ProductName, preview.PriceLabel,
                templateDescription, preview.StoreName], waba, prefix), token);
        await using (var update = new SqlCommand("UPDATE integration.WhatsAppProductShareRequests SET Status=@status,ProviderMessageId=@message,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND RequestId=@request", connection))
        {
            update.Parameters.AddWithValue("@status", result.Succeeded ? "ACCEPTED" : "UNCONFIRMED");
            update.Parameters.AddWithValue("@message", (object?)result.ProviderMessageId ?? DBNull.Value);
            update.Parameters.AddWithValue("@tenant", tenantId); update.Parameters.AddWithValue("@request", input.RequestId);
            await update.ExecuteNonQueryAsync(token);
        }
        if (result.Succeeded && usageBilling is not null && result.ProviderMessageId is not null && config.PhoneNumberId is not null)
            await usageBilling.RecordAcceptedAsync(new(tenantId, config.Mode, config.PhoneNumberId, result.ProviderMessageId,
                recipient, template, session ? WhatsAppMessageCategories.Service : WhatsAppMessageCategories.Marketing, result.AttemptedAt), token);
        return new(input.RequestId, result.Succeeded ? "ACCEPTED" : "UNCONFIRMED", result.ProviderMessageId,
            result.SafeMessage ?? "Check delivery status before retrying. No automatic resend will occur.");
    }

    public async Task<ProductShareSendResult> GetProductShareStatusAsync(Guid tenantId, Guid productId, Guid requestId, CancellationToken token)
    {
        tenantId = AuthenticatedTenant(tenantId);
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT s.Status,s.ProviderMessageId,w.MessageStatus
            FROM integration.WhatsAppProductShareRequests s
            OUTER APPLY(SELECT TOP(1) e.MessageStatus FROM integration.WhatsAppWebhookEvents e
                WHERE e.TenantId=s.TenantId AND e.MetaMessageId=s.ProviderMessageId AND e.EventType=N'MESSAGE_STATUS'
                ORDER BY e.EventTimestamp DESC,e.ReceivedOn DESC) w
            WHERE s.TenantId=@tenant AND s.ProductId=@product AND s.RequestId=@request;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@product", productId); command.Parameters.AddWithValue("@request", requestId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new EntityNotFoundException("Product share request was not found.");
        var state = reader.IsDBNull(2) ? reader.GetString(0) : reader.GetString(2);
        return new(requestId, state, reader.IsDBNull(1) ? null : reader.GetString(1), "Provider acceptance is not delivery. Status reflects received webhook evidence when available.");
    }

    private static async Task<ProductShareSendResult> ExistingShare(SqlConnection connection, Guid tenant, Guid product, SendProductShareInput input, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT ProductId,CustomerId,Status,ProviderMessageId FROM integration.WhatsAppProductShareRequests WHERE TenantId=@tenant AND RequestId=@request", connection);
        command.Parameters.AddWithValue("@tenant", tenant); command.Parameters.AddWithValue("@request", input.RequestId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || reader.GetGuid(0) != product || reader.GetGuid(1) != input.CustomerId)
            throw new BusinessRuleException("This send request identifier has already been used for another selection.");
        return new(input.RequestId, reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), "This request was already recorded. No duplicate message was sent.");
    }
}
