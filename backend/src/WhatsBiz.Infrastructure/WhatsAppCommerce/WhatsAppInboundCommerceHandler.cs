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
using WhatsBiz.Application.Features.Payments;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

/// Deterministic, tenant-scoped inbound commerce flow. The webhook has already
/// authenticated and resolved the tenant; this service never accepts a tenant
/// from message content.
public sealed class WhatsAppInboundCommerceHandler(
    IConfiguration configuration,
    IWhatsAppCommerceProviderResolver providers,
    IDataProtectionProvider protection,
    IPOSEngine pos,
    ILogger<WhatsAppInboundCommerceHandler> logger,
    IWhatsAppUsageBillingService? usageBilling = null,
    ICommercePaymentService? payments = null) : IWhatsAppInboundCommerceHandler
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task HandleAsync(Guid tenantId, string providerMode, string phoneNumberId, string sender, string messageId,
        WhatsAppCommerceInboundMessage message, CancellationToken token)
    {
        if (!providerMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)) return;
        var mobile = new string(sender.Where(char.IsDigit).ToArray());
        if (mobile.Length is < 8 or > 15) return;
        var action = message.ActionId ?? (message.Products.Count > 0 ? "NATIVE_ORDER" : SafeCommandName(message.Text));
        await RetryFailedAsync(tenantId, messageId, token);
        var conversation = await BeginAsync(tenantId, mobile, messageId, token);
        if (conversation is null) return; // Meta retry or a concurrent worker already owns it.
        var result = "FAILED";
        try
        {
            var response = await ExecuteAsync(tenantId, mobile, conversation, message, messageId, token);
            await SendAsync(tenantId, phoneNumberId, mobile, messageId, response, token);
            result = "PROCESSED";
            await CompleteAsync(tenantId, conversation.ConversationId, messageId, result, response.FallbackText ?? response.Body, token);
            logger.LogInformation("WhatsApp commerce TenantId {TenantId} inbound {MessageId} action {Action} result {Result} outbound {OutboundResult}", tenantId, messageId, action, result, "SENT");
        }
        catch (Exception ex) when (ex is BusinessRuleException or SqlException or InvalidOperationException)
        {
            await FailAsync(tenantId, conversation.ConversationId, messageId, ex.Message, token);
            logger.LogWarning(ex, "WhatsApp commerce TenantId {TenantId} inbound {MessageId} action {Action} result FAILED", tenantId, messageId, action);
        }
    }

    private async Task<WhatsAppCommerceOutboundMessage> ExecuteAsync(Guid tenantId, string mobile, Conversation conversation,
        WhatsAppCommerceInboundMessage inbound, string messageId, CancellationToken token)
    {
        if (inbound.Products.Count > 0)
        {
            await AddMappedProductsToCart(tenantId, conversation, inbound.CatalogId, inbound.Products, token);
            return await CartMessage(tenantId, conversation.ConversationId, token);
        }
        var command = ResolveCommand(inbound);
        if (conversation.OrderId is Guid orderId)
        {
            if (command.Equals("pay razorpay", StringComparison.OrdinalIgnoreCase) || command.Equals("pay online", StringComparison.OrdinalIgnoreCase))
                return Text(await CreatePayment(tenantId, orderId, PaymentProviders.Razorpay, token));
            if (command.Equals("pay upi", StringComparison.OrdinalIgnoreCase) || command.Equals("upi", StringComparison.OrdinalIgnoreCase))
                return Text(await CreatePayment(tenantId, orderId, PaymentProviders.DirectUpi, token));
            if (command.Equals("cod", StringComparison.OrdinalIgnoreCase) || command.Contains("cash on delivery", StringComparison.OrdinalIgnoreCase))
                return Text(await CreatePayment(tenantId, orderId, PaymentProviders.Cod, token));
        }
        if (command.Length == 0 || command.Equals("hi", StringComparison.OrdinalIgnoreCase) || command.Equals("hello", StringComparison.OrdinalIgnoreCase) || command.Equals("menu", StringComparison.OrdinalIgnoreCase))
            return await Menu(tenantId, token);
        if (command.Equals("1", StringComparison.OrdinalIgnoreCase) || command.Contains("browse", StringComparison.OrdinalIgnoreCase))
            return await ProductList(tenantId, conversation.WarehouseId, null, token);
        if (command.Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            await SetState(tenantId, conversation.ConversationId, "SEARCH", token);
            return Text("What product are you looking for? Reply with a product name or code.");
        }
        if (command.StartsWith("search ", StringComparison.OrdinalIgnoreCase))
            return await ProductList(tenantId, conversation.WarehouseId, command[7..].Trim(), token);
        if (conversation.State.Equals("SEARCH", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(inbound.Text))
        {
            await SetState(tenantId, conversation.ConversationId, "MENU", token);
            return await ProductList(tenantId, conversation.WarehouseId, inbound.Text.Trim(), token);
        }
        if (command.Equals("3", StringComparison.OrdinalIgnoreCase) || command.Contains("cart", StringComparison.OrdinalIgnoreCase))
            return await CartMessage(tenantId, conversation.ConversationId, token);
        if (command.Equals("4", StringComparison.OrdinalIgnoreCase) || command.Contains("order status", StringComparison.OrdinalIgnoreCase))
            return Text(await OrderStatus(tenantId, conversation.CustomerId, token));
        if (command.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !decimal.TryParse(parts.Length > 2 ? parts[2] : "1", out var quantity) || quantity <= 0) return Text("Use ADD <product code> <quantity>.");
            return Text(await AddToCart(tenantId, conversation, parts[1], quantity, token));
        }
        if (command.Equals("confirm", StringComparison.OrdinalIgnoreCase) || command.Equals("place order", StringComparison.OrdinalIgnoreCase))
            return await Confirm(tenantId, conversation, messageId, token);
        return Text("I did not recognize that command. Reply MENU to see the available WhatsApp store options.");
    }

    internal static string ResolveCommand(WhatsAppCommerceInboundMessage inbound) => inbound.ActionId switch
    {
        WhatsAppCommerceActionIds.Browse or WhatsAppCommerceActionIds.ContinueShopping => "browse",
        WhatsAppCommerceActionIds.Search => "search",
        WhatsAppCommerceActionIds.Cart => "cart",
        WhatsAppCommerceActionIds.Orders => "order status",
        WhatsAppCommerceActionIds.Checkout => "confirm",
        WhatsAppCommerceActionIds.PayRazorpay => "pay razorpay",
        WhatsAppCommerceActionIds.PayUpi => "pay upi",
        WhatsAppCommerceActionIds.PayCod => "cod",
        _ => inbound.Text?.Trim() ?? string.Empty
    };

    private async Task<WhatsAppCommerceOutboundMessage> Menu(Guid tenantId, CancellationToken token)
    {
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);
        await using var command=new SqlCommand("SELECT TOP(1) CompanyName FROM admin.Companies WHERE TenantId=@tenant AND IsActive=1 ORDER BY CreatedOn;",connection);
        command.Parameters.AddWithValue("@tenant",tenantId);var store=await command.ExecuteScalarAsync(token) as string??"WhatsBiz Store";
        const string fallback="Reply with:\n1. Browse Products\n2. Search <name or code>\n3. My Cart\n4. Order Status\n\nReply ADD <product code> <quantity> to add an item, or CONFIRM to place your order.";
        return new(WhatsAppCommerceMessageKinds.InteractiveMenu,$"Welcome to {store}. What would you like to do?",store,
        [
            new(WhatsAppCommerceActionIds.Browse,"Browse Products","View products currently in stock"),
            new(WhatsAppCommerceActionIds.Search,"Search Products","Search by product name or code"),
            new(WhatsAppCommerceActionIds.Cart,"My Cart","Review your saved cart"),
            new(WhatsAppCommerceActionIds.Orders,"My Orders","Check your latest order")
        ],FallbackText:$"Welcome to {store}.\n\n{fallback}");
    }

    private async Task SetState(Guid tenantId,Guid conversationId,string state,CancellationToken token)
    {
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);
        await using var command=new SqlCommand("UPDATE integration.WhatsAppCommerceConversations SET State=@state,UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND ConversationId=@conversation;",connection);
        command.Parameters.AddWithValue("@state",state);command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@conversation",conversationId);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task AddMappedProductsToCart(Guid tenantId,Conversation conversation,string? catalogId,
        IReadOnlyCollection<WhatsAppCommerceInboundProduct> products,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(catalogId)||products.Count==0||products.Count>10)throw new BusinessRuleException("The WhatsApp product selection is invalid.");
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        foreach(var item in products)
        {
            await using var query=new SqlCommand("""
                SELECT TOP(1) p.ProductId,p.SellingPrice,p.GSTPercentage,
                    ISNULL((SELECT SUM(b.QuantityAvailable) FROM inventory.InventoryBalances b
                            WHERE b.TenantId=@tenant AND b.WarehouseId=@warehouse AND b.ProductId=p.ProductId),0)
                FROM commerce.ProductChannelMappings m
                JOIN master.Products p ON p.ProductId=m.ProductId AND p.TenantId=m.TenantId
                WHERE m.TenantId=@tenant AND m.Provider=N'META' AND m.SyncStatus=N'MAPPED'
                  AND m.CatalogId=@catalog AND m.ExternalProductId=@external
                  AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1;
                """,connection,transaction);
            query.Parameters.AddWithValue("@tenant",tenantId);query.Parameters.AddWithValue("@warehouse",conversation.WarehouseId);
            query.Parameters.AddWithValue("@catalog",catalogId);query.Parameters.AddWithValue("@external",item.ProductRetailerId);
            Guid productId;decimal price;decimal tax;decimal available;
            await using(var reader=await query.ExecuteReaderAsync(token))
            {
                if(!await reader.ReadAsync(token))throw new BusinessRuleException("A selected WhatsApp product is not available for this retailer.");
                productId=reader.GetGuid(0);price=reader.GetDecimal(1);tax=reader.GetDecimal(2);available=reader.GetDecimal(3);
            }
            if(available<item.Quantity)throw new BusinessRuleException("A selected WhatsApp product does not have enough stock.");
            await using var upsert=new SqlCommand("MERGE integration.WhatsAppCommerceCartLines AS t USING (SELECT @conversation ConversationId,@tenant TenantId,@product ProductId) AS s ON t.ConversationId=s.ConversationId AND t.ProductId=s.ProductId WHEN MATCHED THEN UPDATE SET Quantity=t.Quantity+@quantity,UnitPrice=@price,TaxPercentage=@tax,UpdatedOn=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT(ConversationId,TenantId,ProductId,Quantity,UnitPrice,TaxPercentage) VALUES(@conversation,@tenant,@product,@quantity,@price,@tax);",connection,transaction);
            upsert.Parameters.AddWithValue("@conversation",conversation.ConversationId);upsert.Parameters.AddWithValue("@tenant",tenantId);upsert.Parameters.AddWithValue("@product",productId);
            upsert.Parameters.AddWithValue("@quantity",item.Quantity);upsert.Parameters.AddWithValue("@price",price);upsert.Parameters.AddWithValue("@tax",tax);await upsert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    private static WhatsAppCommerceOutboundMessage Text(string value)=>new(WhatsAppCommerceMessageKinds.Text,value,FallbackText:value);

    private async Task<WhatsAppCommerceOutboundMessage> Confirm(Guid tenantId, Conversation c, string messageId, CancellationToken token)
    {
        if (c.CustomerId is not Guid customer) return Text("Please provide or link a customer account before confirming this order.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var claim = new SqlCommand("UPDATE integration.WhatsAppCommerceConversations SET PendingOrderStatus=N'PROCESSING',UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND ConversationId=@conversation AND PendingOrderStatus IN(N'NONE',N'FAILED') AND OrderId IS NULL;", connection);
        claim.Parameters.AddWithValue("@tenant", tenantId); claim.Parameters.AddWithValue("@conversation", c.ConversationId);
        if (await claim.ExecuteNonQueryAsync(token) != 1) return Text("Your order is already being processed. Reply ORDER STATUS shortly.");
        var lines = new List<object>();
        await using (var q = new SqlCommand("SELECT ProductId,Quantity,UnitPrice,TaxPercentage FROM integration.WhatsAppCommerceCartLines WHERE TenantId=@tenant AND ConversationId=@conversation;", connection))
        { q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@conversation", c.ConversationId); await using var r = await q.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) lines.Add(new { ProductId = r.GetGuid(0), Quantity = r.GetDecimal(1), UnitPrice = r.GetDecimal(2), TaxPercentage = r.GetDecimal(3), Barcode = (string?)null, DiscountPercentage = 0m, DiscountAmount = 0m }); }
        if (lines.Count == 0) { await FailAsync(tenantId, c.ConversationId, messageId, "Cart is empty.", token); return Text("Your cart is empty. Reply BROWSE to see products."); }
        var request = new POSPostRequest(null, null, customer, c.WarehouseId, null, JsonSerializer.Serialize(lines), "[]", 0, 0, "WhatsApp LIVE order", "HELD", false, null, "WHATSAPP", tenantId, "WHATSAPP");
        try
        {
            var posted = await pos.PostForTenant(request, tenantId, $"WA:{tenantId:N}:{c.ConversationId:N}:{messageId}", token);
            await using var update = new SqlCommand("UPDATE integration.WhatsAppCommerceConversations SET PendingOrderStatus=N'COMPLETED',OrderId=@order,CartStatus=N'ORDERED',State=N'MENU',LastResult=@result,UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND ConversationId=@conversation;", connection);
            update.Parameters.AddWithValue("@order", posted.InvoiceId); update.Parameters.AddWithValue("@result", posted.InvoiceNumber); update.Parameters.AddWithValue("@tenant", tenantId); update.Parameters.AddWithValue("@conversation", c.ConversationId); await update.ExecuteNonQueryAsync(token);
            var methods = await PaymentService.GetEnabledMethodsForTenantAsync(tenantId, token);
            var body = $"Order confirmed.\nOrder: {posted.InvoiceNumber}\nAmount: ₹{posted.GrandTotal:0.00}\nOrder status: {posted.Status}\nPayment status: Pending";
            var actions = methods.Select(x => x.Provider switch
            {
                PaymentProviders.Razorpay => new WhatsAppCommerceAction(WhatsAppCommerceActionIds.PayRazorpay, "Pay Online"),
                PaymentProviders.DirectUpi => new WhatsAppCommerceAction(WhatsAppCommerceActionIds.PayUpi, "Pay via UPI"),
                _ => new WhatsAppCommerceAction(WhatsAppCommerceActionIds.PayCod, "Cash on Delivery")
            }).Take(3).ToArray();
            var fallback = actions.Length == 0 ? $"{body}\n\nNo payment method is currently enabled; please contact the retailer."
                : $"{body}\n\nChoose payment method:\n" + string.Join('\n', methods.Select(x => x.Provider switch
                { PaymentProviders.Razorpay => "PAY RAZORPAY - Pay Online", PaymentProviders.DirectUpi => "PAY UPI - Direct UPI", _ => "COD - Cash on Delivery" }));
            logger.LogInformation("WhatsApp commerce tenant {TenantId} confirmed order {OrderReference}", tenantId, posted.InvoiceNumber);
            return actions.Length == 0 ? Text(fallback)
                : new(WhatsAppCommerceMessageKinds.InteractiveButtons, body, Actions: actions, FallbackText: fallback);
        }
        catch { await FailAsync(tenantId, c.ConversationId, messageId, "Order could not be posted.", token); throw; }
    }

    private async Task<string> CreatePayment(Guid tenantId, Guid orderId, string provider, CancellationToken token)
    {
        var enabled = await PaymentService.GetEnabledMethodsForTenantAsync(tenantId, token);
        if (enabled.All(x => x.Provider != provider)) return "That payment method is not enabled for this retailer.";
        var attempt = await PaymentService.CreateAttemptForTenantAsync(tenantId, new(orderId, provider), "WHATSAPP", token);
        return provider switch
        {
            PaymentProviders.Razorpay => $"Pay ₹{attempt.Payment.Amount:0.00} securely to the retailer:\n{attempt.PaymentAction}\n\nPayment is confirmed only after Razorpay verifies it.",
            PaymentProviders.DirectUpi => $"Pay ₹{attempt.Payment.Amount:0.00} directly to the retailer:\n{attempt.PaymentAction}\n\nPayment remains pending until the retailer verifies receipt.",
            _ => "Cash on Delivery selected. Payment will be collected during delivery."
        };
    }

    private async Task<string> AddToCart(Guid tenantId, Conversation c, string code, decimal quantity, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var q = new SqlCommand("SELECT TOP(1)p.ProductId,p.ProductName,p.SellingPrice,p.GSTPercentage FROM master.Products p WHERE p.TenantId=@tenant AND p.ProductCode=@code AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1;", connection); q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@code", code);
        await using var r = await q.ExecuteReaderAsync(token); if (!await r.ReadAsync(token)) return "Product code was not found or is not available."; var id = r.GetGuid(0); var name = r.GetString(1); var price = r.GetDecimal(2); var tax = r.GetDecimal(3);
        await r.CloseAsync(); await using var stock = new SqlCommand("SELECT ISNULL(SUM(QuantityAvailable),0) FROM inventory.InventoryBalances WHERE TenantId=@tenant AND WarehouseId=@warehouse AND ProductId=@product;", connection); stock.Parameters.AddWithValue("@tenant", tenantId); stock.Parameters.AddWithValue("@warehouse", c.WarehouseId); stock.Parameters.AddWithValue("@product", id); if (Convert.ToDecimal(await stock.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) < quantity) return "The requested quantity is not currently available.";
        await using var upsert = new SqlCommand("MERGE integration.WhatsAppCommerceCartLines AS t USING (SELECT @conversation ConversationId,@tenant TenantId,@product ProductId) AS s ON t.ConversationId=s.ConversationId AND t.ProductId=s.ProductId WHEN MATCHED THEN UPDATE SET Quantity=t.Quantity+@quantity,UpdatedOn=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT(ConversationId,TenantId,ProductId,Quantity,UnitPrice,TaxPercentage) VALUES(@conversation,@tenant,@product,@quantity,@price,@tax);", connection); upsert.Parameters.AddWithValue("@conversation", c.ConversationId); upsert.Parameters.AddWithValue("@tenant", tenantId); upsert.Parameters.AddWithValue("@product", id); upsert.Parameters.AddWithValue("@quantity", quantity); upsert.Parameters.AddWithValue("@price", price); upsert.Parameters.AddWithValue("@tax", tax); await upsert.ExecuteNonQueryAsync(token); return $"Added {quantity:0.####} × {name} to your cart. Reply CART to review or CONFIRM to place the order.";
    }

    private async Task<WhatsAppCommerceOutboundMessage> ProductList(Guid tenantId, Guid warehouse, string? search, CancellationToken token)
    {
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        await using var q = new SqlCommand("""
            SELECT TOP(10) p.ProductId,p.ProductCode,p.ProductName,p.SellingPrice,ISNULL(SUM(b.QuantityAvailable),0),
                   CASE WHEN m.SyncStatus=N'MAPPED' THEN m.CatalogId END,
                   CASE WHEN m.SyncStatus=N'MAPPED' THEN m.ExternalProductId END
            FROM master.Products p
            LEFT JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId AND b.TenantId=@tenant AND b.WarehouseId=@warehouse
            LEFT JOIN commerce.ProductChannelMappings m ON m.TenantId=@tenant AND m.ProductId=p.ProductId AND m.Provider=N'META'
            WHERE p.TenantId=@tenant AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1
              AND (@search IS NULL OR p.ProductCode LIKE N'%'+@search+N'%' OR p.ProductName LIKE N'%'+@search+N'%')
            GROUP BY p.ProductId,p.ProductCode,p.ProductName,p.SellingPrice,m.SyncStatus,m.CatalogId,m.ExternalProductId
            HAVING ISNULL(SUM(b.QuantityAvailable),0)>0
            ORDER BY p.ProductName;
            """, c);
        q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@warehouse",warehouse);q.Parameters.AddWithValue("@search",(object?)search??DBNull.Value);
        await using var r=await q.ExecuteReaderAsync(token);var rows=new List<ProductResult>();
        while(await r.ReadAsync(token))rows.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetDecimal(3),r.GetDecimal(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6)));
        if(rows.Count==0)return Text("No products are currently available.");
        var fallback="Products:\n"+string.Join('\n',rows.Select(x=>$"{x.Code} - {x.Name} ₹{x.Price:0.00} ({x.Available:0.####} available)"))+"\n\nReply ADD <product code> <quantity>.";
        var mapped=rows.Where(x=>!string.IsNullOrWhiteSpace(x.CatalogId)&&!string.IsNullOrWhiteSpace(x.ExternalProductId)).ToArray();
        var oneCatalog=mapped.Length>0&&mapped.Select(x=>x.CatalogId).Distinct(StringComparer.Ordinal).Count()==1;
        logger.LogInformation("WhatsApp commerce tenant {TenantId} native eligibility {Eligible}; mapped {MappedCount}; unmapped {UnmappedCount}",tenantId,oneCatalog,mapped.Length,rows.Count-mapped.Length);
        if(!oneCatalog)return Text(fallback);
        var products=mapped.Select(x=>new WhatsAppCommerceProductMessage(x.ProductId,x.Name,x.Code,x.Price,null,x.CatalogId,x.ExternalProductId)).ToArray();
        var title=string.IsNullOrWhiteSpace(search)?"Available products":$"Results for {search}";
        return new(products.Length==1?WhatsAppCommerceMessageKinds.Product:WhatsAppCommerceMessageKinds.ProductList,
            "Select a product to view it in WhatsApp. Use SEARCH <term> to narrow results.",title,Products:products,FallbackText:fallback);
    }
    private async Task<WhatsAppCommerceOutboundMessage> CartMessage(Guid tenantId, Guid conversation, CancellationToken token)
    {
        var text=await Cart(tenantId,conversation,token);
        if(text=="Your cart is empty.")return Text(text);
        return new(WhatsAppCommerceMessageKinds.InteractiveButtons,text,Actions:
        [new(WhatsAppCommerceActionIds.ContinueShopping,"Continue Shopping"),new(WhatsAppCommerceActionIds.Checkout,"Checkout")],FallbackText:text);
    }
    private async Task<string> Cart(Guid tenantId, Guid conversation, CancellationToken token)
    { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT p.ProductName,l.Quantity,l.UnitPrice FROM integration.WhatsAppCommerceCartLines l JOIN master.Products p ON p.ProductId=l.ProductId AND p.TenantId=l.TenantId WHERE l.TenantId=@tenant AND l.ConversationId=@conversation;",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@conversation",conversation);await using var r=await q.ExecuteReaderAsync(token);var rows=new List<string>();decimal total=0;while(await r.ReadAsync(token)){var line=r.GetDecimal(1)*r.GetDecimal(2);total+=line;rows.Add($"{r.GetString(0)} × {r.GetDecimal(1):0.####} = ₹{line:0.00}");}return rows.Count==0?"Your cart is empty.":"Your cart:\n"+string.Join('\n',rows)+$"\n\nTotal: ₹{total:0.00}\nReply CONFIRM to place the order."; }
    private async Task<string> OrderStatus(Guid tenantId, Guid? customer, CancellationToken token) { if (customer is not Guid id) return "No customer account is linked to this WhatsApp number yet."; await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TOP(1)i.InvoiceNumber,i.Status FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId AND i.TenantId=w.TenantId WHERE w.TenantId=@tenant AND i.CustomerId=@customer ORDER BY i.InvoiceDate DESC;",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@customer",id);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?$"Latest order {r.GetString(0)}: {r.GetString(1)}":"No WhatsApp orders found."; }
    private async Task<string> ExistingOrder(Guid tenantId, Guid id, CancellationToken token) { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TOP(1)InvoiceNumber,GrandTotal FROM sales.SalesInvoices WHERE TenantId=@tenant AND InvoiceId=@id;",c);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@id",id);await using var r=await q.ExecuteReaderAsync(token);return await r.ReadAsync(token)?$"Order already confirmed.\nOrder: {r.GetString(0)}\nAmount: ₹{r.GetDecimal(1):0.00}":"Order status is unavailable."; }

    private async Task<Conversation?> BeginAsync(Guid tenantId,string mobile,string messageId,CancellationToken token)
    { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var t=await c.BeginTransactionAsync(token);await using var i=new SqlCommand("INSERT INTO integration.WhatsAppCommerceInbound(TenantId,MetaMessageId,Status,UpdatedOn) VALUES(@tenant,@message,N'PROCESSING',SYSUTCDATETIME());",c,(SqlTransaction)t);i.Parameters.AddWithValue("@tenant",tenantId);i.Parameters.AddWithValue("@message",messageId);try{await i.ExecuteNonQueryAsync(token);}catch(SqlException e)when(e.Number is 2601 or 2627){return null;}await using var q=new SqlCommand("SELECT TOP(1)ConversationId,CustomerId,WarehouseId,OrderId,State FROM integration.WhatsAppCommerceConversations WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND NormalizedMobile=@mobile;",c,(SqlTransaction)t);q.Parameters.AddWithValue("@tenant",tenantId);q.Parameters.AddWithValue("@mobile",mobile);Guid conversation;Guid? customer;Guid warehouse;Guid? orderId;string state;if(await q.ExecuteReaderAsync(token) is not SqlDataReader r) return null;await using(r){if(await r.ReadAsync(token)){conversation=r.GetGuid(0);customer=r.IsDBNull(1)?null:r.GetGuid(1);warehouse=r.IsDBNull(2)?Guid.Empty:r.GetGuid(2);orderId=r.IsDBNull(3)?null:r.GetGuid(3);state=r.GetString(4);}else{conversation=Guid.Empty;customer=null;warehouse=Guid.Empty;orderId=null;state="MENU";}}if(warehouse==Guid.Empty){await using var w=new SqlCommand("SELECT TOP(1)WarehouseId FROM inventory.Warehouses WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0 ORDER BY IsDefault DESC,WarehouseName;",c,(SqlTransaction)t);w.Parameters.AddWithValue("@tenant",tenantId);warehouse=(Guid?)await w.ExecuteScalarAsync(token)??Guid.Empty;}if(warehouse==Guid.Empty)throw new BusinessRuleException("No active warehouse is configured for this tenant.");if(customer is null){await using var cu=new SqlCommand("SELECT TOP(1)CustomerId FROM sales.Customers WHERE TenantId=@tenant AND IsDeleted=0 AND Mobile IS NOT NULL AND RIGHT(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),10)=RIGHT(@mobile,10) ORDER BY IsActive DESC,CreatedOn;",c,(SqlTransaction)t);cu.Parameters.AddWithValue("@tenant",tenantId);cu.Parameters.AddWithValue("@mobile",mobile);customer=(Guid?)await cu.ExecuteScalarAsync(token);}if(conversation==Guid.Empty){conversation=Guid.NewGuid();await using var n=new SqlCommand("INSERT integration.WhatsAppCommerceConversations(ConversationId,TenantId,CustomerId,NormalizedMobile,WarehouseId) VALUES(@id,@tenant,@customer,@mobile,@warehouse);",c,(SqlTransaction)t);n.Parameters.AddWithValue("@id",conversation);n.Parameters.AddWithValue("@tenant",tenantId);n.Parameters.AddWithValue("@customer",(object?)customer??DBNull.Value);n.Parameters.AddWithValue("@mobile",mobile);n.Parameters.AddWithValue("@warehouse",warehouse);await n.ExecuteNonQueryAsync(token);}await using var u=new SqlCommand("UPDATE integration.WhatsAppCommerceInbound SET ConversationId=@conversation WHERE TenantId=@tenant AND MetaMessageId=@message;",c,(SqlTransaction)t);u.Parameters.AddWithValue("@conversation",conversation);u.Parameters.AddWithValue("@tenant",tenantId);u.Parameters.AddWithValue("@message",messageId);await u.ExecuteNonQueryAsync(token);await t.CommitAsync(token);return new(conversation,customer,warehouse,orderId,state); }
    private async Task SendAsync(Guid tenantId,string webhookPhone,string recipient,string messageId,WhatsAppCommerceOutboundMessage message,CancellationToken token)
    {
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);
        await using var q=new SqlCommand("SELECT TOP(1)ApiVersion,PhoneNumberId,AccessTokenProtected,WhatsAppBusinessAccountId FROM integration.WhatsAppConfigurations WHERE TenantId=@tenant AND ProviderMode=N'LIVE' AND IsEnabled=1 AND ConnectionStatus=N'CONNECTED';",c);
        q.Parameters.AddWithValue("@tenant",tenantId);await using var r=await q.ExecuteReaderAsync(token);
        if(!await r.ReadAsync(token))throw new BusinessRuleException("LIVE WhatsApp is not configured for this tenant.");
        var version=r.IsDBNull(0)?null:r.GetString(0);var phoneId=r.IsDBNull(1)?null:r.GetString(1);var protectedToken=r.IsDBNull(2)?null:r.GetString(2);var wabaId=r.IsDBNull(3)?null:r.GetString(3);
        if(version is null||phoneId is null||protectedToken is null||string.IsNullOrWhiteSpace(wabaId)||!phoneId.Equals(webhookPhone,StringComparison.Ordinal))
            throw new BusinessRuleException("LIVE WhatsApp configuration does not match the webhook phone asset.");
        var tokenValue=protection.CreateProtector("WhatsBiz.WhatsApp.Secrets.v1").Unprotect(protectedToken);
        var sent=await providers.Resolve(WhatsAppProviderModes.Live).SendCommerceAsync(new(version,phoneId,tokenValue,recipient,message),token);
        await r.CloseAsync();var storedText=message.FallbackText??message.Body;
        await using var o=new SqlCommand("INSERT integration.WhatsAppCommerceOutbound(WhatsAppCommerceOutboundId,TenantId,MetaMessageId,RecipientNumber,MessageText,Status,ProviderMessageId,Attempts,LastError) VALUES(NEWID(),@tenant,@message,@recipient,@text,@status,@provider,1,@error);",c);
        o.Parameters.AddWithValue("@tenant",tenantId);o.Parameters.AddWithValue("@message",messageId);o.Parameters.AddWithValue("@recipient",recipient);o.Parameters.AddWithValue("@text",storedText);o.Parameters.AddWithValue("@status",sent.Succeeded?"SENT":"FAILED");o.Parameters.AddWithValue("@provider",(object?)sent.ProviderMessageId??DBNull.Value);o.Parameters.AddWithValue("@error",(object?)sent.SafeMessage??DBNull.Value);await o.ExecuteNonQueryAsync(token);
        logger.LogInformation("WhatsApp commerce tenant {TenantId} outbound type {MessageType} result {Result} Meta message {MetaMessageId}",tenantId,message.Kind,sent.Succeeded?"SENT":"FAILED",sent.ProviderMessageId);
        if(sent.Succeeded&&sent.ProviderMessageId is not null&&usageBilling is not null)await usageBilling.RecordAcceptedAsync(new(tenantId,WhatsAppProviderModes.Live,phoneId,sent.ProviderMessageId,recipient,null,WhatsAppMessageCategories.Unknown,sent.AttemptedAt),token);
        if(!sent.Succeeded)throw new BusinessRuleException("The commerce action was processed, but the WhatsApp reply could not be delivered.");
    }
    private async Task CompleteAsync(Guid tenant,Guid conversation,string message,string status,string result,CancellationToken token){await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("UPDATE integration.WhatsAppCommerceInbound SET Status=@status,ResultMessage=@result,UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND MetaMessageId=@message;UPDATE integration.WhatsAppCommerceConversations SET LastInboundMessageId=@message,LastResult=@result,UpdatedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND ConversationId=@conversation;",c);q.Parameters.AddWithValue("@status",status);q.Parameters.AddWithValue("@result",result[..Math.Min(result.Length,500)]);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@message",message);q.Parameters.AddWithValue("@conversation",conversation);await q.ExecuteNonQueryAsync(token);}
    private Task FailAsync(Guid tenant,Guid conversation,string message,string error,CancellationToken token)=>CompleteAsync(tenant,conversation,message,"FAILED",error,token);
    private static string SafeCommandName(string? value)
    {
        var command=value?.Trim();if(string.IsNullOrWhiteSpace(command))return "EMPTY";
        var first=command.Split(' ',2,StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
        return first is "HI" or "HELLO" or "MENU" or "BROWSE" or "SEARCH" or "ADD" or "CART" or "ORDER" or "CONFIRM" or "PAY" or "COD" ? first : "TEXT_COMMAND";
    }
    private ICommercePaymentService PaymentService => payments ?? throw new BusinessRuleException("Payment services are unavailable.");
    private async Task RetryFailedAsync(Guid tenant,string message,CancellationToken token)
    { await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("DELETE FROM integration.WhatsAppCommerceInbound WHERE TenantId=@tenant AND MetaMessageId=@message AND Status=N'FAILED';",c);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@message",message);await q.ExecuteNonQueryAsync(token); }
    private sealed record Conversation(Guid ConversationId,Guid? CustomerId,Guid WarehouseId,Guid? OrderId,string State);
    private sealed record ProductResult(Guid ProductId,string Code,string Name,decimal Price,decimal Available,string? CatalogId,string? ExternalProductId);
}
