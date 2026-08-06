using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Product.View)]
    public Task<PagedResult<ProductListItemDto>> Get([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string sortBy = "productName", [FromQuery] bool descending = false, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) => sender.Send(new GetProductsQuery(search, isActive, sortBy, descending, pageNumber, pageSize), cancellationToken);

    [HttpGet("{id:guid}"), HasPermission(Permissions.Product.View)]
    public Task<ProductDto> GetById(Guid id, CancellationToken cancellationToken) => sender.Send(new GetProductByIdQuery(id), cancellationToken);

    [HttpPost, HasPermission(Permissions.Product.Create)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ProductInput input, CancellationToken cancellationToken) { var product = await sender.Send(new CreateProductCommand(input), cancellationToken); return CreatedAtAction(nameof(GetById), new { id = product.ProductId }, product); }

    [HttpPut("{id:guid}"), HasPermission(Permissions.Product.Edit)]
    public Task<ProductDto> Update(Guid id, ProductInput input, CancellationToken cancellationToken) => sender.Send(new UpdateProductCommand(id, input), cancellationToken);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.Product.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) { await sender.Send(new DeleteProductCommand(id), cancellationToken); return NoContent(); }

    [HttpGet("export"), HasPermission(Permissions.Product.View)]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken) => File(await sender.Send(new ExportProductsQuery(search, isActive), cancellationToken), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "products.xlsx");

    [HttpGet("import-template"), HasPermission(Permissions.Product.Create)]
    public async Task<IActionResult> Template(CancellationToken cancellationToken) => File(await sender.Send(new DownloadProductTemplateQuery(), cancellationToken), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "product-import-template.xlsx");

    [HttpPost("import"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ImportProductsResult> Import(IFormFile file, CancellationToken cancellationToken) { await using var stream = new MemoryStream(); await file.CopyToAsync(stream, cancellationToken); return await sender.Send(new ImportProductsCommand(stream.ToArray()), cancellationToken); }

    [HttpGet("{id:guid}/image"), HasPermission(Permissions.Product.View)]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken) { var image = await sender.Send(new GetProductImageQuery(id), cancellationToken); return image is null ? NotFound() : File(image.Content, image.ContentType, image.FileName); }

    [HttpPost("{id:guid}/image"), HasPermission(Permissions.Product.Edit), RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ProductImageDto> UploadImage(Guid id, IFormFile file, CancellationToken cancellationToken) { await using var stream = new MemoryStream(); await file.CopyToAsync(stream, cancellationToken); return await sender.Send(new UploadProductImageCommand(id, file.FileName, file.ContentType, stream.ToArray()), cancellationToken); }

    [HttpDelete("{id:guid}/image"), HasPermission(Permissions.Product.Edit)]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken cancellationToken) { await sender.Send(new DeleteProductImageCommand(id), cancellationToken); return NoContent(); }
}
