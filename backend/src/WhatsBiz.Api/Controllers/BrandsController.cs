using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.MasterData;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/brands")]
public sealed class BrandsController(ISender sender) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Product.View)] public Task<IReadOnlyCollection<BrandDto>> Get([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) => sender.Send(new GetBrandsQuery(search, isActive), cancellationToken);
    [HttpPost, HasPermission(Permissions.Product.Create)] public Task<BrandDto> Create(BrandInput input, CancellationToken cancellationToken) => sender.Send(new CreateBrandCommand(input), cancellationToken);
    [HttpPut("{id:guid}"), HasPermission(Permissions.Product.Edit)] public Task<BrandDto> Update(Guid id, BrandInput input, CancellationToken cancellationToken) => sender.Send(new UpdateBrandCommand(id, input), cancellationToken);
    [HttpDelete("{id:guid}"), HasPermission(Permissions.Product.Delete)] public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) { await sender.Send(new DeleteBrandCommand(id), cancellationToken); return NoContent(); }
    [HttpGet("export"), HasPermission(Permissions.Product.View)] public async Task<IActionResult> Export(CancellationToken token) => File(await sender.Send(new ExportBrandsQuery(), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "brands.xlsx");
    [HttpGet("import-template"), HasPermission(Permissions.Product.Create)] public async Task<IActionResult> Template(CancellationToken token) => File(await sender.Send(new DownloadBrandTemplateQuery(), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "brand-import-template.xlsx");
    [HttpPost("import"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10 * 1024 * 1024)] public async Task<ImportProductMasterResult> Import(IFormFile file, CancellationToken token) { await using var stream = new MemoryStream(); await file.CopyToAsync(stream, token); return await sender.Send(new ImportBrandsCommand(stream.ToArray()), token); }
}
