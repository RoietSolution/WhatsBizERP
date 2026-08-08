using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Suppliers;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;
[ApiController, Route("api/suppliers")]
public sealed class SuppliersController(ISender sender) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Supplier.View)] public Task<PagedSuppliers> Get([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] string sortBy = "supplierName", [FromQuery] bool descending = false, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => sender.Send(new GetSuppliersQuery(search, isActive, sortBy, descending, pageNumber, pageSize), token);
    [HttpGet("search"), HasPermission(Permissions.Supplier.View)] public Task<PagedSuppliers> Search([FromQuery] string? q, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => sender.Send(new GetSuppliersQuery(q, null, "supplierName", false, pageNumber, pageSize), token);
    [HttpGet("dropdown"), HasPermission(Permissions.Supplier.View)] public Task<IReadOnlyCollection<SupplierDropdownDto>> Dropdown([FromQuery] string? search, CancellationToken token) => sender.Send(new SupplierDropdownQuery(search), token);
    [HttpGet("payment-terms"), HasPermission(Permissions.Supplier.View)] public Task<IReadOnlyCollection<PaymentTermDto>> Terms(CancellationToken token) => sender.Send(new PaymentTermsQuery(), token);
    [HttpGet("export"), HasPermission(Permissions.Supplier.View)] public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken token) => File(await sender.Send(new ExportSuppliersQuery(search, isActive), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "suppliers.xlsx");
    [HttpGet("import-template"), HasPermission(Permissions.Supplier.Create)] public async Task<IActionResult> Template(CancellationToken token) => File(await sender.Send(new SupplierTemplateQuery(), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "supplier-import-template.xlsx");
    [HttpPost("import"), HasPermission(Permissions.Supplier.Create), RequestSizeLimit(10 * 1024 * 1024)] public async Task<SupplierImportResult> Import(IFormFile file, CancellationToken token) { await using var m = new MemoryStream(); await file.CopyToAsync(m, token); return await sender.Send(new ImportSuppliersCommand(m.ToArray()), token); }
    [HttpGet("{id:guid}"), HasPermission(Permissions.Supplier.View)] public Task<SupplierDto> GetById(Guid id, CancellationToken token) => sender.Send(new GetSupplierQuery(id), token);
    [HttpPost, HasPermission(Permissions.Supplier.Create)] public async Task<IActionResult> Create(SupplierInput input, CancellationToken token) { var x = await sender.Send(new CreateSupplierCommand(input), token); return CreatedAtAction(nameof(GetById), new { id = x.SupplierId }, x); }
    [HttpPut("{id:guid}"), HasPermission(Permissions.Supplier.Edit)] public Task<SupplierDto> Update(Guid id, SupplierInput input, CancellationToken token) => sender.Send(new UpdateSupplierCommand(id, input), token);
    [HttpDelete("{id:guid}"), HasPermission(Permissions.Supplier.Delete)] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await sender.Send(new DeleteSupplierCommand(id), token); return NoContent(); }
    [HttpPost("{id:guid}/documents"), HasPermission(Permissions.Supplier.Edit), RequestSizeLimit(10 * 1024 * 1024)] public async Task<SupplierDocumentDto> Upload(Guid id, [FromForm] string documentType, IFormFile file, CancellationToken token) { await using var m = new MemoryStream(); await file.CopyToAsync(m, token); return await sender.Send(new UploadSupplierDocumentCommand(id, documentType, file.FileName, file.ContentType, m.ToArray()), token); }
    [HttpGet("{id:guid}/documents/{documentId:guid}"), HasPermission(Permissions.Supplier.View)] public async Task<IActionResult> Document(Guid id, Guid documentId, CancellationToken token) { var x = await sender.Send(new GetSupplierDocumentQuery(id, documentId), token); return x is null ? NotFound() : File(x.Data, x.ContentType, x.FileName); }
    [HttpDelete("{id:guid}/documents/{documentId:guid}"), HasPermission(Permissions.Supplier.Edit)] public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken token) { await sender.Send(new DeleteSupplierDocumentCommand(id, documentId), token); return NoContent(); }
}
