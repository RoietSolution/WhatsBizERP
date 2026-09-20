using System.Text.Json;
using System.Globalization;
#pragma warning disable CA1848
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

/// Deterministic, tenant-scoped inbound commerce flow. The webhook has already
/// authenticated and resolved the tenant; this service never accepts a tenant
/// from message content.
public sealed class WhatsAppInboundCommerceHandler(
    IConfiguration configuration,
    IWhatsAppCommerceProviderResolver providers,
    IDataProtectionProvider protection,
    IPOSEngine pos,
    ILogger<WhatsAppInboundCommerceHandler> logger) : IWhatsAppInboundCommerceHandler
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task HandleAsync(Guid tenantId, string providerMode, string phoneNumberId, string sender, string messageId, string text, CancellationToken token)
    {
        if (!providerMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)) return;
        var mobile = new string(sender.Where(char.IsDigit).ToArray());
        if (mobile.Length is < 8 or > 15) return;
        var action = (text ?? string.Empty).Trim();
        await RetryFailedAsync(tenantId, messageId, token);
        var conversation = await BeginAsync(tenantId, mobile, messageId, token);
        if (conversation is null) return; // Meta retry or a concurrent worker already owns it.
        var result = "FAILED";
        try
        {
            var response = await ExecuteAsync(tenantId, mobile, conversation, action, messageId, token);
            await SendAsync(tenantId, phoneNumberId, mobile, messageId, response, token);
            result = "PROCESSED";
            await CompleteAsync(tenantId, conversation.ConversationId, messageId, result, response, token);
            logger.LogInformation("WhatsApp commerce TenantId {TenantId} inbound {MessageId} action {Action} result {Result} outbound {OutboundResult}", tenantId, messageId, action, result, "SENT");
        }
        catch (Exception ex) when (ex is BusinessRuleException or SqlException or InvalidOperationException)
        {
            await FailAsync(tenantId, conversation.ConversationId, messageId, ex.Message, token);
            logger.LogWarning(ex, "WhatsApp commerce TenantId {TenantId} inbound {MessageId} action {Action} result FAILED", tenantId, messageId, action);
        }
    }

    private async Task<string> ExecuteAsync(Guid tenantId, string mobile, Conversation conversation, string input, string messageId, CancellationToken token)
    {
        var command = input.Trim();
        if (command.Length == 0 || command.Equals("hi", StringComparison.OrdinalIgnoreCase) || command.Equals("hello", StringComparison.OrdinalIgnoreCase) || command.Equals("menu", StringComparison.OrdinalIgnoreCase))
            return "Welcome to WhatsBiz.\n\nReply with:\n1. Browse Products\n2. Search <name or code>\n3. My Cart\n4. Order Status\n\nReply ADD <product code> <quantity> to add an item, or CONFIRM to place your order.";
        if (command.Equals("1", StringComparison.OrdinalIgnoreCase) || command.Contains("browse", StringComparison.OrdinalIgnoreCase))
            return await ProductList(tenantId, conversation.WarehouseId, null, token);
        if (command.StartsWith("search ", StringComparison.OrdinalIgnoreCase))
            return await ProductList(tenantId, conversation.WarehouseId, command[7..].Trim(), token);
        if (command.Equals("3", StringComparison.OrdinalIgnoreCase) || command.Contains("cart", StringComparison.OrdinalIgnoreCase))
            return await Cart(tenantId, conversation.ConversationId, token);
        if (command.Equals("4", StringComparison.OrdinalIgnoreCase) || command.Contains("order status", StringComparison.OrdinalIgnoreCase))
            return await OrderStatus(tenantId, conversation.CustomerId, token);
        if (command.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !decimal.TryParse(parts.Length > 2 ? parts[2] : "1", out var quantity) || quantity <= 0) return "Use ADD <product code> <quantity>.";
            return await AddToCart(tenantId, conversation, parts[1], quantity, token);
        }
        if (command.Equals("confirm", StringComparison.OrdinalIgnoreCase) || command.Equals("place order", StringComparison.OrdinalIgnoreCase))
            return await Confirm(tenantId, conversation, messageId, token);
        return "I did not recognize that command. Reply MENU to see the available WhatsApp store options.";
    }

    private async Task<string> Confirm(Guid tenantId, Conversation c, string messageId, CancellationToken token)
    {
        if (c.CustomerId is not Guid customer) return "Please provide or link a customer account before confirming this order.";
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var claim = new SqlCommand("UPDATE integration.WhatsAppCommerceConversations SET PendingOrderStatus=N'PROCESSING',UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND ConversationId=@conversation AND PendingOrderStatus IN(N'NONE',N'FAILED') AND OrderId IS NULL;", connection);
        claim.Parameters.AddWithValue("@tenant", tenantId); claim.Parameters.AddWithValue("@conversation", c.ConversationId);
        if (await claim.ExecuteNonQueryAsync(token) != 1) return "Your order is already being processed. Reply ORDER STATUS shortly.";
        var lines = new List<object>();
        await using (var q = new SqlCommand("SELECT ProductId,Quantity,UnitPrice,TaxPercentage FROM integration.WhatsAppCommerceCartLines WHERE TenantId=@tenant AND ConversationId=@conversation;", connection))
        { q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@conversation", c.ConversationId); await using var r = await q.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) lines.Add(new { ProductId = r.GetGuid(0), Quantity = r.GetDecimal(1), UnitPrice = r.GetDecimal(2), TaxPercentage = r.GetDecimal(3), Barcode = (string?)null, DiscountPercentage = 0m, DiscountAmount = 0m }); }
        if (lines.Count == 0) { await FailAsync(tenantId, c.ConversationId, messageId, "Cart is empty.", token); return "Your cart is empty. Reply BROWSE to see products."; }
        var request = new POSPostRequest(null, null, customer, c.WarehouseId, null, JsonSerializer.Serialize(lines), "[]", 0, 0, "WhatsApp LIVE order", "HELD", false, null, "WHATSAPP", tenantId, "WHATSAPP");
        try
        {
            var posted = await pos.PostForTenant(request, tenantId, $"WA:{tenantId:N}:{c.ConversationId:N}:{messageId}", token);
            await using var update = new SqlCommand("UPDATE integration.WhatsAppCommerceConversations SET PendingOrderStatus=N'COMPLETED',OrderId=@order,CartStatus=N'ORDERED',State=N'MENU',LastResult=@result,UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND ConversationId=@conversation;", connection);
            update.Parameters.AddWithValue("@order", posted.InvoiceId); update.Parameters.AddWithValue("@result", posted.InvoiceNumber); update.Parameters.AddWithValue("@tenant", tenantId); update.Parameters.AddWithValue("@conversation", c.ConversationId); await update.ExecuteNonQueryAsync(token);
            return $"Order confirmed.\nOrder: {posted.InvoiceNumber}\nAmount: ₹{posted.GrandTotal:0.00}";
        }
        catch { await FailAsync(tenantId, c.ConversationId, messageId, "Order could not be posted.", token); throw; }
    }

    private async Task<string> AddToCart(Guid tenantId, Conversation c, string code, decimal quantity, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var q = new SqlCommand("SELECT TOP(1)p.ProductId,p.ProductName,p.SellingPrice,p.GSTPercentage FROM master.Products p WHERE p.TenantId=@tenant AND p.ProductCode=@code AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1;", connection); q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@code", code);
        await using var r = await q.ExecuteReaderAsync(token); if (!await r.ReadAsync(token)) return "Product code was not found or is not available."; var id = r.GetGuid(0); var name = r.GetString(1); var price = r.GetDecimal(2); var tax = r.GetDecimal(3);
        await r.CloseAsync(); await using var stock = new SqlCommand("SELECT ISNULL(SUM(QuantityAvailable),0) FROM inventory.InventoryBalances WHERE TenantId=@tenant AND WarehouseId=@warehouse AND ProductId=@product;", connection); stock.Parameters.AddWithValue("@tenant", tenantId); stock.Parameters.AddWithValue("@warehouse", c.WarehouseId); stock.Parameters.AddWithValue("@product", id); if (Convert.ToDecimal(await stock.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) < quantity) return "The requested quantity is not currently available.";
        await using var upsert = new SqlCommand("MERGE integration.WhatsAppCommerceCartLines AS t USING (SELECT @conversation ConversationId,@tenant TenantId,@product ProductId) AS s ON t.ConversationId=s.ConversationId AND t.ProductId=s.ProductId WHEN MATCHED THEN UPDATE SET Quantity=t.Quantity+@quantity,UpdatedOn=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT(ConversationId,TenantId,ProductId,Quantity,UnitPrice,TaxPercentage) VALUES(@conversation,@tenant,@product,@quantity,@price,@tax);", connection); upsert.Parameters.AddWithValue("@conversation", c.ConversationId); upsert.Parameters.AddWithValue("@tenant", tenantId); upsert.Parameters.AddWithValue("@product", id); upsert.Parameters.AddWithValue("@quantity", quantity); upsert.Parameters.AddWithValue("@price", price); upsert.Parameters.AddWithValue("@tax", tax); await upsert.ExecuteNonQueryAsync(token); return $"Added {quantity:0.####} × {name} to your cart. Reply CART to review or CONFIRM to place the order.";
    }

    private async Task<string> ProductList(Guid tenantId, Guid warehouse, string? search, CancellationToken token)
    { await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("SELECT TOP(10)p.ProductCode,p.ProductName,p.SellingPrice,ISNULL(SUM(b.QuantityAvailable),0) FROM master.Products p LEFT JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId AND b.TenantId=@tenant AND b.WarehouseId=@warehouse WHERE p.TenantId=@tenant AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1 AND (@search IS NULL OR p.ProductCode LIKE N'%'+@search+N'%' OR p.ProductName LIKE N'%'+@search+N'%') GROUP BY p.ProductCode,p.ProductName,p.SellingPrice ORDER BY p.ProductName;", c); q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@warehouse",warehouse);q.Parameters.AddWithValue("@search",(object?)search??DBNull.Value); await using var r=await q.ExecuteReaderAsync(token);var rows=new List<string>();while(await r.ReadAsync(token))rows.Add($"{r.GetString(0)} - {r.GetString(1)} ₹{r.GetDecimal(2):0.00} ({r.GetDecimal(3):0.####} available)");return rows.Count==0?"No products are currently available.":"Products:\n"+string.Join('\n',rows)+"\n\nReply ADD <product code> <quantity>."; }
    private async Task<string> Cart(Guid tenantId, Guid conversation, CancellationToken token) { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT p.ProductName,l.Quantity,l.UnitPrice FROM integration.WhatsAppCommerceCartLines l JOIN master.Products p ON p.ProductId=l.ProductId AND p.TenantId=l.TenantId WHERE l.TenantId=@tenant AND l.ConversationId=@conversation;",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@conversation",conversation);await using var r=await q.ExecuteReaderAsync(token);var rows=new List<string>();while(await r.ReadAsync(token))rows.Add($"{r.GetString(0)} × {r.GetDecimal(1):0.####} = ₹{r.GetDecimal(1)*r.GetDecimal(2):0.00}");return rows.Count==0?"Your cart is empty.":"Your cart:\n"+string.Join('\n',rows)+"\n\nReply CONFIRM to place the order."; }
    private async Task<string> OrderStatus(Guid tenantId, Guid? customer, CancellationToken token) { if (customer is not Guid id) return "No customer account is linked to this WhatsApp number yet."; await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TOP(1)i.InvoiceNumber,i.Status FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId AND i.TenantId=w.TenantId WHERE w.TenantId=@tenant AND i.CustomerId=@customer ORDER BY i.InvoiceDate DESC;",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@customer",id);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?$"Latest order {r.GetString(0)}: {r.GetString(1)}":"No WhatsApp orders found."; }
    private async Task<string> ExistingOrder(Guid tenantId, Guid id, CancellationToken token) { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TOP(1)InvoiceNumber,GrandTotal FROM sales.SalesInvoices WHERE TenantId=@tenant AND InvoiceId=@id;",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@id",id);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?$"Order already confirmed.\nOrder: {r.GetString(0)}\nAmount: ₹{r.GetDecimal(1):0.00}":"Order status is unavailable."; }

    private async Task<Conversation?> BeginAsync(Guid tenantId,string mobile,string messageId,CancellationToken token)
    { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var t=await c.BeginTransactionAsync(token);await using var i=new SqlCommand("INSERT INTO integration.WhatsAppCommerceInbound(TenantId,MetaMessageId,Status,UpdatedOn) VALUES(@tenant,@message,N'PROCESSING',SYSUTCDATETIME());",c,(SqlTransaction)t);i.Parameters.AddWithValue("@tenant",tenantId);i.Parameters.AddWithValue("@message",messageId);try{await i.ExecuteNonQueryAsync(token);}catch(SqlException e)when(e.Number is 2601 or 2627){return null;}await using var q=new SqlCommand("SELECT TOP(1)ConversationId,CustomerId,WarehouseId FROM integration.WhatsAppCommerceConversations WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND NormalizedMobile=@mobile;",c,(SqlTransaction)t);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@mobile",mobile);Guid conversation;Guid? customer;Guid warehouse;if(await q.ExecuteReaderAsync(token) is not SqlDataReader r) return null;await using(r){if(await r.ReadAsync(token)){conversation=r.GetGuid(0);customer=r.IsDBNull(1)?null:r.GetGuid(1);warehouse=r.IsDBNull(2)?Guid.Empty:r.GetGuid(2);}else{conversation=Guid.NewGuid();customer=null;warehouse=Guid.Empty;}}if(warehouse==Guid.Empty){await using var w=new SqlCommand("SELECT TOP(1)WarehouseId FROM inventory.Warehouses WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0 ORDER BY IsDefault DESC,WarehouseName;",c,(SqlTransaction)t);w.Parameters.AddWithValue("@tenant",tenantId);warehouse=(Guid?)await w.ExecuteScalarAsync(token)??Guid.Empty;}if(warehouse==Guid.Empty)throw new BusinessRuleException("No active warehouse is configured for this tenant.");if(customer is null){await using var cu=new SqlCommand("SELECT TOP(1)CustomerId FROM sales.Customers WHERE TenantId=@tenant AND IsDeleted=0 AND Mobile IS NOT NULL AND RIGHT(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),10)=RIGHT(@mobile,10) ORDER BY IsActive DESC,CreatedOn;",c,(SqlTransaction)t);cu.Parameters.AddWithValue("@tenant",tenantId);cu.Parameters.AddWithValue("@mobile",mobile);customer=(Guid?)await cu.ExecuteScalarAsync(token);}if(conversation==Guid.Empty){await using var n=new SqlCommand("INSERT integration.WhatsAppCommerceConversations(ConversationId,TenantId,CustomerId,NormalizedMobile,WarehouseId) VALUES(@id,@tenant,@customer,@mobile,@warehouse);",c,(SqlTransaction)t);n.Parameters.AddWithValue("@id",conversation);n.Parameters.AddWithValue("@tenant",tenantId);n.Parameters.AddWithValue("@customer",(object?)customer??DBNull.Value);n.Parameters.AddWithValue("@mobile",mobile);n.Parameters.AddWithValue("@warehouse",warehouse);await n.ExecuteNonQueryAsync(token);}await using var u=new SqlCommand("UPDATE integration.WhatsAppCommerceInbound SET ConversationId=@conversation WHERE TenantId=@tenant AND MetaMessageId=@message;",c,(SqlTransaction)t);u.Parameters.AddWithValue("@conversation",conversation);u.Parameters.AddWithValue("@tenant",tenantId);u.Parameters.AddWithValue("@message",messageId);await u.ExecuteNonQueryAsync(token);await t.CommitAsync(token);return new(conversation,customer,warehouse); }
    private async Task SendAsync(Guid tenantId,string phone,string recipient,string messageId,string text,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TOP(1)ApiVersion,PhoneNumberId,AccessTokenProtected FROM integration.WhatsAppConfigurations WHERE TenantId=@tenant AND ProviderMode=N'LIVE' AND IsEnabled=1 AND ConnectionStatus=N'CONNECTED';",c);q.Parameters.AddWithValue("@tenant",tenantId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))throw new BusinessRuleException("LIVE WhatsApp is not configured for this tenant.");var version=r.IsDBNull(0)?null:r.GetString(0);var phoneId=r.IsDBNull(1)?null:r.GetString(1);var protectedToken=r.IsDBNull(2)?null:r.GetString(2);if(version is null||phoneId is null||protectedToken is null)throw new BusinessRuleException("LIVE WhatsApp configuration is incomplete.");var tokenValue=protection.CreateProtector("WhatsBiz.WhatsApp.Secrets.v1").Unprotect(protectedToken);var sent=await providers.Resolve(WhatsAppProviderModes.Live).SendTransactionalAsync(new(version,phoneId,tokenValue,recipient,"INBOUND_COMMERCE",null,"en_US",text,[]),token);await r.CloseAsync();await using var o=new SqlCommand("INSERT integration.WhatsAppCommerceOutbound(WhatsAppCommerceOutboundId,TenantId,MetaMessageId,RecipientNumber,MessageText,Status,ProviderMessageId,Attempts,LastError) VALUES(NEWID(),@tenant,@message,@recipient,@text,@status,@provider,1,@error);",c);o.Parameters.AddWithValue("@tenant",tenantId);o.Parameters.AddWithValue("@message",messageId);o.Parameters.AddWithValue("@recipient",recipient);o.Parameters.AddWithValue("@text",text);o.Parameters.AddWithValue("@status",sent.Succeeded?"SENT":"FAILED");o.Parameters.AddWithValue("@provider",(object?)sent.ProviderMessageId??DBNull.Value);o.Parameters.AddWithValue("@error",(object?)sent.SafeMessage??DBNull.Value);await o.ExecuteNonQueryAsync(token);if(!sent.Succeeded)throw new BusinessRuleException("The order was processed, but the WhatsApp reply could not be delivered.");}
    private async Task CompleteAsync(Guid tenant,Guid conversation,string message,string status,string result,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("UPDATE integration.WhatsAppCommerceInbound SET Status=@status,ResultMessage=@result,UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND MetaMessageId=@message;UPDATE integration.WhatsAppCommerceConversations SET LastInboundMessageId=@message,LastResult=@result,UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND ConversationId=@conversation;",c);q.Parameters.AddWithValue("@status",status);q.Parameters.AddWithValue("@result",result);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@message",message);q.Parameters.AddWithValue("@conversation",conversation);await q.ExecuteNonQueryAsync(token);}
    private Task FailAsync(Guid tenant,Guid conversation,string message,string error,CancellationToken token)=>CompleteAsync(tenant,conversation,message,"FAILED",error,token);
    private async Task RetryFailedAsync(Guid tenant,string message,CancellationToken token)
    { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("DELETE FROM integration.WhatsAppCommerceInbound WHERE TenantId=@tenant AND MetaMessageId=@message AND Status=N'FAILED';",c);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@message",message);await q.ExecuteNonQueryAsync(token); }
    private sealed record Conversation(Guid ConversationId,Guid? CustomerId,Guid WarehouseId);
}
