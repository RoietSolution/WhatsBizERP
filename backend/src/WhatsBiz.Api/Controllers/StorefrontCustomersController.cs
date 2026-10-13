using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/storefront-customers"), RequireFeature(FeatureKeys.CustomerDemand)]
public sealed class StorefrontCustomersController(IStorefrontCustomerManagementService customers, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Customer.View)]
    public Task<StorefrontCustomerManagementPageDto> Search([FromQuery] string? search, [FromQuery] string sortBy = "name", [FromQuery] bool descending = false, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
        => customers.SearchAsync(currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), search, sortBy, descending, pageNumber, pageSize, token);

    [HttpGet("{customerId:guid}"), HasPermission(Permissions.Customer.View)]
    public Task<StorefrontCustomerManagementDetailDto?> Get(Guid customerId, CancellationToken token)
        => customers.GetAsync(currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), customerId, token);
}
