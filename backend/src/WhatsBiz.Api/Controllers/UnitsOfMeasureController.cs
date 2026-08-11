using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.MasterData;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/uom")]
public sealed class UnitsOfMeasureController(ISender sender) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Product.View)] public Task<IReadOnlyCollection<UnitOfMeasureDto>> Get([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) => sender.Send(new GetUnitsOfMeasureQuery(search, isActive), cancellationToken);
    [HttpPost, HasPermission(Permissions.Product.Create)] public Task<UnitOfMeasureDto> Create(UnitOfMeasureInput input, CancellationToken cancellationToken) => sender.Send(new CreateUnitOfMeasureCommand(input), cancellationToken);
    [HttpPut("{id:guid}"), HasPermission(Permissions.Product.Edit)] public Task<UnitOfMeasureDto> Update(Guid id, UnitOfMeasureInput input, CancellationToken cancellationToken) => sender.Send(new UpdateUnitOfMeasureCommand(id, input), cancellationToken);
    [HttpDelete("{id:guid}"), HasPermission(Permissions.Product.Delete)] public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) { await sender.Send(new DeleteUnitOfMeasureCommand(id), cancellationToken); return NoContent(); }
    [HttpGet("export"), HasPermission(Permissions.Product.View)] public async Task<IActionResult> Export(CancellationToken token) => File(await sender.Send(new ExportUnitsOfMeasureQuery(), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "units-of-measure.xlsx");
    [HttpGet("import-template"), HasPermission(Permissions.Product.Create)] public async Task<IActionResult> Template(CancellationToken token) => File(await sender.Send(new DownloadUnitOfMeasureTemplateQuery(), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "unit-import-template.xlsx");
    [HttpPost("import"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10 * 1024 * 1024)] public async Task<ImportProductMasterResult> Import(IFormFile file, CancellationToken token) { await using var stream = new MemoryStream(); await file.CopyToAsync(stream, token); return await sender.Send(new ImportUnitsOfMeasureCommand(stream.ToArray()), token); }
}
