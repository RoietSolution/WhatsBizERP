using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/store/{storeKey}")]
public sealed partial class StoreController(IStorefrontService storefront, IStorefrontCheckoutService checkout, ILogger<StoreController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StorefrontStoreDto>> GetStore(string storeKey, CancellationToken token)
    {
        var store = await storefront.GetStoreAsync(storeKey, token);
        return store is null ? NotFound() : Ok(store);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontCategoryDto>>> GetCategories(string storeKey, CancellationToken token)
    {
        var categories = await storefront.GetCategoriesAsync(storeKey, token);
        return categories is null ? NotFound() : Ok(categories);
    }

    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontProductDto>>> GetProducts(string storeKey, CancellationToken token)
    {
        var products = await storefront.GetProductsAsync(storeKey, token);
        return products is null ? NotFound() : Ok(products);
    }

    [HttpGet("products/{productId:guid}")]
    public async Task<ActionResult<StorefrontProductDto>> GetProduct(string storeKey, Guid productId, CancellationToken token)
    {
        var product = await storefront.GetProductAsync(storeKey, productId, token);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("products/{productId:guid}/image")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetProductImage(string storeKey, Guid productId, CancellationToken token)
    {
        var image = await storefront.GetProductImageAsync(storeKey, productId, token);
        return image is null ? NotFound() : File(image.Content, image.ContentType);
    }

    [HttpGet("presentation/logo")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetLogo(string storeKey, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, "logo", null, token));

    [HttpGet("presentation/banners/{slot}")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBanner(string storeKey, string slot, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, $"banner-{slot}", null, token));

    [HttpGet("presentation/categories/{categoryId:guid}")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCategoryImage(string storeKey, Guid categoryId, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, "category", categoryId, token));

    [HttpPost("checkout")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCheckoutResult>> CheckoutWithSelectedMethod(
        string storeKey, StorefrontCheckoutInput input, CancellationToken token) => await CheckoutCore(storeKey, input, token);

    [HttpPost("checkout/razorpay")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCheckoutResult>> Checkout(
        string storeKey, StorefrontCheckoutInput input, CancellationToken token)
        => await CheckoutCore(storeKey, input with { PaymentProvider = "RAZORPAY" }, token);

    private async Task<ActionResult<StorefrontCheckoutResult>> CheckoutCore(string storeKey, StorefrontCheckoutInput input, CancellationToken token)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (!Guid.TryParse(idempotencyKey, out var parsed) || parsed == Guid.Empty)
            return BadRequest(new { message = "A valid Idempotency-Key header is required." });
        try { return Ok(await checkout.CheckoutAsync(storeKey, input, parsed.ToString("D"), token)); }
        catch (BusinessRuleException exception)
        {
            var safeMessage = SafeCheckoutMessage(exception.Message);
            if (safeMessage is null)
            {
                StorefrontControllerLogs.UnsafeCheckoutRule(logger, exception, storeKey);
                safeMessage = "We couldn't place this order right now. Please check your details and try again.";
            }
            return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Checkout could not be completed", Detail = safeMessage });
        }
    }

    private IActionResult ToImage(StorefrontImage? image) => image is null ? NotFound() : File(image.Content, image.ContentType);
    private static string? SafeCheckoutMessage(string message) => message switch
    {
        "Enter a valid customer name." => message,
        "Enter a valid mobile number." => message,
        "Enter a valid email address." => message,
        "Enter a valid delivery address." => message,
        "A valid idempotency key is required." => message,
        "The cart is empty." => message,
        "The cart contains invalid items or quantities." => message,
        "The selected payment method is not currently available for this store." => message,
        "The selected payment method is not configured." => "This payment method is currently unavailable.",
        "The selected payment method is not available." => "This payment method is currently unavailable.",
        "The cart items are not available together at this store." => "One or more products are no longer available.",
        "One or more cart items are no longer available." => "One or more products are no longer available.",
        "One or more products are not available." => "One or more products are no longer available.",
        "Insufficient stock for invoice." => "One or more products are no longer available.",
        "Active invoice series not configured." => "This store is temporarily unable to accept orders. Please contact the retailer.",
        "Warehouse is not available." => "This store is temporarily unable to accept orders. Please try again later.",
        "Online payment is currently unavailable. Please try again." => message,
        "UPI payment is currently unavailable. Please try again." => message,
        _ => null
    };
}

internal static partial class StorefrontControllerLogs
{
    [LoggerMessage(3402, LogLevel.Warning, "Storefront checkout rejected by an unmapped business rule for store {StoreKey}.")]
    public static partial void UnsafeCheckoutRule(ILogger logger, Exception exception, string storeKey);
}
