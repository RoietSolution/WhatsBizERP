using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FluentValidation;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Infrastructure.Storefront;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Features;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/public")]
public sealed class PublicMarketplaceController(PublicMarketplaceService marketplace) : ControllerBase
{
    [HttpGet("routes/{slug}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Resolve(string slug, CancellationToken ct)
    {
        var result = await marketplace.Resolve(slug, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("cities/{slug}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> City(string slug, CancellationToken ct)
    {
        var result = await marketplace.CityBySlug(slug, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("cities/{slug}/products")]
    [AllowAnonymous]
    public async Task<IActionResult> Products(string slug,[FromQuery]int page=1,[FromQuery]int pageSize=24,[FromQuery]string? search=null,[FromQuery]string? category=null,[FromQuery]string? brand=null,[FromQuery]Guid? searchEventId=null,CancellationToken ct=default)
    {
        try { return Ok(await marketplace.CityProducts(slug,page,pageSize,search,category,brand,searchEventId,ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}

[ApiController, Authorize, HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.CityMarketplace), Route("api/marketplace/retailer/cities")]
public sealed class RetailerMarketplaceController(PublicMarketplaceService marketplace, WhatsBiz.Application.Common.Interfaces.ICurrentUserService user) : ControllerBase
{
    [HttpGet]
    public Task<object[]> List(CancellationToken ct) => marketplace.RetailerCities(ct);

    [HttpPut]
    public async Task<IActionResult> Save(RetailerCityInput input,CancellationToken ct)
    {
        await marketplace.SaveRetailerCity(input,user.UserId?.ToString()??"retailer",ct);
        return Ok(await marketplace.RetailerCities(ct));
    }
}

[ApiController, PlatformAuthorize, HasPermission(Permissions.Features.Manage), Route("api/marketplace/owner/cities")]
public sealed class MarketplaceCityOwnerController(PublicMarketplaceService marketplace, WhatsBiz.Application.Common.Interfaces.ICurrentUserService user) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<object>> List(CancellationToken ct) => marketplace.OwnerCities(ct);

    [HttpPost]
    public async Task<IActionResult> Create(CityInput input, CancellationToken ct)
    {
        try { return Ok(await marketplace.SaveCity(null, input, user.UserId?.ToString() ?? "owner", ct)); }
        catch (ValidationException exception) { return CityValidationProblem(exception); }
    }

    [HttpPut("{cityId:guid}")]
    public async Task<IActionResult> Update(Guid cityId, CityInput input, CancellationToken ct)
    {
        try { return Ok(await marketplace.SaveCity(cityId, input, user.UserId?.ToString() ?? "owner", ct)); }
        catch (ValidationException exception) { return CityValidationProblem(exception); }
    }

    [HttpGet("{cityId:guid}/retailers")]
    public Task<object[]> Retailers(Guid cityId,CancellationToken ct)=>marketplace.OwnerCityRetailers(cityId,ct);

    [HttpPut("{cityId:guid}/retailers/{tenantId:guid}")]
    public async Task<IActionResult> AssignRetailer(Guid cityId,Guid tenantId,OwnerCityRetailerInput input,CancellationToken ct)
    { await marketplace.SaveOwnerCityRetailer(cityId,tenantId,input.Assigned,input.Locality,input.ServiceArea,ct); return NoContent(); }

    private static BadRequestObjectResult CityValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).Distinct().ToArray());
        return new BadRequestObjectResult(new ValidationProblemDetails(errors)
        {
            Title = "City details need correction.",
            Status = StatusCodes.Status400BadRequest
        });
    }
}

[ApiController,Route("api/marketplace/owner/cities/{cityId:guid}/media")]
public sealed class MarketplaceCityMediaOwnerController(PublicMarketplaceService marketplace):ControllerBase
{
    private const long MaxUploadBytes=5*1024*1024;
    [HttpPost("{kind}"),PlatformAuthorize,HasPermission(Permissions.Features.Manage),RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload(Guid cityId,string kind,[FromForm]IFormFile file,CancellationToken ct)
    {if(kind is not ("logo" or "banner"))return BadRequest(new{message="Choose logo or banner media."});if(file is null||file.Length is <=0 or >MaxUploadBytes)return BadRequest(new{message="Choose a JPEG, PNG or WebP image up to 5 MB."});await using var stream=file.OpenReadStream();await marketplace.UploadCityMedia(cityId,kind,file.FileName,stream,ct);return NoContent();}
    [HttpDelete("{kind}"),PlatformAuthorize,HasPermission(Permissions.Features.Manage)]
    public async Task<IActionResult> Remove(Guid cityId,string kind,CancellationToken ct){if(kind is not ("logo" or "banner"))return BadRequest(new{message="Choose logo or banner media."});await marketplace.RemoveCityMedia(cityId,kind,ct);return NoContent();}
    [HttpPost("categories/{categoryToken}"),PlatformAuthorize,HasPermission(Permissions.Features.Manage),RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> UploadCategory(Guid cityId,string categoryToken,[FromForm]IFormFile file,CancellationToken ct){if(file is null||file.Length is <=0 or >MaxUploadBytes)return BadRequest(new{message="Choose an image up to 5 MB."});await using var stream=file.OpenReadStream();await marketplace.UploadCityCategoryMedia(cityId,categoryToken,file.FileName,stream,ct);return NoContent();}
    [HttpDelete("categories/{categoryToken}"),PlatformAuthorize,HasPermission(Permissions.Features.Manage)]
    public async Task<IActionResult> RemoveCategory(Guid cityId,string categoryToken,CancellationToken ct){await marketplace.RemoveCityCategoryMedia(cityId,categoryToken,ct);return NoContent();}
}

[ApiController,AllowAnonymous,Route("api/public/cities/{slug}/media")]
public sealed class PublicCityMediaController(PublicMarketplaceService marketplace):ControllerBase
{
    [HttpGet("{kind}"),ResponseCache(Duration=300,Location=ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(string slug,string kind,CancellationToken ct){if(kind is not ("logo" or "banner"))return NotFound();var image=await marketplace.PublicCityMedia(slug,kind,ct);return image is null?NotFound():File(image.Content,image.ContentType);}
    [HttpGet("categories/{categoryToken}"),ResponseCache(Duration=300,Location=ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCategory(string slug,string categoryToken,CancellationToken ct){var image=await marketplace.PublicCityMedia(slug,"CATEGORY",categoryToken,ct);return image is null?NotFound():File(image.Content,image.ContentType);}
}

public sealed record OwnerCityRetailerInput(bool Assigned,string? Locality,string? ServiceArea);

[ApiController, Authorize, HasPermission(Permissions.Admin.Settings), RequireFeature(WhatsBiz.Application.Common.Features.FeatureKeys.PartnerSellerManagement), Route("api/partner-sellers")]
public sealed class PartnerSellerController(PartnerSellerService sellers) : ControllerBase
{
    [HttpGet]
    public Task<object[]> List(CancellationToken ct) => sellers.List(ct);

    [HttpGet("eligible")]
    public Task<object[]> Eligible(CancellationToken ct)=>sellers.EligibleForProducts(ct);

    [HttpGet("{sellerId:guid}/history")]
    public Task<object[]> History(Guid sellerId,CancellationToken ct)=>sellers.History(sellerId,ct);

    [HttpPost]
    public async Task<IActionResult> Create(PartnerSellerInput input, CancellationToken ct) => Ok(await sellers.Create(input, ct));

    [HttpPut("{sellerId:guid}")]
    public async Task<IActionResult> Update(Guid sellerId,PartnerSellerInput input,[FromQuery]string version,CancellationToken ct)=>Ok(await sellers.Update(sellerId,input,version,ct));

    [HttpPatch("{sellerId:guid}/status")]
    public async Task<IActionResult> Status(Guid sellerId,PartnerSellerStatusInput input,CancellationToken ct){await sellers.SetStatus(sellerId,input.Status,input.Version,ct);return NoContent();}

    [HttpGet("products/{productId:guid}")]
    public async Task<IActionResult> ProductListing(Guid productId,CancellationToken ct)=>Ok(await sellers.ProductListing(productId,ct));

}
public sealed record PartnerSellerStatusInput(string Status,string Version);

[ApiController, PlatformAuthorize, HasPermission(Permissions.Features.Manage), Route("api/partner-sellers/owner")]
public sealed class PartnerSellerOwnerController(PartnerSellerService sellers) : ControllerBase
{
    [HttpGet]
    public Task<object[]> List(CancellationToken ct) => sellers.OwnerList(ct);

    [HttpGet("listings")]
    public Task<object[]> Listings(CancellationToken ct)=>sellers.OwnerListingQueue(ct);

    [HttpGet("{tenantId:guid}/{sellerId:guid}/history")]
    public Task<object[]> History(Guid tenantId,Guid sellerId,CancellationToken ct)=>sellers.OwnerHistory(tenantId,sellerId,ct);

    [HttpPost("{tenantId:guid}/{productId:guid}/{sellerId:guid}/listing-decision")]
    public async Task<IActionResult> ListingDecision(Guid tenantId,Guid productId,Guid sellerId,PartnerSellerDecision input,CancellationToken ct){await sellers.SetListingDecision(tenantId,productId,sellerId,input.Status,input.Reason,input.Version,ct);return NoContent();}

    [HttpPost("{tenantId:guid}/{sellerId:guid}/decision")]
    public async Task<IActionResult> Decision(Guid tenantId, Guid sellerId, PartnerSellerDecision input, CancellationToken ct)
    {
        await sellers.SetOwnerDecision(tenantId,sellerId,input.Status,input.Reason,input.Version,ct);
        return NoContent();
    }
}
public sealed record PartnerSellerDecision(string Status,string Reason,string Version);

[ApiController, Route("api/store-directory")]
public sealed class StoreDirectoryController(PublicCatalogService catalog) : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public Task<object> Get(CancellationToken ct) => catalog.Directory(ct);
}

[ApiController, Authorize, Route("api/public-catalog")]
public sealed class PublicCatalogController(PublicCatalogService catalog) : ControllerBase
{
    [HttpGet("business-types"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.Administration)]
    public Task<object> BusinessTypes([FromServices] BusinessTypeCatalogService types,CancellationToken ct)=>types.List(false,ct);
    [HttpGet("directory"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.Administration)]
    public Task<object> Directory(CancellationToken ct) => catalog.DirectorySettings(catalog.Retailer,ct);
    [HttpPut("directory"), HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.Administration)]
    public async Task<IActionResult> Directory(DirectoryInput input,CancellationToken ct) {await catalog.SaveDirectory(catalog.Retailer,input,ct);return Ok(await catalog.DirectorySettings(catalog.Retailer,ct));}
    [HttpGet("products/{product:guid}"), HasPermission(Permissions.Product.View), RequireFeature(FeatureKeys.Products)]
    public Task<object> Product(Guid product,CancellationToken ct) => catalog.Details(catalog.Retailer,product,ct);
    [HttpPost("products/{product:guid}/submit"), HasPermission(Permissions.Product.Edit), RequireFeature(FeatureKeys.Products)]
    public async Task<IActionResult> Submit(Guid product,CatalogAction input,CancellationToken ct)
    {
        // Ignore any attempted reviewer action in a retailer request.
        await catalog.Decide(catalog.Retailer,product,input with {Action="SUBMIT"},ct);
        return Ok(await catalog.Details(catalog.Retailer,product,ct));
    }
    [HttpPost("products/submit-bulk"), HasPermission(Permissions.Product.Edit), RequireFeature(FeatureKeys.Products)]
    public async Task<IActionResult> SubmitBulk(CatalogBatchSubmissionInput input,CancellationToken ct)
    {
        if(!PublicCatalogBatchPolicy.HasValidIds(input.ProductIds))
            return BadRequest(new{message="Select 1–100 unique products per submission."});
        return Ok(await catalog.SubmitMany(input.ProductIds,input.Commit,ct));
    }
}

public sealed record CatalogBatchSubmissionInput(IReadOnlyCollection<Guid> ProductIds,bool Commit);

[ApiController, PlatformAuthorize, HasPermission(Permissions.Features.Manage), Route("api/public-catalog/owner")]
public sealed class PublicCatalogOwnerController(PublicCatalogService catalog,ApplicationDbContext db,IProductImageStorage images) : ControllerBase
{
    [HttpGet("business-types")]
    public Task<object> BusinessTypes([FromServices] BusinessTypeCatalogService types,CancellationToken ct)=>types.List(true,ct);
    [HttpPut("business-types")]
    public async Task<IActionResult> BusinessType(BusinessTypeInput input,[FromServices] BusinessTypeCatalogService types,CancellationToken ct){await types.Save(input,ct);return Ok(await types.List(true,ct));}
    [HttpGet("products")]
    public Task<object> Queue([FromQuery] Guid? tenant,[FromQuery] string? status,[FromQuery] DateTimeOffset? since,[FromQuery] DateTimeOffset? until,[FromQuery] int page=1,CancellationToken ct=default)
        => catalog.Queue(tenant,status,since,until,page,ct);
    [HttpGet("tenants/{tenant:guid}/products/{product:guid}")]
    public Task<object> Details(Guid tenant,Guid product,CancellationToken ct) => catalog.Details(tenant,product,ct);
    [HttpPost("tenants/{tenant:guid}/products/{product:guid}")]
    public async Task<IActionResult> Decide(Guid tenant,Guid product,CatalogAction input,CancellationToken ct) {await catalog.Decide(tenant,product,input,ct);return Ok(await catalog.Details(tenant,product,ct));}
    [HttpPost("tenants/{tenant:guid}/products/bulk")]
    public async Task<IActionResult> DecideBulk(Guid tenant,CatalogBulkDecisionInput input,CancellationToken ct)
    {
        if(input.Items is null||input.Items.Count is <1 or >PublicCatalogBatchPolicy.MaximumItems||input.Items.Any(x=>x.ProductId==Guid.Empty)||input.Items.Select(x=>x.ProductId).Distinct().Count()!=input.Items.Count)
            return BadRequest(new{message="Select 1–100 unique products per review."});
        return Ok(await catalog.DecideMany(tenant,input,ct));
    }
    [HttpGet("tenants/{tenant:guid}/legacy-plan")]
    public Task<object> Legacy(Guid tenant,CancellationToken ct) => catalog.LegacyPlan(tenant,ct);
    [HttpGet("directory")]
    public Task<object> Directory(CancellationToken ct) => catalog.OwnerDirectory(ct);
    [HttpPost("directory/{tenant:guid}")]
    public async Task<IActionResult> Publish(Guid tenant,PublishDirectoryInput input,CancellationToken ct) {await catalog.PublishDirectory(tenant,input.Publish,input.Version,input.Action,input.Reason,ct);return NoContent();}
    [HttpGet("tenants/{tenant:guid}/products/{product:guid}/images/{image:guid}")]
    public async Task<IActionResult> Image(Guid tenant,Guid product,Guid image,CancellationToken ct)
    {
        var row=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(db.ProductImages
            .Where(x=>x.TenantId==tenant&&x.ProductId==product&&x.ProductImageId==image&&x.IsActive&&!x.IsDeleted),ct);
        if(row is null)return NotFound();
        var content=await images.ReadAsync(new(row.TenantId,row.StorageProvider,row.ObjectKey,row.ImageData,row.ContentType),ct);
        return content is null?NotFound():File(content.Content,content.ContentType);
    }
}
public sealed record PublishDirectoryInput(bool Publish,string Version,string? Action=null,string? Reason=null);
