using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;
using WhatsBiz.Application.Common.Features;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/storefront-administration/campaigns")]
[HasPermission(Permissions.Admin.Settings), RequireFeature(FeatureKeys.StorefrontPopupCampaigns)]
public sealed class StorefrontCampaignsController(IStorefrontCampaignService campaigns, ICurrentUserService user) : ControllerBase
{
    private Guid Tenant => user.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
    [HttpGet] public Task<IReadOnlyCollection<StorefrontCampaign>> List(CancellationToken token)=>campaigns.ListAsync(Tenant,token);
    [HttpPost] public Task<StorefrontCampaign> Create(SaveStorefrontCampaign input,CancellationToken token)=>campaigns.SaveAsync(Tenant,null,input,token);
    [HttpPut("{id:guid}")] public Task<StorefrontCampaign> Update(Guid id,SaveStorefrontCampaign input,CancellationToken token)=>campaigns.SaveAsync(Tenant,id,input,token);
    [HttpPost("{id:guid}/image"), RequestSizeLimit(6*1024*1024)]
    public async Task<IActionResult> Upload(Guid id,[FromForm] IFormFile file,CancellationToken token)
    {if(file.Length is <=0 or >5242880)return BadRequest(new{message="Choose an image up to 5 MB."});await using var content=file.OpenReadStream();await campaigns.UploadAsync(Tenant,id,file.FileName,content,token);return NoContent();}
    [HttpGet("{id:guid}/image"), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Image(Guid id,CancellationToken token)
    {var image=await campaigns.GetAdminImageAsync(Tenant,id,token);return image is null?NotFound():File(image.Content,image.ContentType);}
    [HttpDelete("{id:guid}/image")] public async Task<IActionResult> RemoveImage(Guid id,CancellationToken token){await campaigns.RemoveImageAsync(Tenant,id,token);return NoContent();}
}

[ApiController, AllowAnonymous, Route("api/store/{storeKey}/campaigns")]
public sealed class PublicStorefrontCampaignsController(IStorefrontCampaignService campaigns) : ControllerBase
{
    [HttpGet, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<ActionResult<StorefrontCampaignFeed>> Feed(string storeKey,CancellationToken token)
    {var result=await campaigns.FeedAsync(storeKey,token);return result is null?NotFound():Ok(result);}
    [HttpGet("{id:guid}/image/{media:guid}"), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Image(string storeKey,Guid id,Guid media,CancellationToken token)
    {var image=await campaigns.ImageAsync(storeKey,id,media,token);return image is null?NotFound():File(image.Content,image.ContentType);}
}
