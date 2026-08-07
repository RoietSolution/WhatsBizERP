using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Warehouses;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;
[ApiController, Route("api/warehousetypes")]
public sealed class WarehouseTypesController(ISender sender) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Warehouse.View)] public Task<IReadOnlyCollection<WarehouseTypeDto>> Get(CancellationToken token) => sender.Send(new GetWarehouseTypes(), token);
    [HttpPost, HasPermission(Permissions.Warehouse.Create)] public async Task<IActionResult> Create(WarehouseTypeInput input, CancellationToken token) { var value = await sender.Send(new CreateWarehouseType(input), token); return Ok(value); }
    [HttpPut("{id:guid}"), HasPermission(Permissions.Warehouse.Edit)] public Task<WarehouseTypeDto> Update(Guid id, WarehouseTypeInput input, CancellationToken token) => sender.Send(new UpdateWarehouseType(id, input), token);
    [HttpDelete("{id:guid}"), HasPermission(Permissions.Warehouse.Delete)] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await sender.Send(new DeleteWarehouseType(id), token); return NoContent(); }
}
