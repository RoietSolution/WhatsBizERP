using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Referrals;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/customer-referrals/administration/tenants/{tenantId:guid}")]
public sealed class CustomerReferralAdministrationController(
    ICustomerReferralService referrals,
    IFeatureService features,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("configuration"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<CustomerReferralAdministrationDto> Get(Guid tenantId, CancellationToken token)
    {
        await TargetTenant(tenantId, token);
        return new(tenantId, await features.IsEnabledAsync(tenantId, FeatureKeys.CustomerReferralRewards, token),
            await referrals.GetConfigurationAsync(tenantId, token));
    }

    [HttpPut("configuration"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<CustomerReferralAdministrationDto> Save(Guid tenantId, ReferralConfigurationInput input, CancellationToken token)
    {
        await TargetTenant(tenantId, token);
        var configuration = await referrals.SaveConfigurationAsync(tenantId, input, currentUser.Username, token);
        return new(tenantId, true, configuration);
    }

    private async Task TargetTenant(Guid tenantId, CancellationToken token)
    {
        await features.GetTenantConfigurationAsync(tenantId, token);
        HttpContext.Items[AuditMiddleware.TargetTenantItemKey] = tenantId;
    }
}