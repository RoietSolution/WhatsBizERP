using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

public sealed class WhatsAppCommerceService(IConfiguration configuration, IPOSEngine pos,
    IWhatsAppCommerceProviderResolver providers, IFeatureService features) : IWhatsAppCommerceService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<WhatsAppCommerceSetup> GetSetupAsync(Guid tenantId, Guid? warehouseId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var mode = await ProviderMode(connection, tenantId, token); var provider = providers.Resolve(mode);
        var storeName = await Scalar<string>(connection, "SELECT TOP(1) CompanyName FROM admin.Companies WHERE IsActive=1 ORDER BY CreatedOn;", token) ?? "WhatsBiz Store";
        var customers = new List<WhatsAppCommerceCustomer>();
        await using (var command = new SqlCommand("SELECT TOP(100) CustomerId,CustomerCode,CustomerName,Mobile FROM sales.Customers WHERE IsActive=1 AND IsDeleted=0 ORDER BY CustomerName;", connection))
        await using (var reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) customers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        var warehouses = new List<WhatsAppCommerceWarehouse>();
        await using (var command = new SqlCommand("SELECT WarehouseId,WarehouseCode,WarehouseName FROM inventory.Warehouses WHERE IsActive=1 AND IsDeleted=0 ORDER BY IsDefault DESC,WarehouseName;", connection))
        await using (var reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) warehouses.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        var selectedWarehouse = warehouseId ?? warehouses.FirstOrDefault()?.WarehouseId;
        var products = selectedWarehouse.HasValue ? await Products(connection, selectedWarehouse.Value, token) : [];
        return new(mode, storeName, customers, warehouses, products, await provider.SendWelcomeAsync(storeName, token));
    }

    public async Task<WhatsAppCommerceCart> CalculateCartAsync(Guid tenantId, Guid warehouseId, IReadOnlyCollection<WhatsAppCommerceCartItem> items, CancellationToken token)
    {
        if (items.Count == 0) return new(warehouseId, [], 0, 0, 0);
        if (items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0)) throw new BusinessRuleException("Cart quantities must be greater than zero.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token); _ = await ProviderMode(connection, tenantId, token);
        var catalogue = (await Products(connection, warehouseId, token)).ToDictionary(x => x.ProductId);
        var lines = new List<WhatsAppCommerceCartLine>();
        foreach (var item in items.GroupBy(x => x.ProductId).Select(x => new WhatsAppCommerceCartItem(x.Key, x.Sum(y => y.Quantity))))
        {
            if (!catalogue.TryGetValue(item.ProductId, out var product)) throw new BusinessRuleException("A cart product is unavailable for the selected warehouse.");
            if (item.Quantity > product.AvailableQuantity) throw new BusinessRuleException($"Only {product.AvailableQuantity:0.####} units of {product.ProductName} are available.");
            var subtotal = decimal.Round(item.Quantity * product.SellingPrice, 2);
            var tax = decimal.Round(subtotal * product.TaxPercentage / 100m, 2);
            lines.Add(new(product.ProductId, product.ProductCode, product.ProductName, product.ImageUrl, item.Quantity,
                product.SellingPrice, product.TaxPercentage, tax, subtotal + tax, product.AvailableQuantity));
        }
        return new(warehouseId, lines, lines.Sum(x => x.Quantity * x.UnitPrice), lines.Sum(x => x.TaxAmount), lines.Sum(x => x.LineTotal));
    }

    public async Task<WhatsAppCommerceOrderResult> PlaceOrderAsync(Guid tenantId, PlaceWhatsAppDemoOrderInput input, string? actor, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var mode = await ProviderMode(connection, tenantId, token); var provider = providers.Resolve(mode);
        if (mode != "MOCK") throw new BusinessRuleException("Only MOCK mode order placement is implemented.");
        await using (var customer = new SqlCommand("SELECT COUNT(1) FROM sales.Customers WHERE CustomerId=@id AND IsActive=1 AND IsDeleted=0;", connection))
        { customer.Parameters.AddWithValue("@id", input.CustomerId); if (Convert.ToInt32(await customer.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new BusinessRuleException("An active customer is required."); }
        var cart = await CalculateCartAsync(tenantId, input.WarehouseId, input.Items, token);
        if (cart.Items.Count == 0) throw new BusinessRuleException("The cart is empty.");
        var posItems = cart.Items.Select(x => new POSItemInput(x.ProductId, null, x.Quantity, x.UnitPrice, 0, 0, x.TaxPercentage)).ToArray();
        var result = await pos.Post(new(null, null, input.CustomerId, input.WarehouseId, null,
            JsonSerializer.Serialize(posItems), "[]", 0, 0, "WhatsApp Commerce MOCK order",
            "HELD", false, null, actor, tenantId, "WHATSAPP_DEMO"), token);
        return new(result.InvoiceId, result.InvoiceNumber, result.Status, result.GrandTotal,
            await provider.SendOrderConfirmationAsync(result.InvoiceNumber, result.GrandTotal, token));
    }

    public async Task<WhatsAppCommerceReadiness> GetReadinessAsync(Guid tenantId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        async Task<bool> Exists(string sql)
        { await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@tenant", tenantId); return Convert.ToInt32(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture) > 0; }
        var checks = new List<WhatsAppCommerceReadinessCheck>
        {
            new("featureEnabled", "WhatsApp Commerce enabled", await features.IsEnabledAsync(tenantId, FeatureKeys.WhatsAppCommerce, token), null, "Ask an administrator to enable WhatsApp Commerce."),
            new("mockProviderConfigured", "MOCK provider configured", await Exists("SELECT COUNT(1) FROM integration.WhatsAppConfigurations WHERE TenantId=@tenant AND IsEnabled=1 AND ProviderMode='MOCK';"), "/admin/whatsapp", "Open WhatsApp settings and enable Demo Mode."),
            new("customerAvailable", "Customer available", await Exists("SELECT COUNT(1) FROM sales.Customers WHERE IsActive=1 AND IsDeleted=0;"), "/customers", "Create or activate a customer."),
            new("warehouseAvailable", "Warehouse available", await Exists("SELECT COUNT(1) FROM inventory.Warehouses WHERE IsActive=1 AND IsDeleted=0;"), "/warehouses", "Create or activate a warehouse."),
            new("invoiceSeriesAvailable", "Invoice series configured", await Exists("SELECT COUNT(1) FROM sales.InvoiceSeries WHERE IsActive=1 AND IsDefault=1;"), "/admin/settings", "Configure an active default invoice series."),
            new("productsAvailable", "Products available", await Exists("SELECT COUNT(1) FROM master.Products WHERE IsActive=1 AND IsDeleted=0;"), "/products", "Create or activate products."),
            new("stockAvailable", "Stock available", await Exists("SELECT COUNT(1) FROM inventory.InventoryBalances b JOIN master.Products p ON p.ProductId=b.ProductId JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId WHERE p.IsActive=1 AND p.IsDeleted=0 AND w.IsActive=1 AND w.IsDeleted=0 AND b.QuantityAvailable>0;"), "/inventory", "Receive or adjust stock for an active product.")
        };
        return new(checks.All(x => x.Ready), checks);
    }

    public async Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> GetOrdersAsync(Guid tenantId, Guid customerId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var rows = new List<WhatsAppCommerceOrderSummary>();
        await using var command = new SqlCommand("SELECT TOP(50)i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.Status,w.SourceChannel,w.ProviderMode FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId WHERE w.TenantId=@tenant AND i.CustomerId=@customer ORDER BY i.InvoiceDate DESC;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@customer", customerId);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(Summary(reader));
        return rows;
    }

    public async Task<WhatsAppCommerceOrderDetails> GetOrderAsync(Guid tenantId, Guid customerId, Guid orderId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        WhatsAppCommerceOrderSummary? order;
        await using (var command = new SqlCommand("SELECT i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.Status,w.SourceChannel,w.ProviderMode FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId WHERE w.TenantId=@tenant AND i.CustomerId=@customer AND i.InvoiceId=@invoice;", connection))
        { command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@customer", customerId); command.Parameters.AddWithValue("@invoice", orderId); await using var reader = await command.ExecuteReaderAsync(token); order = await reader.ReadAsync(token) ? Summary(reader) : null; }
        if (order is null) throw new BusinessRuleException("Demo order was not found.");
        var items = new List<WhatsAppCommerceCartLine>();
        await using (var command = new SqlCommand("SELECT x.ProductId,p.ProductCode,p.ProductName,p.ImageUrl,x.Quantity,x.UnitPrice,x.TaxPercentage,x.TaxAmount,x.LineTotal,ISNULL(SUM(b.QuantityAvailable),0) FROM sales.SalesInvoiceItems x JOIN master.Products p ON p.ProductId=x.ProductId LEFT JOIN sales.SalesInvoices i ON i.InvoiceId=x.InvoiceId LEFT JOIN inventory.InventoryBalances b ON b.ProductId=x.ProductId AND b.WarehouseId=i.WarehouseId WHERE x.InvoiceId=@invoice GROUP BY x.ProductId,p.ProductCode,p.ProductName,p.ImageUrl,x.Quantity,x.UnitPrice,x.TaxPercentage,x.TaxAmount,x.LineTotal;", connection))
        { command.Parameters.AddWithValue("@invoice", orderId); await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetDecimal(9))); }
        return new(order, items);
    }

    public async Task<IReadOnlyCollection<WhatsAppCommerceMessage>> GetStatusNotificationsAsync(Guid tenantId, Guid customerId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var mode = await ProviderMode(connection, tenantId, token); var provider = providers.Resolve(mode);
        var changes = new List<(Guid LinkId,string Number,string Status)>();
        await using (var command = new SqlCommand("SELECT w.WhatsAppCommerceOrderId,i.InvoiceNumber,i.Status FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId WHERE w.TenantId=@tenant AND i.CustomerId=@customer AND ISNULL(w.LastNotifiedErpStatus,'')<>i.Status ORDER BY i.InvoiceDate;", connection))
        { command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@customer",customerId);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))changes.Add((reader.GetGuid(0),reader.GetString(1),reader.GetString(2))); }
        var messages = new List<WhatsAppCommerceMessage>();
        foreach (var change in changes)
        {
            await using var update = new SqlCommand("UPDATE integration.WhatsAppCommerceOrders SET LastNotifiedErpStatus=@status,LastNotifiedOn=SYSUTCDATETIME() WHERE WhatsAppCommerceOrderId=@id AND ISNULL(LastNotifiedErpStatus,'')<>@status;", connection);
            update.Parameters.AddWithValue("@status",change.Status);update.Parameters.AddWithValue("@id",change.LinkId);
            if (await update.ExecuteNonQueryAsync(token)==1) messages.AddRange(await provider.SendOrderStatusAsync(change.Number, NotificationStatus(change.Status), token));
        }
        return messages;
    }

    private static WhatsAppCommerceOrderSummary Summary(SqlDataReader reader)
    { var status=reader.GetString(4);return new(reader.GetGuid(0),reader.GetString(1),reader.GetDateTimeOffset(2),reader.GetDecimal(3),status,DisplayStatus(status),reader.GetString(5),reader.GetString(6)); }
    public static string DisplayStatus(string status) => status.ToUpperInvariant() switch { "HELD" or "SUSPENDED" => "Order Confirmed", "COMPLETED" => "Completed", "CANCELLED" or "VOID" => "Cancelled", "RETURNED" => "Returned", "PARTIALLY_RETURNED" => "Partially Returned", _ => status };
    private static string NotificationStatus(string status) => status.ToUpperInvariant().Replace('_',' ');

    private static async Task<IReadOnlyCollection<WhatsAppCommerceProduct>> Products(SqlConnection connection, Guid warehouseId, CancellationToken token)
    {
        var rows = new List<WhatsAppCommerceProduct>();
        await using var command = new SqlCommand("""
            SELECT p.ProductId,p.ProductCode,p.Barcode,p.ProductName,p.ShortDescription,p.ImageUrl,p.SellingPrice,p.GSTPercentage,
                   ISNULL(SUM(b.QuantityAvailable),0) AvailableQuantity
            FROM master.Products p LEFT JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId AND b.WarehouseId=@warehouse
            WHERE p.IsActive=1 AND p.IsDeleted=0
            GROUP BY p.ProductId,p.ProductCode,p.Barcode,p.ProductName,p.ShortDescription,p.ImageUrl,p.SellingPrice,p.GSTPercentage
            HAVING ISNULL(SUM(b.QuantityAvailable),0)>0 ORDER BY p.ProductName;
            """, connection);
        command.Parameters.AddWithValue("@warehouse", warehouseId); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8)));
        return rows;
    }
    private static async Task<string> ProviderMode(SqlConnection connection, Guid tenantId, CancellationToken token)
    { await using var command = new SqlCommand("SELECT ProviderMode FROM integration.WhatsAppConfigurations WHERE TenantId=@tenant AND IsEnabled=1;", connection); command.Parameters.AddWithValue("@tenant", tenantId); return await command.ExecuteScalarAsync(token) as string ?? throw new BusinessRuleException("WhatsApp Commerce is not enabled or configured for this tenant."); }
    private static async Task<T?> Scalar<T>(SqlConnection connection, string sql, CancellationToken token)
    { await using var command = new SqlCommand(sql, connection); var value = await command.ExecuteScalarAsync(token); return value is null or DBNull ? default : (T)value; }
}
