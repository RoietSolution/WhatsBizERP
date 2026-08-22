using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Customers;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;
[ApiController, Route("api/customer-groups")]
public sealed class CustomerGroupsController(ISender sender) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Customer.View)] public Task<IReadOnlyCollection<CustomerGroupDto>> Get(CancellationToken token) => sender.Send(new GetCustomerGroups(), token);
    [HttpGet("{id:guid}/customers"), HasPermission(Permissions.Customer.View)] public Task<PagedCustomers> Customers(Guid id, CancellationToken token) => sender.Send(new GetCustomerGroupCustomers(id), token);
    [HttpPost, HasPermission(Permissions.Customer.Create)] public async Task<IActionResult> Create(CustomerGroupInput input, CancellationToken token) { var x = await sender.Send(new CreateCustomerGroup(input), token); return Created($"/api/customer-groups/{x.CustomerGroupId}", x); }
}
