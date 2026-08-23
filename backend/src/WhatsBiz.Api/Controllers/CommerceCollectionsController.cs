using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.CommerceCollections;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/commerce/collections"), RequireFeature(FeatureKeys.CommerceCollections)]
public sealed class CommerceCollectionsController(ISender sender, IWhatsAppCommerceService commerce, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Product.View)] public Task<PagedCollections> Get([FromQuery] string? search, [FromQuery] bool? isActive, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => sender.Send(new GetCollections(search, isActive, pageNumber, pageSize), token);
    [HttpGet("{id:guid}"), HasPermission(Permissions.Product.View)] public Task<CollectionDetailDto> GetById(Guid id, CancellationToken token) => sender.Send(new GetCollection(id), token);
    [HttpPost, HasPermission(Permissions.Product.Create)] public async Task<IActionResult> Create(CollectionInput input, CancellationToken token) { var x = await sender.Send(new CreateCollection(input), token); return CreatedAtAction(nameof(GetById), new { id = x.CollectionId }, x); }
    [HttpPut("{id:guid}"), HasPermission(Permissions.Product.Edit)] public Task<CollectionDetailDto> Update(Guid id, CollectionInput input, CancellationToken token) => sender.Send(new UpdateCollection(id, input), token);
    [HttpDelete("{id:guid}"), HasPermission(Permissions.Product.Delete)] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await sender.Send(new DeleteCollection(id), token); return NoContent(); }
    [HttpGet("{id:guid}/products"), HasPermission(Permissions.Product.View)] public Task<IReadOnlyCollection<CollectionProductDto>> Products(Guid id, CancellationToken token) => sender.Send(new GetCollectionProducts(id), token);
    [HttpPost("{id:guid}/products"), HasPermission(Permissions.Product.Edit)] public Task<IReadOnlyCollection<CollectionProductDto>> AddProducts(Guid id, AddCollectionProductsInput input, CancellationToken token) => sender.Send(new AddCollectionProducts(id, input), token);
    [HttpDelete("{id:guid}/products/{productId:guid}"), HasPermission(Permissions.Product.Edit)] public async Task<IActionResult> RemoveProduct(Guid id, Guid productId, CancellationToken token) { await sender.Send(new RemoveCollectionProduct(id, productId), token); return NoContent(); }
    [HttpPost("{id:guid}/send-whatsapp"), HasPermission(Permissions.Product.Edit)] public Task<WhatsAppCommerceSendResult> Send(Guid id, SendCollectionInput input, CancellationToken token) => commerce.SendCollectionAsync(currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), id, input.CustomerId, token);
}
