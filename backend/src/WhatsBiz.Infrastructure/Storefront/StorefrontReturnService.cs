using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontReturnService(IConfiguration configuration, IStorefrontCustomerService customers) : IStorefrontReturnService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<StorefrontReturnRequestDto> RequestAsync(string storeKey, string sessionToken, Guid orderId, Guid invoiceItemId, StorefrontReturnRequestInput input, CancellationToken token)
    {
        if (input.Quantity <= 0 || input.Quantity > 1000000) throw new BusinessRuleException("Enter a valid return quantity.");
        var reason = input.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 250) throw new BusinessRuleException("Choose a return reason.");
        var explanation = string.IsNullOrWhiteSpace(input.Explanation) ? null : input.Explanation.Trim();
        if (explanation?.Length > 1000) throw new BusinessRuleException("Return explanation is too long.");
        var customer = await customers.GetSessionAsync(storeKey, sessionToken, token) ?? throw new System.UnauthorizedAccessException("A customer session is required.");
        var tenant = await ResolveTenant(storeKey, token) ?? throw new EntityNotFoundException("Store was not found.");
        await using var connection = await Open(tenant, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await using var item = new SqlCommand("""
            SELECT i.InvoiceNumber,i.Status,i.CustomerId,x.InvoiceItemId,p.ProductName,p.ProductCode,x.Quantity,x.ReturnedQuantity,
              COALESCE(d.DeliveredAt,NULL),COALESCE(x.ReturnPolicyModeSnapshot,p.ReturnPolicyMode),
              CASE WHEN COALESCE(x.ReturnPolicyModeSnapshot,p.ReturnPolicyMode)=N'NON_RETURNABLE' THEN NULL ELSE COALESCE(x.ReturnWindowDaysSnapshot,CASE WHEN p.ReturnPolicyMode=N'CUSTOM' THEN p.ReturnWindowDays ELSE sc.DefaultReturnWindowDays END) END
            FROM sales.SalesInvoices i WITH(UPDLOCK,HOLDLOCK)
            JOIN sales.SalesInvoiceItems x ON x.InvoiceId=i.InvoiceId AND x.InvoiceItemId=@item
            JOIN master.Products p ON p.ProductId=x.ProductId AND p.TenantId=i.TenantId
            LEFT JOIN commerce.OrderDeliveries d ON d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId
            LEFT JOIN commerce.StorefrontConfigurations sc ON sc.TenantId=i.TenantId
            WHERE i.TenantId=@tenant AND i.InvoiceId=@order AND i.CustomerId=@customer AND i.Status NOT IN(N'CANCELLED',N'VOID',N'RETURNED');
            """, connection, transaction);
        Add(item,"@tenant",tenant); Add(item,"@order",orderId); Add(item,"@item",invoiceItemId); Add(item,"@customer",customer.Id);
        await using var reader = await item.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new EntityNotFoundException("The order item was not found.");
        var number=reader.GetString(0);var status=reader.GetString(1);var sold=reader.GetDecimal(6);var returned=reader.GetDecimal(7);
        var delivered=reader.IsDBNull(8)?(DateTimeOffset?)null:reader.GetDateTimeOffset(8);var mode=reader.GetString(9);var days=reader.IsDBNull(10)?(int?)null:reader.GetInt32(10);
        var name=reader.GetString(4);var code=reader.IsDBNull(5)?null:reader.GetString(5);await reader.CloseAsync();
        if (delivered is null) throw new BusinessRuleException("Returns can be requested after the order is delivered.");
        if (mode.Equals("NON_RETURNABLE",StringComparison.OrdinalIgnoreCase) || days is null or <0) throw new BusinessRuleException("This product is not eligible for return.");
        var deadline=delivered.Value.Date.AddDays(days.Value);
        if (DateTimeOffset.UtcNow.Date > deadline.Date) throw new BusinessRuleException("The return window has ended.");
        await using var existing = new SqlCommand("SELECT COALESCE(SUM(Quantity),0),MAX(CASE WHEN Status IN(N'REQUESTED',N'APPROVED',N'RECEIVED') THEN 1 ELSE 0 END) FROM commerce.StorefrontReturnRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND InvoiceItemId=@item AND Status<>N'REJECTED';",connection,transaction);
        Add(existing,"@tenant",tenant);Add(existing,"@item",invoiceItemId);await using var er=await existing.ExecuteReaderAsync(token);await er.ReadAsync(token);var requested=er.GetDecimal(0);var hasPending=er.GetInt32(1)==1;await er.CloseAsync();
        if (hasPending) throw new BusinessRuleException("A return request is already awaiting processing.");
        if (requested + input.Quantity > sold-returned) throw new BusinessRuleException("The requested quantity exceeds the remaining returnable quantity.");
        var id=Guid.NewGuid();
        await using var insert=new SqlCommand("INSERT commerce.StorefrontReturnRequests(ReturnRequestId,TenantId,InvoiceId,InvoiceItemId,CustomerId,Quantity,Reason,Explanation) VALUES(@id,@tenant,@order,@item,@customer,@qty,@reason,@explanation);",connection,transaction);
        Add(insert,"@id",id);Add(insert,"@tenant",tenant);Add(insert,"@order",orderId);Add(insert,"@item",invoiceItemId);Add(insert,"@customer",customer.Id);Add(insert,"@qty",input.Quantity);Add(insert,"@reason",reason);Add(insert,"@explanation",explanation);await insert.ExecuteNonQueryAsync(token);await transaction.CommitAsync(token);
        return new(id,orderId,invoiceItemId,number,name,code,input.Quantity,reason,explanation,"REQUESTED",DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyCollection<StorefrontReturnRequestDto>?> CustomerRequestsAsync(string storeKey,string sessionToken,Guid orderId,CancellationToken token)
    {
        var customer=await customers.GetSessionAsync(storeKey,sessionToken,token);if(customer is null)return null;var tenant=await ResolveTenant(storeKey,token);if(tenant is null)return null;await using var c=await Open(tenant.Value,token);return await Read(c,"WHERE r.TenantId=@tenant AND r.InvoiceId=@order AND r.CustomerId=@customer",new[]{("@tenant",(object)tenant.Value),("@order",(object)orderId),("@customer",(object)customer.Id)},token);
    }

    public async Task<IReadOnlyCollection<StorefrontReturnRequestDto>> RetailerRequestsAsync(Guid tenantId,CancellationToken token)
    { await using var c=await Open(tenantId,token);return await Read(c,"WHERE r.TenantId=@tenant",new[]{("@tenant",(object)tenantId)},token); }

    public async Task<StorefrontReturnRequestDto> DecideAsync(Guid tenantId,Guid requestId,Guid actorId,StorefrontReturnDecisionInput input,CancellationToken token)
    {
        var next=input.Status?.Trim().ToUpperInvariant();if(next is not("APPROVED" or "REJECTED" or "RECEIVED" or "COMPLETED"))throw new BusinessRuleException("Return status is invalid.");if(input.Note?.Length>500)throw new BusinessRuleException("Decision note is too long.");
        await using var c=await Open(tenantId,token);await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,token);
        await using var current=new SqlCommand("SELECT Status FROM commerce.StorefrontReturnRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND ReturnRequestId=@id",c,tx);Add(current,"@tenant",tenantId);Add(current,"@id",requestId);var prior=(string?)await current.ExecuteScalarAsync(token)??throw new EntityNotFoundException("Return request was not found.");
        var allowed=(prior,next) switch { ("REQUESTED","APPROVED") or ("REQUESTED","REJECTED") or ("APPROVED","RECEIVED") or ("RECEIVED","COMPLETED")=>true,_=>false };
        if(!allowed)throw new BusinessRuleException("That return status transition is not allowed.");
        await using var q=new SqlCommand("UPDATE commerce.StorefrontReturnRequests SET Status=@status,DecidedAtUtc=CASE WHEN @status IN(N'APPROVED',N'REJECTED') THEN SYSUTCDATETIME() ELSE DecidedAtUtc END,DecidedBy=CASE WHEN @status IN(N'APPROVED',N'REJECTED') THEN @actor ELSE DecidedBy END,DecisionNote=COALESCE(@note,DecisionNote),ReceivedAtUtc=CASE WHEN @status=N'RECEIVED' THEN SYSUTCDATETIME() ELSE ReceivedAtUtc END,CompletedAtUtc=CASE WHEN @status=N'COMPLETED' THEN SYSUTCDATETIME() ELSE CompletedAtUtc END,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND ReturnRequestId=@id AND Status NOT IN(N'REJECTED',N'COMPLETED');",c,tx);Add(q,"@tenant",tenantId);Add(q,"@id",requestId);Add(q,"@status",next);Add(q,"@actor",actorId);Add(q,"@note",input.Note?.Trim());if(await q.ExecuteNonQueryAsync(token)!=1)throw new BusinessRuleException("The return request is no longer actionable.");await tx.CommitAsync(token);return (await RetailerRequestsAsync(tenantId,token)).Single(x=>x.RequestId==requestId);
    }

    private static async Task<IReadOnlyCollection<StorefrontReturnRequestDto>> Read(SqlConnection c,string where,(string,object)[] parameters,CancellationToken token)
    { await using var q=new SqlCommand($"SELECT r.ReturnRequestId,r.InvoiceId,r.InvoiceItemId,i.InvoiceNumber,p.ProductName,p.ProductCode,r.Quantity,r.Reason,r.Explanation,r.Status,r.RequestedAtUtc,r.DecidedAtUtc,r.DecisionNote FROM commerce.StorefrontReturnRequests r JOIN sales.SalesInvoices i ON i.TenantId=r.TenantId AND i.InvoiceId=r.InvoiceId JOIN sales.SalesInvoiceItems x ON x.InvoiceId=r.InvoiceId AND x.InvoiceItemId=r.InvoiceItemId JOIN master.Products p ON p.TenantId=r.TenantId AND p.ProductId=x.ProductId {where} ORDER BY r.RequestedAtUtc DESC;",c);foreach(var p in parameters)Add(q,p.Item1,p.Item2);var rows=new List<StorefrontReturnRequestDto>();await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetDecimal(6),r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),r.GetString(9),r.GetDateTimeOffset(10),r.IsDBNull(11)?null:r.GetDateTimeOffset(11),r.IsDBNull(12)?null:r.GetString(12)));return rows; }
    private async Task<Guid?> ResolveTenant(string key,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1",c);Add(q,"@key",key.Trim().ToUpperInvariant());return await q.ExecuteScalarAsync(token) is Guid id?id:null;}
    private async Task<SqlConnection> Open(Guid tenant,CancellationToken token){var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant",c);Add(q,"@tenant",tenant);await q.ExecuteNonQueryAsync(token);return c;}
    private static void Add(SqlCommand c,string name,object? value)=>c.Parameters.AddWithValue(name,value??DBNull.Value);
}
