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
public sealed partial class StoreController(IStorefrontService storefront, IStorefrontCheckoutService checkout, IStorefrontCustomerService customers, IStorefrontCustomerAuthenticationService customerAuth, IStorefrontReviewService reviews, IStorefrontCancellationService cancellations, ILogger<StoreController> logger) : ControllerBase
{
    private const long MaxProfileImageBytes = 5 * 1024 * 1024;
    private const long MaxProfileImageRequestBytes = 6 * 1024 * 1024;
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
    public async Task<ActionResult<StorefrontProductPageDto>> GetProducts(string storeKey, [FromQuery] int page = 1, [FromQuery] int pageSize = 24, CancellationToken token = default)
    {
        page = Math.Clamp(page, 1, 100000); pageSize = Math.Clamp(pageSize, 1, 50); var products = await storefront.GetProductsAsync(storeKey, page, pageSize, token);
        return products is null ? NotFound() : Ok(products);
    }

    [HttpGet("products/{productId:guid}")]
    public async Task<ActionResult<StorefrontProductDto>> GetProduct(string storeKey, Guid productId, CancellationToken token)
    {
        var product = await storefront.GetProductAsync(storeKey, productId, token);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("products/{productId:guid}/reviews")]
    public async Task<ActionResult<StorefrontReviewSummaryDto>> GetReviews(string storeKey, Guid productId, CancellationToken token)
    {
        var result = await reviews.GetAsync(storeKey, productId, CustomerSessionToken(), token);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("products/{productId:guid}/reviews/mine")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontReviewDto>> UpsertReview(string storeKey, Guid productId, StorefrontReviewInput input, CancellationToken token)
    {
        try
        {
            var result = await reviews.UpsertAsync(storeKey, productId, CustomerSessionToken(), input, token);
            return result is null ? Unauthorized() : Ok(result);
        }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpGet("offers/{offerId:guid}")]
    public async Task<ActionResult<StorefrontOfferDto>> GetOffer(string storeKey, Guid offerId, CancellationToken token)
    {
        var offer = await storefront.GetOfferAsync(storeKey, offerId, token);
        return offer is null ? NotFound() : Ok(offer);
    }
    [HttpGet("products/{productId:guid}/image")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetProductImage(string storeKey, Guid productId, CancellationToken token)
    {
        var image = await storefront.GetProductImageAsync(storeKey, productId, token);
        return image is null ? NotFound() : File(image.Content, image.ContentType);
    }

    [HttpGet("pwa/manifest.webmanifest")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetPwaManifest(string storeKey, [FromQuery] string? shopOrigin, CancellationToken token)
    {
        var store = await storefront.GetStoreAsync(storeKey, token);
        if (store is null) return NotFound();
        var key = store.StoreKey.ToLowerInvariant();
        var origin = ValidOrigin(shopOrigin) ?? ValidOrigin(Request.Headers.Referer.ToString());
        var startUrl = $"{origin?.TrimEnd('/') ?? string.Empty}/{key}/";
        var shortName = store.Name.Trim();
        if (shortName.Length > 12) shortName = shortName[..12].TrimEnd();
        var manifest = new Dictionary<string, object?>
        {
            ["name"] = store.Name,
            ["short_name"] = shortName,
            ["description"] = store.Tagline,
            ["start_url"] = startUrl,
            ["scope"] = startUrl,
            ["display"] = "standalone",
            ["orientation"] = "portrait-primary",
            ["background_color"] = "#f6f7f4",
            ["theme_color"] = store.AccentColor,
            ["icons"] = store.LogoUrl is null ? Array.Empty<object>() : new object[]
            {
                new { src = $"/api/store/{Uri.EscapeDataString(key)}/pwa/icon/192", sizes = "192x192", type = "image/webp", purpose = "any maskable" },
                new { src = $"/api/store/{Uri.EscapeDataString(key)}/pwa/icon/512", sizes = "512x512", type = "image/webp", purpose = "any maskable" }
            }
        };
        return new JsonResult(manifest) { ContentType = "application/manifest+json" };
    }

    [HttpGet("pwa/icon/{size:int}")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetPwaIcon(string storeKey, int size, CancellationToken token)
        => ToImage(await storefront.GetPwaIconAsync(storeKey, size, token));
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

    [HttpGet("presentation/categories/all")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetAllCategoryImage(string storeKey, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, "category-all", null, token));

    [HttpPost("customer-auth/otp/request")]
    [EnableRateLimiting("StorefrontOtpRequest")]
    public async Task<IActionResult> RequestCustomerOtp(string storeKey, StorefrontOtpRequest input, CancellationToken token)
    {
        try
        {
            var challenge = await customerAuth.RequestOtpAsync(storeKey, input, token);
            return challenge is null ? BadRequest(new { message = "Enter a valid mobile number." }) : Accepted(challenge);
        }
        catch (BusinessRuleException exception)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = exception.Message });
        }
    }

    [HttpPost("customer-auth/otp/verify")]
    [EnableRateLimiting("StorefrontOtpVerify")]
    public async Task<ActionResult<StorefrontAuthenticationDto>> VerifyCustomerOtp(string storeKey, StorefrontOtpVerifyInput input, CancellationToken token)
    {
        try
        {
            var result = await customerAuth.VerifyOtpAsync(storeKey, input, token);
            return result is null ? Unauthorized(new { message = "The verification code is invalid or expired." }) : Ok(result);
        }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPut("session/profile")]
    [EnableRateLimiting("StorefrontOtpVerify")]
    public async Task<ActionResult<StorefrontCustomerDto>> UpdateCustomerProfile(string storeKey, UpdateStorefrontCustomerInput input, CancellationToken token)
    {
        try
        {
            var result = await customerAuth.UpdateProfileAsync(storeKey, CustomerSessionToken(), input, token);
            return result is null ? Unauthorized() : Ok(result);
        }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }
    [HttpGet("session")]
    public async Task<ActionResult<StorefrontCustomerDto>> GetSession(string storeKey, CancellationToken token)
    {
        var session = await customers.GetSessionAsync(storeKey, CustomerSessionToken(), token);
        return session is null ? Unauthorized() : Ok(session);
    }
    [HttpPost("session/profile-image")]
    [RequestSizeLimit(MaxProfileImageRequestBytes)]
    public async Task<ActionResult<StorefrontCustomerDto>> UploadProfileImage(string storeKey, [FromForm(Name = "file")] IFormFile file, CancellationToken token)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Choose an image to upload." });
        if (file.Length > MaxProfileImageBytes) return BadRequest(new { message = "Choose an image up to 5 MB." });
        try { await using var stream = file.OpenReadStream(); var result = await customers.UploadProfileImageAsync(storeKey, CustomerSessionToken(), file.FileName, stream, token); return result is null ? Unauthorized() : Ok(result); }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
        catch (Exception exception) { StorefrontControllerLogs.ProfileUploadFailed(logger, exception, storeKey); return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Profile image upload failed."); }
    }
    [HttpDelete("session/profile-image")]
    public async Task<ActionResult<StorefrontCustomerDto>> RemoveProfileImage(string storeKey, CancellationToken token)
    { var result = await customers.RemoveProfileImageAsync(storeKey, CustomerSessionToken(), token); return result is null ? Unauthorized() : Ok(result); }
    [HttpGet("session/profile-image")]
    public async Task<IActionResult> GetProfileImage(string storeKey, CancellationToken token)
    { var image = await customers.GetProfileImageAsync(storeKey, CustomerSessionToken(), token); return image is null ? Unauthorized() : File(image.Content, image.ContentType); }
    [HttpGet("addresses")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontCustomerAddressDto>>> GetAddresses(string storeKey, CancellationToken token)
    { var result = await customers.GetAddressesAsync(storeKey, CustomerSessionToken(), token); return result is null ? Unauthorized() : Ok(result); }
    [HttpPost("addresses")]
    public async Task<ActionResult<StorefrontCustomerAddressDto>> AddAddress(string storeKey, StorefrontCustomerAddressInput input, CancellationToken token)
    { try { var result = await customers.SaveAddressAsync(storeKey, CustomerSessionToken(), null, input, token); return result is null ? Unauthorized() : Ok(result); } catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); } }
    [HttpPut("addresses/{addressId:guid}")]
    public async Task<ActionResult<StorefrontCustomerAddressDto>> UpdateAddress(string storeKey, Guid addressId, StorefrontCustomerAddressInput input, CancellationToken token)
    { try { var result = await customers.SaveAddressAsync(storeKey, CustomerSessionToken(), addressId, input, token); return result is null ? NotFound() : Ok(result); } catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); } }
    [HttpDelete("addresses/{addressId:guid}")]
    public async Task<IActionResult> DeleteAddress(string storeKey, Guid addressId, CancellationToken token) => await customers.DeleteAddressAsync(storeKey, CustomerSessionToken(), addressId, token) ? NoContent() : NotFound();
    [HttpPost("addresses/{addressId:guid}/default")]
    public async Task<IActionResult> SetDefaultAddress(string storeKey, Guid addressId, CancellationToken token) => await customers.SetDefaultAddressAsync(storeKey, CustomerSessionToken(), addressId, token) ? NoContent() : NotFound();

    [HttpGet("orders")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontCustomerOrderDto>>> GetOrders(string storeKey, CancellationToken token)
    {
        var orders = await customers.GetOrdersAsync(storeKey, CustomerSessionToken(), token);
        return orders is null ? Unauthorized() : Ok(orders);
    }

    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult<StorefrontCustomerOrderDto>> GetOrder(string storeKey, Guid orderId, CancellationToken token)
    {
        var order = await customers.GetOrderAsync(storeKey, CustomerSessionToken(), orderId, token);
        if (order is null) return NotFound();
        var cancellation = await cancellations.GetCustomerStatusAsync(storeKey, CustomerSessionToken(), orderId, token);
        return Ok(order with { Cancellation = cancellation });
    }

    [HttpPost("orders/{orderId:guid}/cancellation-request")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCancellationDto>> RequestCancellation(
        string storeKey, Guid orderId, StorefrontCancellationRequestInput input, CancellationToken token)
    {
        try
        {
            var result = await cancellations.RequestAsync(storeKey, CustomerSessionToken(), orderId, input.Reason, token);
            return result is null ? Unauthorized() : Ok(result);
        }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }
    [HttpPost("cart/quote")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCartQuoteDto>> Quote(string storeKey, StorefrontCartQuoteInput input, CancellationToken token)
    {
        try
        {
            var quote = await checkout.QuoteAsync(storeKey, input, CustomerSessionToken(), token);
            return quote is null ? NotFound() : Ok(quote);
        }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }
    [HttpGet("wishlist")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontProductDto>>> GetWishlist(string storeKey, CancellationToken token)
    {
        var products = await customers.GetWishlistAsync(storeKey, CustomerSessionToken(), token);
        return products is null ? Unauthorized() : Ok(products);
    }

    [HttpPut("wishlist/{productId:guid}")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<IActionResult> AddWishlist(string storeKey, Guid productId, CancellationToken token)
        => await customers.AddWishlistAsync(storeKey, CustomerSessionToken(), productId, token) ? NoContent() : Unauthorized();

    [HttpDelete("wishlist/{productId:guid}")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<IActionResult> RemoveWishlist(string storeKey, Guid productId, CancellationToken token)
        => await customers.RemoveWishlistAsync(storeKey, CustomerSessionToken(), productId, token) ? NoContent() : Unauthorized();

    [HttpPost("wishlist/merge")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<IActionResult> MergeWishlist(string storeKey, StorefrontWishlistInput input, CancellationToken token)
        => await customers.MergeWishlistAsync(storeKey, CustomerSessionToken(), input.ProductIds ?? [], token) ? NoContent() : Unauthorized();
    [HttpPost("checkout")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCheckoutResult>> CheckoutWithSelectedMethod(
        string storeKey, StorefrontCheckoutInput input, CancellationToken token) => await CheckoutCore(storeKey, input, token);

    [HttpPost("checkout/razorpay")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCheckoutResult>> Checkout(
        string storeKey, StorefrontCheckoutInput input, CancellationToken token)
        => await CheckoutCore(storeKey, input with { PaymentMethod = string.IsNullOrWhiteSpace(input.PaymentMethod) ? "UPI" : input.PaymentMethod }, token);

    private async Task<ActionResult<StorefrontCheckoutResult>> CheckoutCore(string storeKey, StorefrontCheckoutInput input, CancellationToken token)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (!Guid.TryParse(idempotencyKey, out var parsed) || parsed == Guid.Empty)
            return BadRequest(new { message = "A valid Idempotency-Key header is required." });
        try { return Ok(await checkout.CheckoutAsync(storeKey, input, parsed.ToString("D"), CustomerSessionToken(), token)); }
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

    private string CustomerSessionToken() => Request.Headers["X-Customer-Session"].ToString();

    private static string? ValidOrigin(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
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
    [LoggerMessage(3403, LogLevel.Error, "Storefront customer profile image upload failed for store {StoreKey}.")]
    public static partial void ProfileUploadFailed(ILogger logger, Exception exception, string storeKey);
}
