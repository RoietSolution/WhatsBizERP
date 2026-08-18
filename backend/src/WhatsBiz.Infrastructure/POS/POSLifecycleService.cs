using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSLifecycleService(IConfiguration configuration) : IPOSLifecycleService
{
    public async Task TransitionHeldAsync(Guid invoiceId, string action, string? user, CancellationToken token)
    {
        try
        {
            await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable."));
            await connection.OpenAsync(token); await using var command = connection.CreateCommand();
            command.CommandText = "sales.POS_TransitionHeldInvoice"; command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@InvoiceId", invoiceId); command.Parameters.AddWithValue("@Action", action);
            command.Parameters.AddWithValue("@ModifiedBy", user ?? (object)DBNull.Value); await command.ExecuteNonQueryAsync(token);
        }
        catch (SqlException exception) when (exception.Number >= 51100) { throw new BusinessRuleException(exception.Message); }
    }
}
