#pragma warning disable CA1725
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;
namespace WhatsBiz.Infrastructure.Inventory;
public sealed class InventoryService(SqlIdempotencyExecutor idempotency, IHttpContextAccessor httpContext) : IInventoryService
{
    public Task<InventoryOperationResult> Adjust(InventoryAdjustmentRequest r, CancellationToken t) => Execute("INVENTORY_ADJUSTMENT_LEGACY", r, r.User, "inventory.Inventory_Adjust", [("@ProductId", r.ProductId), ("@WarehouseId", r.WarehouseId), ("@ZoneId", r.ZoneId), ("@BinId", r.BinId), ("@BatchNo", r.BatchNo), ("@SerialNo", r.SerialNo), ("@Quantity", r.Quantity), ("@UnitCost", r.UnitCost), ("@AdjustmentType", r.AdjustmentType), ("@ReasonCode", r.ReasonCode), ("@Remarks", r.Remarks), ("@CreatedBy", r.User)], "StockAdjustmentId", "TransactionId", "TransactionNo", t);
    public Task<InventoryOperationResult> Transfer(InventoryTransferRequest r, CancellationToken t) => Execute("STOCK_TRANSFER_LEGACY", r, r.User, "inventory.Inventory_Transfer", [("@ProductId", r.ProductId), ("@SourceWarehouseId", r.SourceWarehouseId), ("@DestinationWarehouseId", r.DestinationWarehouseId), ("@Quantity", r.Quantity), ("@TransferDate", r.TransferDate), ("@Remarks", r.Remarks), ("@CreatedBy", r.User)], "StockTransferId", "SourceTransactionId", "TransferNo", t);
    public Task<InventoryOperationResult> Reserve(InventoryReservationRequest r, CancellationToken t) => Execute("INVENTORY_RESERVATION", r, r.User, "inventory.Inventory_Reserve", [("@Action", r.Action), ("@StockReservationId", r.StockReservationId), ("@ProductId", r.ProductId), ("@WarehouseId", r.WarehouseId), ("@Quantity", r.Quantity), ("@ReservationReason", r.Reason), ("@ReferenceType", r.ReferenceType), ("@ReferenceId", r.ReferenceId), ("@CreatedBy", r.User)], "StockReservationId", "TransactionId", "ReservationNo", t);

    private async Task<InventoryOperationResult> Execute(string operation, object request, string? user, string procedure, IReadOnlyCollection<(string Name, object? Value)> values, string operationColumn, string transactionColumn, string numberColumn, CancellationToken token)
    {
        try
        {
            return await idempotency.Execute(IdempotencyKeyReader.Read(httpContext), operation, request, user, async (connection, transaction, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = procedure;
                command.CommandType = CommandType.StoredProcedure;
                foreach (var item in values) command.Parameters.AddWithValue(item.Name, item.Value ?? DBNull.Value);
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Inventory operation returned no result.");
                return new InventoryOperationResult(reader.GetGuid(reader.GetOrdinal(operationColumn)), reader.GetGuid(reader.GetOrdinal(transactionColumn)), reader.GetString(reader.GetOrdinal(numberColumn)));
            }, token);
        }
        catch (SqlException ex) when (ex.Number >= 51000) { throw new BusinessRuleException(ex.Message); }
    }
}
