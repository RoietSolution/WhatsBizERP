using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/store/{storeKey}")]
public sealed class StoreController(IStorefrontService storefront, IStorefrontCheckoutService checkout) : ControllerBase
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

    [HttpPost("checkout/razorpay")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCheckoutResult>> Checkout(
        string storeKey, StorefrontCheckoutInput input, CancellationToken token)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (!Guid.TryParse(idempotencyKey, out var parsed) || parsed == Guid.Empty)
            return BadRequest(new { message = "A valid Idempotency-Key header is required." });
        return Ok(await checkout.CheckoutAsync(storeKey, input, parsed.ToString("D"), token));
    }
}
