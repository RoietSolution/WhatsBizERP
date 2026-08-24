using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Loyalty;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/loyalty")]
public sealed class LoyaltyController(ILoyaltyService loyalty, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("configuration"), HasPermission(Permissions.Admin.View)]
    public Task<CoinConfigurationDto> Configuration(CancellationToken token) => loyalty.GetConfigurationAsync(TenantId(), token);

    [HttpPut("configuration"), HasPermission(Permissions.Admin.Settings)]
    public Task<CoinConfigurationDto> Configuration(CoinConfigurationInput input, CancellationToken token) => loyalty.SaveConfigurationAsync(TenantId(), input, currentUser.Username, token);

    [HttpGet("customers/{customerId:guid}/wallet"), HasPermission(Permissions.Customer.View)]
    public Task<CoinWalletDto> Wallet(Guid customerId, [FromQuery] int take = 100, CancellationToken token = default) => loyalty.GetWalletAsync(TenantId(), customerId, take, token);

    [HttpGet("customers/{customerId:guid}/redemption-quote"), HasPermission(Permissions.POS.View)]
    public Task<CoinRedemptionQuote> Quote(Guid customerId, [FromQuery] int coins, [FromQuery] decimal otherDiscount = 0, CancellationToken token = default) => loyalty.QuoteRedemptionAsync(TenantId(), customerId, coins, otherDiscount, token);

    private Guid TenantId() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
}
