using ImageMagick;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/products/{productId:guid}/whatsapp-share"), HasPermission(Permissions.Product.View)]
public sealed class ProductSharingController(IWhatsAppCommerceService service, ICurrentUserService user) : ControllerBase
{
    private Guid Tenant => user.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
    [HttpGet]
    public Task<ProductSharePreview> Preview(Guid productId, CancellationToken token) => service.GetProductShareAsync(Tenant, productId, token);
    [HttpPost, HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<ProductShareSendResult> Send(Guid productId, SendProductShareInput input, CancellationToken token) => service.SendProductShareAsync(Tenant, productId, input, token);
    [HttpGet("{requestId:guid}"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.WhatsAppCommerce)]
    public Task<ProductShareSendResult> Status(Guid productId, Guid requestId, CancellationToken token) => service.GetProductShareStatusAsync(Tenant, productId, requestId, token);
}

// Meta image headers require JPEG/PNG. Existing optimized storefront assets may be WebP.
[ApiController, AllowAnonymous, Route("api/store/{storeKey}/products/{productId:guid}/share-image")]
public sealed class ProductShareImageController(IStorefrontService storefront, IConfiguration configuration) : ControllerBase
{
    [HttpGet("/api/store/{storeKey}/products/{productId:guid}/share"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LinkPreview(string storeKey, Guid productId, CancellationToken token)
    {
        var product = await storefront.GetProductAsync(storeKey, productId, token);
        var store = await storefront.GetStoreAsync(storeKey, token);
        if (product is null || store is null) return NotFound();
        var origin = configuration[$"Storefront:Stores:{store.StoreKey}:PublicBaseUrl"]
            ?? configuration["Storefront:PublicBaseUrl"] ?? configuration["Storefront:BaseUrl"];
        var url = ProductSharingPolicy.ProductUrl(origin, store.StoreKey, productId);
        var api = ProductSharingPolicy.HttpsRoot(configuration["Storefront:PublicApiBaseUrl"] ?? configuration["WhatsApp:PublicBaseUrl"]);
        var media = await storefront.GetProductImageAsync(storeKey, productId, null, false, token);
        var imageTags = "";
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        if (media is not null)
        {
            var version = ProductSharingPolicy.ImageVersion(media.Content);
            var image = new Uri(api, $"api/store/{Uri.EscapeDataString(store.StoreKey)}/products/{productId:D}/share-image?v={version}").AbsoluteUri;
            using var decoded = new MagickImage(media.Content);
            imageTags = $"<meta property=\"og:image\" content=\"{Encode(image)}\"><meta property=\"og:image:secure_url\" content=\"{Encode(image)}\">"
                + $"<meta property=\"og:image:type\" content=\"image/jpeg\"><meta property=\"og:image:width\" content=\"{decoded.Width}\"><meta property=\"og:image:height\" content=\"{decoded.Height}\">"
                + $"<meta property=\"og:image:alt\" content=\"{Encode(product.Name)}\"><meta name=\"twitter:card\" content=\"summary_large_image\">";
        }
        var title = Encode(product.Name + " | " + store.Name);
        var description = Encode(ProductSharingPolicy.PriceLabel(product.SellingPrice, product.GstPercentage, product.GstPricingType)
            + " — " + ProductSharingPolicy.PlainDescription(product.Description));
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; img-src https:; script-src 'nonce-{nonce}'; base-uri 'none'; frame-ancestors 'none'";
        return Content($"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{title}</title><meta property="og:type" content="product"><meta property="og:title" content="{title}">
            <meta property="og:description" content="{description}"><meta property="og:url" content="{Encode(url)}">
            {imageTags}<link rel="canonical" href="{Encode(url)}"></head>
            <body><h1>{title}</h1><p>{description}</p><a href="{Encode(url)}">View Product</a>
            <script nonce="{nonce}">window.location.replace({JsonSerializer.Serialize(url)});</script></body></html>
            """, "text/html; charset=utf-8");
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Image(string storeKey, Guid productId, CancellationToken token)
    {
        // The existing media service rechecks public tenant/product visibility on every request.
        var image = await storefront.GetProductImageAsync(storeKey, productId, null, false, token);
        if (image is null) return NotFound();
        using var converted = new MagickImage(image.Content);
        converted.BackgroundColor = MagickColors.White;
        converted.Alpha(AlphaOption.Remove);
        converted.Strip(); converted.Quality = 85;
        var jpeg = converted.ToByteArray(MagickFormat.Jpeg);
        Response.Headers.ETag = "\"" + ProductSharingPolicy.ImageVersion(jpeg) + "\"";
        return File(jpeg, "image/jpeg");
    }
}
