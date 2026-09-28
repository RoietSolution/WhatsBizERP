using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/storefront-administration")]
[HasPermission(Permissions.Admin.Settings)]
public sealed class StorefrontAdministrationController(IStorefrontAdministrationService service) : ControllerBase
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    [HttpGet] public Task<StorefrontAdminDto> Get(CancellationToken token) => service.GetAsync(token);
    [HttpPut("review-settings")] public async Task<IActionResult> UpdateReviewSettings(UpdateStorefrontReviewSettingsInput input, CancellationToken token) { await service.UpdateReviewSettingsAsync(input, token); return NoContent(); }
    [HttpPut("delivery-settings")] public async Task<IActionResult> UpdateDeliverySettings(UpdateStorefrontDeliverySettingsInput input, CancellationToken token) { await service.UpdateDeliverySettingsAsync(input, token); return NoContent(); }
    [HttpPut("pincodes")]
    public async Task<IActionResult> SavePincode(StorefrontPincodeDto input, CancellationToken token) { await service.SavePincodeAsync(input, token); return NoContent(); }
    [HttpDelete("pincodes/{pincode}")]
    public async Task<IActionResult> RemovePincode(string pincode, CancellationToken token) { await service.RemovePincodeAsync(pincode, token); return NoContent(); }
    [HttpPost("promotions")]
    public Task<StorefrontPromotionDto> CreatePromotion(SaveStorefrontPromotionInput input, CancellationToken token) => service.SavePromotionAsync(null, input, token);
    [HttpPut("promotions/{id:guid}")]
    public Task<StorefrontPromotionDto> UpdatePromotion(Guid id, SaveStorefrontPromotionInput input, CancellationToken token) => service.SavePromotionAsync(id, input, token);
    [HttpDelete("promotions/{id:guid}")]
    public async Task<IActionResult> RemovePromotion(Guid id, CancellationToken token) { await service.RemovePromotionAsync(id, token); return NoContent(); }    [HttpPut("banners/{slot}")] public async Task<IActionResult> UpdateBanner(string slot, UpdateStorefrontBannerInput input, CancellationToken token) { await service.UpdateBannerAsync(slot, input, token); return NoContent(); }
    [HttpGet("logo")] public async Task<IActionResult> Logo(CancellationToken token) => ToImage(await service.GetImageAsync("logo", null, token));
    [HttpPost("logo")] public Task<IActionResult> UploadLogo(IFormFile file, CancellationToken token) => Upload("logo", null, file, token);
    [HttpDelete("logo")] public async Task<IActionResult> RemoveLogo(CancellationToken token) { await service.RemoveImageAsync("logo", null, token); return NoContent(); }
    [HttpGet("categories/all/image")] public async Task<IActionResult> AllCategoryImage(CancellationToken token) => ToImage(await service.GetImageAsync("category-all", null, token));
    [HttpPost("categories/all/image")] public Task<IActionResult> UploadAllCategoryImage(IFormFile file, CancellationToken token) => Upload("category-all", null, file, token);
    [HttpDelete("categories/all/image")] public async Task<IActionResult> RemoveAllCategoryImage(CancellationToken token) { await service.RemoveImageAsync("category-all", null, token); return NoContent(); }
    [HttpGet("banners/{slot}/image")] public async Task<IActionResult> BannerImage(string slot, CancellationToken token) => ToImage(await service.GetImageAsync($"banner-{slot.ToLowerInvariant()}", null, token));
    [HttpPost("banners/{slot}/image")] public Task<IActionResult> UploadBanner(string slot, IFormFile file, CancellationToken token) => Upload($"banner-{slot.ToLowerInvariant()}", null, file, token);
    [HttpDelete("banners/{slot}/image")] public async Task<IActionResult> RemoveBanner(string slot, CancellationToken token) { await service.RemoveImageAsync($"banner-{slot.ToLowerInvariant()}", null, token); return NoContent(); }
    [HttpGet("categories/{categoryId:guid}/image")] public async Task<IActionResult> CategoryImage(Guid categoryId, CancellationToken token) => ToImage(await service.GetImageAsync("category", categoryId, token));
    [HttpPost("categories/{categoryId:guid}/image")] public Task<IActionResult> UploadCategory(Guid categoryId, IFormFile file, CancellationToken token) => Upload("category", categoryId, file, token);
    [HttpDelete("categories/{categoryId:guid}/image")] public async Task<IActionResult> RemoveCategory(Guid categoryId, CancellationToken token) { await service.RemoveImageAsync("category", categoryId, token); return NoContent(); }
    private async Task<IActionResult> Upload(string resource, Guid? categoryId, IFormFile file, CancellationToken token)
    {
        if (file.Length is <= 0 or > MaxImageBytes) return BadRequest(new { message = "Choose an image up to 5 MB." });
        await using var stream = file.OpenReadStream(); await service.UploadImageAsync(resource, categoryId, file.FileName, stream, token); return NoContent();
    }
    private IActionResult ToImage(StorefrontImage? image) => image is null ? NotFound() : File(image.Content, image.ContentType);
}
