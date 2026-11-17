using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Configuration;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/storefront-domain")]
public sealed class StorefrontDomainController(IConfiguration configuration, IStorefrontService storefront) : ControllerBase
{
    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve(CancellationToken token)
    {
        var key = StorefrontDomainMappings.ResolveStoreKey(configuration, Request.Headers.Origin.ToString());
        if (key is null) return NotFound();
        var store = await storefront.GetStoreAsync(key, token);
        if (store is null || !string.Equals(store.StoreKey, key, StringComparison.OrdinalIgnoreCase)) return NotFound();
        return Ok(new { storeKey = store.StoreKey });
    }
}
