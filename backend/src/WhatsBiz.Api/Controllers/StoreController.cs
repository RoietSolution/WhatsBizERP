using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Api.Configuration;

namespace WhatsBiz.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/store/{storeKey}")]
public sealed partial class StoreController(IStorefrontService storefront, IStorefrontCheckoutService checkout, IStorefrontAvailabilityService availability, IStorefrontCustomerService customers, IStorefrontCustomerRewardsService rewards, IStorefrontCustomerAuthenticationService customerAuth, IStorefrontReviewService reviews, IStorefrontActivityService activity, ICustomerDemandService demand, IStorefrontCancellationService cancellations, IStorefrontReturnService returns, IStorefrontFeedbackService feedback, ILogger<StoreController> logger) : ControllerBase
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
    public async Task<ActionResult<StorefrontProductPageDto>> GetProducts(string storeKey, [FromQuery] int page = 1, [FromQuery] int pageSize = 24,
        [FromQuery] Guid? categoryId = null, [FromQuery] decimal? minPrice = null, [FromQuery] decimal? maxPrice = null,
        [FromQuery] bool inStockOnly = false, [FromQuery] string sortBy = "RECOMMENDED", CancellationToken token = default)
    {
        if (minPrice is < 0 or > 1_000_000_000m || maxPrice is < 0 or > 1_000_000_000m || (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice))
            return BadRequest(new { message = "Enter a valid non-negative product price range." });
        sortBy = (sortBy ?? "RECOMMENDED").Trim().ToUpperInvariant();
        if (sortBy is not ("RECOMMENDED" or "PRICE_LOW_TO_HIGH" or "PRICE_HIGH_TO_LOW" or "NEWEST"))
            return BadRequest(new { message = "The requested product sort is not supported." });
        page = Math.Clamp(page, 1, 100000); pageSize = Math.Clamp(pageSize, 1, 50);
        var products = await storefront.GetProductsAsync(storeKey, page, pageSize, new StorefrontProductQuery(categoryId, minPrice, maxPrice, inStockOnly, sortBy), token);
        return products is null ? NotFound() : Ok(products);
    }
    [HttpGet("availability-announcements")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontAvailabilityAnnouncementDto>>> AvailabilityAnnouncements(string storeKey, CancellationToken token)
    {
        var store = await storefront.GetStoreAsync(storeKey, token);
        if (store is null) return NotFound();
        var tenantId = await availability.ResolveTenantIdAsync(storeKey, token);
        return tenantId is null ? NotFound() : Ok(await availability.GetAnnouncementsAsync(tenantId.Value, token));
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
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetProductImage(string storeKey, Guid productId, [FromQuery] Guid? imageId, [FromQuery] bool thumbnail, CancellationToken token)
    {
        var image = await storefront.GetProductImageAsync(storeKey, productId, imageId, thumbnail, token);
        return image is null ? NotFound() : File(image.Content, image.ContentType);
    }

    [HttpGet("pwa/manifest.webmanifest")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetPwaManifest(string storeKey, [FromQuery] string? shopOrigin, CancellationToken token)
    {
        var store = await storefront.GetStoreAsync(storeKey, token);
        if (store is null) return NotFound();
        var key = store.StoreKey.ToLowerInvariant();
        var origin = ValidOrigin(shopOrigin) ?? ValidOrigin(Request.Headers.Referer.ToString());
        var mappedStoreKey = StorefrontDomainMappings.ResolveStoreKey(HttpContext.RequestServices.GetRequiredService<IConfiguration>(), origin);
        var customDomain = mappedStoreKey is not null && string.Equals(mappedStoreKey, key, StringComparison.OrdinalIgnoreCase);
        var startUrl = customDomain ? $"{origin!.TrimEnd('/')}/" : $"{origin?.TrimEnd('/') ?? string.Empty}/{key}/";
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
                new { src = $"/api/store/{Uri.EscapeDataString(key)}/pwa/icon/192", sizes = "192x192", type = "image/webp", purpose = "any" },
                new { src = $"/api/store/{Uri.EscapeDataString(key)}/pwa/icon/512", sizes = "512x512", type = "image/webp", purpose = "any" },
                new { src = $"/api/store/{Uri.EscapeDataString(key)}/pwa/icon/192", sizes = "192x192", type = "image/webp", purpose = "maskable" },
                new { src = $"/api/store/{Uri.EscapeDataString(key)}/pwa/icon/512", sizes = "512x512", type = "image/webp", purpose = "maskable" }
            }
        };
        return new JsonResult(manifest) { ContentType = "application/manifest+json" };
    }

    [HttpGet("pwa/icon/{size:int}")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetPwaIcon(string storeKey, int size, CancellationToken token)
        => ToImage(await storefront.GetPwaIconAsync(storeKey, size, token));
    [HttpGet("presentation/logo")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetLogo(string storeKey, [FromQuery] bool thumbnail, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, "logo", null, thumbnail, token));

    [HttpGet("presentation/banners/{slot}")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBanner(string storeKey, string slot, [FromQuery] bool thumbnail, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, $"banner-{slot}", null, thumbnail, token));

    [HttpGet("presentation/categories/{categoryId:guid}")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCategoryImage(string storeKey, Guid categoryId, [FromQuery] bool thumbnail, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, "category", categoryId, thumbnail, token));

    [HttpGet("presentation/categories/all")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetAllCategoryImage(string storeKey, [FromQuery] bool thumbnail, CancellationToken token)
        => ToImage(await storefront.GetPresentationImageAsync(storeKey, "category-all", null, thumbnail, token));

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

    [HttpPost("customer-auth/password/signin")]
    [EnableRateLimiting("StorefrontOtpVerify")]
    public async Task<ActionResult<StorefrontAuthenticationDto>> SignInWithPassword(string storeKey, StorefrontPasswordSignInInput input, CancellationToken token)
    {
        var result = await customerAuth.SignInPasswordAsync(storeKey, input, token);
        return result is null ? Unauthorized(new { message = "The mobile number or password is incorrect." }) : Ok(result);
    }

    [HttpPost("customer-auth/password/reset")]
    [EnableRateLimiting("StorefrontOtpVerify")]
    public async Task<IActionResult> ResetPassword(string storeKey, StorefrontPasswordResetInput input, CancellationToken token)
    {
        try { return await customerAuth.ResetPasswordAsync(storeKey, input, token) ? NoContent() : Unauthorized(new { message = "The verification code is invalid or expired." }); }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPost("session/password/change")]
    [EnableRateLimiting("StorefrontOtpVerify")]
    public async Task<IActionResult> ChangePassword(string storeKey, StorefrontChangePasswordInput input, CancellationToken token)
    {
        try { return await customerAuth.ChangePasswordAsync(storeKey, CustomerSessionToken(), input, token) ? NoContent() : Unauthorized(new { message = "The current password is incorrect." }); }
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
    [HttpPost("activity")]
    public async Task<IActionResult> Activity(string storeKey, StorefrontActivityInput input, CancellationToken token) { await activity.RecordAsync(storeKey, CustomerSessionTokenOrNull(), input, token); return NoContent(); }
    [HttpPost("feedback"), EnableRateLimiting("StorefrontCheckout")]
    public async Task<IActionResult> SubmitFeedback(string storeKey, StorefrontFeedbackInput input, CancellationToken token)
    { try { return Ok(await feedback.SubmitAsync(storeKey, CustomerSessionTokenOrNull(), input, token)); } catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); } }
    [HttpGet("feedback")]
    public async Task<IActionResult> MyFeedback(string storeKey, CancellationToken token)
    { var result = await feedback.CustomerTicketsAsync(storeKey, CustomerSessionToken(), token); return result is null ? Unauthorized() : Ok(result); }
    [HttpGet("feedback/{ticketId:guid}")]
    public async Task<IActionResult> MyFeedbackTicket(string storeKey, Guid ticketId, CancellationToken token)
    { var result = await feedback.CustomerTicketAsync(storeKey, CustomerSessionToken(), ticketId, token); return result is null ? NotFound() : Ok(result); }
    [HttpGet("feedback/notifications")]
    public async Task<IActionResult> MyFeedbackNotifications(string storeKey, CancellationToken token)
    { var customer=await customers.GetSessionAsync(storeKey,CustomerSessionToken(),token); var tenant=await feedback.ResolveTenantAsync(storeKey,token); if(customer is null||tenant is null)return Unauthorized(); return Ok(await feedback.NotificationsAsync(tenant.Value,customer.Id,token)); }
    [HttpPost("feedback/notifications/{notificationId:guid}/read")]
    public async Task<IActionResult> ReadFeedbackNotification(string storeKey, Guid notificationId, CancellationToken token)
    { var customer=await customers.GetSessionAsync(storeKey,CustomerSessionToken(),token); var tenant=await feedback.ResolveTenantAsync(storeKey,token); if(customer is null||tenant is null)return Unauthorized(); return await feedback.MarkNotificationReadAsync(tenant.Value,notificationId,customer.Id,token)?NoContent():NotFound(); }
    [HttpGet("feedback/{ticketId:guid}/attachments")]
    public async Task<IActionResult> FeedbackAttachments(string storeKey, Guid ticketId, CancellationToken token)
    { var customer=await customers.GetSessionAsync(storeKey,CustomerSessionToken(),token); var tenant=await feedback.ResolveTenantAsync(storeKey,token); if(customer is null||tenant is null)return Unauthorized(); return Ok(await feedback.AttachmentsAsync(tenant.Value,ticketId,customer.Id,false,token)); }
    [HttpPost("feedback/{ticketId:guid}/attachments"), RequestSizeLimit(16 * 1024 * 1024), Consumes("multipart/form-data")]
    public async Task<ActionResult<StorefrontFeedbackAttachmentDto>> AddFeedbackAttachment(string storeKey, Guid ticketId, IFormFile file, CancellationToken token)
    { if(file is null)return BadRequest(new{message="Choose an attachment."}); var customer=await customers.GetSessionAsync(storeKey,CustomerSessionToken(),token); var tenant=await feedback.ResolveTenantAsync(storeKey,token); if(customer is null||tenant is null)return Unauthorized(); await using var stream=file.OpenReadStream(); using var memory=new MemoryStream(); await stream.CopyToAsync(memory,token); try{return Ok(await feedback.AddAttachmentAsync(tenant.Value,ticketId,customer.Id,false,file.FileName,file.ContentType,memory.ToArray(),token));}catch(BusinessRuleException ex){return BadRequest(new{message=ex.Message});} }
    [HttpGet("feedback/{ticketId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DownloadFeedbackAttachment(string storeKey, Guid ticketId, Guid attachmentId, CancellationToken token)
    { var customer=await customers.GetSessionAsync(storeKey,CustomerSessionToken(),token); var tenant=await feedback.ResolveTenantAsync(storeKey,token); if(customer is null||tenant is null)return Unauthorized(); var file=await feedback.ReadAttachmentAsync(tenant.Value,ticketId,attachmentId,customer.Id,false,token); if(file is null)return NotFound(); Response.Headers["X-Content-Type-Options"]="nosniff"; return File(file.Content,file.ContentType,file.FileName,enableRangeProcessing:false); }
    [HttpPost("search-events")]
    public async Task<CustomerDemandSummaryDto> SearchEvent(string storeKey, StorefrontSearchEventInput input, CancellationToken token) => await demand.RecordSearchAsync(storeKey, CustomerSessionTokenOrNull(), input, token);
    [HttpPost("products/{productId:guid}/availability-request")]
    public Task<StorefrontAvailabilityRequestDto> Availability(string storeKey, Guid productId, StorefrontAvailabilityRequestInput input, CancellationToken token) => demand.RequestAvailabilityAsync(storeKey, productId, CustomerSessionTokenOrNull(), input, token);
    [HttpGet("products/{productId:guid}/availability-request")]
    public async Task<ActionResult<StorefrontAvailabilityRequestDto>> AvailabilityStatus(string storeKey, Guid productId, CancellationToken token)
    { var tokenValue=CustomerSessionTokenOrNull(); if (tokenValue is null) return NotFound(); var result=await demand.GetAvailabilityAsync(storeKey, productId, tokenValue, token); return result is null ? NotFound() : Ok(result); }
    [HttpDelete("products/{productId:guid}/availability-request")]
    public async Task<IActionResult> CancelAvailability(string storeKey, Guid productId, CancellationToken token) { var tokenValue=CustomerSessionTokenOrNull(); if (tokenValue is null) return Unauthorized(); return (await demand.CancelAvailabilityAsync(storeKey, productId, tokenValue, token)) ? NoContent() : NotFound(); }
    [HttpPost("missing-product-requests")]
    public Task<CustomerDemandMissingDto> MissingProduct(string storeKey, StorefrontMissingProductRequestInput input, CancellationToken token) => demand.RequestMissingProductAsync(storeKey, CustomerSessionTokenOrNull(), input, token);
    [HttpGet("session")]
    public async Task<ActionResult<StorefrontCustomerDto>> GetSession(string storeKey, CancellationToken token)
    {
        var session = await customers.GetSessionAsync(storeKey, CustomerSessionToken(), token);
        return session is null ? Unauthorized() : Ok(session);
    }
    [HttpGet("rewards")]
    public async Task<ActionResult<StorefrontCustomerRewardsDto>> GetRewards(string storeKey, [FromQuery] int take = 100, CancellationToken token = default)
    {
        var result = await rewards.GetRewardsAsync(storeKey, CustomerSessionToken(), take, token);
        return result is null ? Unauthorized() : Ok(result);
    }
    [HttpPost("customer-referral")]
    public async Task<IActionResult> CaptureCustomerReferral(string storeKey, StorefrontReferralCaptureInput input, CancellationToken token)
    {
        try { return await rewards.CaptureReferralAsync(storeKey, CustomerSessionToken(), input, token) ? NoContent() : Unauthorized(); }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }
    [HttpPost("session/profile-image"), Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxProfileImageRequestBytes)]
    public async Task<ActionResult<StorefrontCustomerDto>> UploadProfileImage(string storeKey, IFormFile file, CancellationToken token)
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
        var returnRequests = await returns.CustomerRequestsAsync(storeKey, CustomerSessionToken(), orderId, token);
        return Ok(order with { Cancellation = cancellation, ReturnRequests = returnRequests ?? Array.Empty<StorefrontReturnRequestDto>() });
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
    [HttpPost("orders/{orderId:guid}/items/{invoiceItemId:guid}/return-request")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontReturnRequestDto>> RequestReturn(string storeKey, Guid orderId, Guid invoiceItemId, StorefrontReturnRequestInput input, CancellationToken token)
    { try { return Ok(await returns.RequestAsync(storeKey, CustomerSessionToken(), orderId, invoiceItemId, input, token)); } catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); } }

    [HttpGet("orders/{orderId:guid}/return-requests")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontReturnRequestDto>>> CustomerReturnRequests(string storeKey, Guid orderId, CancellationToken token)
    { var result=await returns.CustomerRequestsAsync(storeKey,CustomerSessionToken(),orderId,token);return result is null?Unauthorized():Ok(result); }
    [HttpGet("delivery/availability")]
    public async Task<ActionResult<StorefrontDeliveryAvailabilityDto>> CheckDelivery(string storeKey, [FromQuery] string pincode, CancellationToken token)
    {
        try { var result = await checkout.CheckDeliveryAsync(storeKey, pincode, token); return result is null ? NotFound() : Ok(result); }
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
    [HttpPost("cart/preview")]
    [AllowAnonymous]
    [EnableRateLimiting("StorefrontCartPreview")]
    public async Task<ActionResult<StorefrontCartQuoteDto>> CartPreview(string storeKey, StorefrontCartQuoteInput input, CancellationToken token)
    {
        try
        {
            var preview = await checkout.QuoteAsync(storeKey, input with { Pincode = null }, CustomerSessionToken(), token);
            return preview is null ? NotFound() : Ok(preview);
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

    [HttpGet("orders/{orderId:guid}/payment-status")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontPaymentReturnDto>> PaymentStatus(string storeKey, Guid orderId, CancellationToken token)
    {
        var result = await checkout.GetPaymentStatusAsync(storeKey, orderId, CustomerSessionToken(), token);
        return result is null ? Unauthorized() : Ok(result);
    }

    [HttpPost("orders/{orderId:guid}/payment-retry")]
    [EnableRateLimiting("StorefrontCheckout")]
    public async Task<ActionResult<StorefrontCheckoutResult>> RetryPayment(string storeKey, Guid orderId, CancellationToken token)
    {
        var result = await checkout.RetryPaymentAsync(storeKey, orderId, CustomerSessionToken(), token);
        return result is null ? Unauthorized() : Ok(result);
    }

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

    private string? CustomerSessionTokenOrNull() => Request.Headers["X-Customer-Session"].FirstOrDefault();
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
        "The order total has changed. Please review the updated amount and confirm again." => message,
        _ when message.StartsWith("Ordering is closed for this product.", StringComparison.Ordinal) => "Ordering is closed for one or more products. Check the next booking window or remove these products to continue.",
        "Ordering availability could not be verified. Please contact the store about its configured timezone." => message,
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
