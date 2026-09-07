using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Loyalty;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSLifecycleService(IConfiguration configuration, ILoyaltyService loyalty, ICurrentUserService currentUser) : IPOSLifecycleService
{
    public async Task TransitionHeldAsync(Guid invoiceId, string action, string? user, CancellationToken token)
    {
        try
        {
            await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable."));
            await connection.OpenAsync(token); var tenantId = currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required.");
            await using (var context = connection.CreateCommand()) { context.CommandText = "sys.sp_set_session_context"; context.CommandType = CommandType.StoredProcedure; context.Parameters.AddWithValue("@key", "TenantId"); context.Parameters.AddWithValue("@value", tenantId); await context.ExecuteNonQueryAsync(token); }
            await using var command = connection.CreateCommand();
            command.CommandText = "sales.POS_TransitionHeldInvoice"; command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@InvoiceId", invoiceId); command.Parameters.AddWithValue("@Action", action);
            command.Parameters.AddWithValue("@ModifiedBy", user ?? (object)DBNull.Value); await command.ExecuteNonQueryAsync(token);
            await loyalty.ProcessOrderAsync(tenantId, invoiceId, action == "CANCEL" ? "CANCELLED" : "COMPLETED", user, token);
        }
        catch (SqlException exception) when (exception.Number >= 51100) { throw new BusinessRuleException(exception.Message); }
    }
}
