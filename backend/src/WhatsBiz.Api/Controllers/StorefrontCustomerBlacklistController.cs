using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/storefront-customer-blacklist"), RequireFeature(FeatureKeys.StorefrontCustomerBlacklist)]
public sealed class StorefrontCustomerBlacklistController(IStorefrontCustomerBlacklistService blacklist, ICurrentUserService current) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Customer.BlacklistManage)]
    public Task<StorefrontBlacklistPageDto> Search([FromQuery] bool activeOnly = true, [FromQuery] string? search = null, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => blacklist.SearchAsync(Tenant(), activeOnly, search, pageNumber, pageSize, token);
    [HttpPost, HasPermission(Permissions.Customer.BlacklistManage)]
    public Task<StorefrontBlacklistDto> Block(StorefrontBlacklistInput input, CancellationToken token) => blacklist.BlockAsync(Tenant(), Actor(), input, token);
    [HttpPost("{blacklistId:guid}/unblock"), HasPermission(Permissions.Customer.BlacklistManage)]
    public async Task<IActionResult> Unblock(Guid blacklistId, CancellationToken token) => await blacklist.UnblockAsync(Tenant(), Actor(), blacklistId, token) ? NoContent() : NotFound();
    private Guid Tenant() => current.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
    private Guid Actor() => current.UserId ?? throw new UnauthorizedAccessException("A user identity is required.");
}
