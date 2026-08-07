using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Inventory;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;
[ApiController, Route("api/inventory")]
public sealed class InventoryController(ISender sender) : ControllerBase
{
    [HttpGet("balance"), HasPermission(Permissions.Inventory.View)] public Task<PagedBalances> Balance([FromQuery] string? search, [FromQuery] Guid? warehouseId, [FromQuery] Guid? productId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => sender.Send(new GetBalances(search, warehouseId, productId, pageNumber, pageSize), token);
    [HttpGet("summary"), HasPermission(Permissions.Inventory.View)] public Task<SummaryDto> Summary([FromQuery] Guid? warehouseId, CancellationToken token) => sender.Send(new GetSummary(warehouseId), token);
    [HttpGet("transactions"), HasPermission(Permissions.Inventory.View)] public Task<PagedTransactions> Transactions([FromQuery] string? search, [FromQuery] Guid? warehouseId, [FromQuery] string? transactionType, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default) => sender.Send(new GetTransactions(search, warehouseId, transactionType, from, to, pageNumber, pageSize), token);
    [HttpGet("transaction/{id:guid}"), HasPermission(Permissions.Inventory.View)] public Task<TransactionDto> Transaction(Guid id, CancellationToken token) => sender.Send(new GetTransaction(id), token);
    [HttpGet("reservations"), HasPermission(Permissions.Inventory.View)] public Task<IReadOnlyCollection<ReservationDto>> Reservations([FromQuery] Guid? warehouseId, [FromQuery] Guid? productId, CancellationToken token) => sender.Send(new GetReservations(warehouseId, productId), token);
    [HttpGet("balance/export"), HasPermission(Permissions.Inventory.View)] public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] Guid? warehouseId, [FromQuery] Guid? productId, CancellationToken token) => File(await sender.Send(new ExportBalances(search, warehouseId, productId), token), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "inventory-balances.xlsx");
    [HttpPost("adjustment"), HasPermission(Permissions.Inventory.Adjust)] public Task<OperationDto> Adjustment(AdjustmentInput input, CancellationToken token) => sender.Send(new AdjustStock(input), token);
    [HttpPost("transfer"), HasPermission(Permissions.Inventory.Transfer)] public Task<OperationDto> Transfer(TransferInput input, CancellationToken token) => sender.Send(new TransferStock(input), token);
    [HttpPost("reserve"), HasPermission(Permissions.Inventory.Reserve)] public Task<OperationDto> Reserve(ReservationInput input, CancellationToken token) => sender.Send(new ReserveStock(input), token);
}
