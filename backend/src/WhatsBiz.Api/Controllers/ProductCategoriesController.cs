using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.MasterData;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/productcategories")]
public sealed class ProductCategoriesController(ISender sender) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Product.View)] public Task<IReadOnlyCollection<ProductCategoryDto>> Get([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) => sender.Send(new GetProductCategoriesQuery(search, isActive), cancellationToken);
    [HttpPost, HasPermission(Permissions.Product.Create)] public Task<ProductCategoryDto> Create(ProductCategoryInput input, CancellationToken cancellationToken) => sender.Send(new CreateProductCategoryCommand(input), cancellationToken);
    [HttpPut("{id:guid}"), HasPermission(Permissions.Product.Edit)] public Task<ProductCategoryDto> Update(Guid id, ProductCategoryInput input, CancellationToken cancellationToken) => sender.Send(new UpdateProductCategoryCommand(id, input), cancellationToken);
    [HttpDelete("{id:guid}"), HasPermission(Permissions.Product.Delete)] public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) { await sender.Send(new DeleteProductCategoryCommand(id), cancellationToken); return NoContent(); }
    [HttpGet("export"), HasPermission(Permissions.Product.View)] public async Task<IActionResult> Export(CancellationToken token) => File(await sender.Send(new ExportProductCategoriesQuery(), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "product-categories.xlsx");
    [HttpGet("import-template"), HasPermission(Permissions.Product.Create)] public async Task<IActionResult> Template(CancellationToken token) => File(await sender.Send(new DownloadProductCategoryTemplateQuery(), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "product-category-import-template.xlsx");
    [HttpPost("import"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10 * 1024 * 1024)] public async Task<ImportProductMasterResult> Import(IFormFile file, CancellationToken token) { await using var stream = new MemoryStream(); await file.CopyToAsync(stream, token); return await sender.Send(new ImportProductCategoriesCommand(stream.ToArray()), token); }
}
