using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsAppCommerce;

public sealed partial class WhatsAppCommerceService(IConfiguration configuration, IPOSEngine pos,
    IWhatsAppCommerceProviderResolver providers, IFeatureService features, IDataProtectionProvider protection) : IWhatsAppCommerceService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<WhatsAppCommerceSetup> GetSetupAsync(Guid tenantId, Guid? warehouseId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var mode = await ProviderMode(connection, tenantId, token); var provider = providers.Resolve(mode);
        var storeName = await Scalar<string>(connection, "SELECT TOP(1) CompanyName FROM admin.Companies WHERE IsActive=1 ORDER BY CreatedOn;", token) ?? "WhatsBiz Store";
        var customers = new List<WhatsAppCommerceCustomer>();
        await using (var command = new SqlCommand("SELECT TOP(100) CustomerId,CustomerCode,CustomerName,Mobile FROM sales.Customers WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0 ORDER BY CustomerName;", connection))
        { command.Parameters.AddWithValue("@tenant", tenantId);
        await using (var reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) customers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        var warehouses = new List<WhatsAppCommerceWarehouse>();
        await using (var command = new SqlCommand("SELECT WarehouseId,WarehouseCode,WarehouseName FROM inventory.Warehouses WHERE IsActive=1 AND IsDeleted=0 ORDER BY IsDefault DESC,WarehouseName;", connection))
        await using (var reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) warehouses.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        var selectedWarehouse = warehouseId ?? warehouses.FirstOrDefault()?.WarehouseId;
        var products = selectedWarehouse.HasValue ? await Products(connection, selectedWarehouse.Value, tenantId, token) : [];
        var categories = products.GroupBy(x => new { x.CategoryId, x.CategoryName })
            .Select(x => new WhatsAppCommerceCategory(x.Key.CategoryId, x.Key.CategoryName, null, x.Count(),
                x.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ImageUrl))?.ProductId.ToString()))
            .OrderBy(x => x.CategoryName).ToArray();
        var collections = await Collections(connection, tenantId, products.Select(x => x.ProductId).ToHashSet(), token);
        return new(mode, storeName, customers, warehouses, categories, collections, products, await provider.SendWelcomeAsync(storeName, token));
    }

    public async Task<WhatsAppCommerceSendResult> SendCollectionAsync(Guid tenantId, Guid collectionId, Guid customerId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var config = await SendConfiguration(connection, tenantId, token);
        if (config is null) throw new BusinessRuleException("WhatsApp is not enabled or configured for this tenant.");
        var customer = await Customer(connection, tenantId, customerId, token);
        if (customer is null || string.IsNullOrWhiteSpace(customer.Mobile)) throw new BusinessRuleException("Select an active customer with a WhatsApp number.");
        var recipient = Digits().Replace(customer.Mobile!, string.Empty);
        if (recipient.Length < 8) throw new BusinessRuleException("The selected customer does not have a valid WhatsApp number.");
        if (config.Mode.Equals("META_TEST", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.TestRecipient) && recipient != config.TestRecipient) throw new BusinessRuleException("META_TEST can send only to the configured test recipient.");
        var products = await CollectionProducts(connection, tenantId, collectionId, token);
        if (products.Items.Count == 0) throw new BusinessRuleException("This collection has no active, in-stock products available to send.");
        var native = config.Mode.Equals("META_TEST", StringComparison.OrdinalIgnoreCase) && products.Items.Count <= 10 && products.Items.All(x => x.CatalogId is not null && x.ExternalProductId is not null) && products.Items.Select(x => x.CatalogId).Distinct(StringComparer.Ordinal).Count() == 1;
        var accessToken = config.Mode.Equals("MOCK", StringComparison.OrdinalIgnoreCase) ? string.Empty : protection.CreateProtector("WhatsBiz.WhatsApp.Secrets.v1").Unprotect(config.ProtectedToken ?? throw new BusinessRuleException("Stored WhatsApp credential cannot be decrypted."));
        return await providers.Resolve(config.Mode).SendProductCollectionAsync(new(config.ApiVersion ?? string.Empty, config.PhoneNumberId ?? string.Empty, accessToken, recipient, products.Title, products.Items.Select(x => new WhatsAppCommerceProductMessage(x.ProductId, x.ProductName, x.ProductCode, x.Price, x.ImageUrl, x.CatalogId, x.ExternalProductId)).ToArray(), native), token);
    }

    public async Task<WhatsAppCommerceCart> CalculateCartAsync(Guid tenantId, Guid warehouseId, IReadOnlyCollection<WhatsAppCommerceCartItem> items, CancellationToken token)
    {
        if (items.Count == 0) return new(warehouseId, [], 0, 0, 0);
        if (items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0)) throw new BusinessRuleException("Cart quantities must be greater than zero.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token); _ = await ProviderMode(connection, tenantId, token);
        var catalogue = (await Products(connection, warehouseId, tenantId, token)).ToDictionary(x => x.ProductId);
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
        if (string.IsNullOrWhiteSpace(input.DeliveryAddress) || input.DeliveryAddress.Trim().Length > 1000) throw new BusinessRuleException("A delivery or collection address is required.");
        if (input.FulfillmentMethod is not ("WALK_IN" or "RETAILER_DELIVERY" or "COURIER")) throw new BusinessRuleException("Select a valid order fulfilment method.");
        if (input.PaymentType is not ("ONLINE" or "COD")) throw new BusinessRuleException("Select a valid payment type.");
        await using (var customer = new SqlCommand("SELECT COUNT(1) FROM sales.Customers WHERE CustomerId=@id AND TenantId=@tenant AND IsActive=1 AND IsDeleted=0;", connection))
        { customer.Parameters.AddWithValue("@id", input.CustomerId); customer.Parameters.AddWithValue("@tenant", tenantId); if (Convert.ToInt32(await customer.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new BusinessRuleException("An active customer is required."); }
        var cart = await CalculateCartAsync(tenantId, input.WarehouseId, input.Items, token);
        if (cart.Items.Count == 0) throw new BusinessRuleException("The cart is empty.");
        var posItems = cart.Items.Select(x => new POSItemInput(x.ProductId, null, x.Quantity, x.UnitPrice, 0, 0, x.TaxPercentage)).ToArray();
        var onlinePayment = input.PaymentType == "ONLINE";
        var paymentReference = onlinePayment ? $"mock_rzp_{Guid.NewGuid():N}" : null;
        var payments = onlinePayment
            ? JsonSerializer.Serialize(new[] { new { MethodCode = "UPI", Amount = cart.GrandTotal, ReferenceNumber = paymentReference } })
            : "[]";
        var result = await pos.Post(new(null, null, input.CustomerId, input.WarehouseId, null,
            JsonSerializer.Serialize(posItems), payments, 0, 0, onlinePayment ? $"WhatsApp Commerce MOCK online payment: {paymentReference}" : "WhatsApp Commerce MOCK cash on delivery order",
            onlinePayment ? "COMPLETED" : "HELD", false, null, actor, tenantId, "WHATSAPP_DEMO"), token);
        await using (var update = new SqlCommand("UPDATE integration.WhatsAppCommerceOrders SET DeliveryAddress=@address,FulfillmentMethod=@fulfillment,PaymentType=@payment WHERE InvoiceId=@invoice AND TenantId=@tenant;", connection))
        { update.Parameters.AddWithValue("@address", input.DeliveryAddress.Trim()); update.Parameters.AddWithValue("@fulfillment", input.FulfillmentMethod); update.Parameters.AddWithValue("@payment", input.PaymentType); update.Parameters.AddWithValue("@invoice", result.InvoiceId); update.Parameters.AddWithValue("@tenant", tenantId); await update.ExecuteNonQueryAsync(token); }
        var messages = (await provider.SendOrderConfirmationAsync(result.InvoiceNumber, result.GrandTotal, token)).ToList();
        if (onlinePayment) messages.Add(new("WHATS_BIZ", "PAYMENT", $"Mock Razorpay UPI payment received âœ“\nReference: {paymentReference}\nAmount: â‚¹{result.GrandTotal:0.00}"));
        return new(result.InvoiceId, result.InvoiceNumber, result.Status, result.GrandTotal, messages);
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
            new("customerAvailable", "Customer available", await Exists("SELECT COUNT(1) FROM sales.Customers WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0;"), "/customers", "Create or activate a customer."),
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
        await using var command = new SqlCommand("SELECT TOP(50)i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.Status,w.SourceChannel,w.ProviderMode,w.DeliveryStatus,w.CourierName,w.TrackingNumber,w.DispatchedOn,w.DeliveredOn,c.CustomerName,c.Mobile,w.DeliveryAddress,w.FulfillmentMethod,w.PaymentType FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE w.TenantId=@tenant AND i.CustomerId=@customer ORDER BY i.InvoiceDate DESC;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@customer", customerId);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(Summary(reader));
        return rows;
    }

    public async Task<IReadOnlyCollection<WhatsAppCommerceOrderSummary>> GetDeliveryOrdersAsync(Guid tenantId, DateTimeOffset? fromDate, DateTimeOffset? toDate, string? deliveryStatus, string? trackingNumber, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        var rows = new List<WhatsAppCommerceOrderSummary>();
        await using var command = new SqlCommand("SELECT TOP(200)i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.Status,w.SourceChannel,w.ProviderMode,w.DeliveryStatus,w.CourierName,w.TrackingNumber,w.DispatchedOn,w.DeliveredOn,c.CustomerName,c.Mobile,w.DeliveryAddress,w.FulfillmentMethod,w.PaymentType FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE w.TenantId=@tenant AND (@from IS NULL OR i.InvoiceDate>=@from) AND (@to IS NULL OR i.InvoiceDate<DATEADD(day,1,@to)) AND (@deliveryStatus IS NULL OR w.DeliveryStatus=@deliveryStatus) AND (@trackingNumber IS NULL OR w.TrackingNumber LIKE '%' + @trackingNumber + '%') ORDER BY i.InvoiceDate DESC;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@from", (object?)fromDate ?? DBNull.Value); command.Parameters.AddWithValue("@to", (object?)toDate ?? DBNull.Value); command.Parameters.AddWithValue("@deliveryStatus", string.IsNullOrWhiteSpace(deliveryStatus) ? DBNull.Value : deliveryStatus.Trim().ToUpperInvariant()); command.Parameters.AddWithValue("@trackingNumber", string.IsNullOrWhiteSpace(trackingNumber) ? DBNull.Value : trackingNumber.Trim()); await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) rows.Add(Summary(reader));
        return rows;
    }

    public async Task<WhatsAppCommerceOrderDetails> GetOrderAsync(Guid tenantId, Guid customerId, Guid orderId, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        WhatsAppCommerceOrderSummary? order;
        await using (var command = new SqlCommand("SELECT i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.Status,w.SourceChannel,w.ProviderMode,w.DeliveryStatus,w.CourierName,w.TrackingNumber,w.DispatchedOn,w.DeliveredOn,c.CustomerName,c.Mobile,w.DeliveryAddress,w.FulfillmentMethod,w.PaymentType FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE w.TenantId=@tenant AND i.CustomerId=@customer AND i.InvoiceId=@invoice;", connection))
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

    public async Task<WhatsAppCommerceOrderSummary> UpdateDeliveryAsync(Guid tenantId, Guid orderId, UpdateWhatsAppCommerceDeliveryInput input, CancellationToken token)
    {
        if (input.DeliveryStatus is not ("PENDING" or "DISPATCHED" or "ON_THE_WAY" or "DELIVERED" or "CANCELLED")) throw new BusinessRuleException("Select a valid delivery status.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("UPDATE integration.WhatsAppCommerceOrders SET DeliveryStatus=@status,CourierName=@courier,TrackingNumber=@tracking,DispatchedOn=CASE WHEN @status IN('DISPATCHED','ON_THE_WAY','DELIVERED') AND DispatchedOn IS NULL THEN SYSUTCDATETIME() ELSE DispatchedOn END,DeliveredOn=CASE WHEN @status='DELIVERED' THEN SYSUTCDATETIME() ELSE DeliveredOn END WHERE TenantId=@tenant AND InvoiceId=@invoice;", connection);
        command.Parameters.AddWithValue("@status", input.DeliveryStatus); command.Parameters.AddWithValue("@courier", (object?)input.CourierName?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("@tracking", (object?)input.TrackingNumber?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@invoice", orderId);
        if (await command.ExecuteNonQueryAsync(token) != 1) throw new EntityNotFoundException("Commerce order was not found.");
        await using var select = new SqlCommand("SELECT i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.GrandTotal,i.Status,w.SourceChannel,w.ProviderMode,w.DeliveryStatus,w.CourierName,w.TrackingNumber,w.DispatchedOn,w.DeliveredOn,c.CustomerName,c.Mobile,w.DeliveryAddress,w.FulfillmentMethod,w.PaymentType FROM integration.WhatsAppCommerceOrders w JOIN sales.SalesInvoices i ON i.InvoiceId=w.InvoiceId LEFT JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE w.TenantId=@tenant AND i.InvoiceId=@invoice;", connection);
        select.Parameters.AddWithValue("@tenant", tenantId); select.Parameters.AddWithValue("@invoice", orderId); await using var reader = await select.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) throw new EntityNotFoundException("Commerce order was not found."); return Summary(reader);
    }

    private static WhatsAppCommerceOrderSummary Summary(SqlDataReader reader)
    { var status=reader.GetString(4);return new(reader.GetGuid(0),reader.GetString(1),reader.GetDateTimeOffset(2),reader.GetDecimal(3),status,DisplayStatus(status),reader.GetString(5),reader.GetString(6),reader.IsDBNull(7)?"PENDING":reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),reader.IsDBNull(9)?null:reader.GetString(9),reader.IsDBNull(10)?null:reader.GetDateTimeOffset(10),reader.IsDBNull(11)?null:reader.GetDateTimeOffset(11),reader.IsDBNull(12)?null:reader.GetString(12),reader.IsDBNull(13)?null:reader.GetString(13),reader.IsDBNull(14)?null:reader.GetString(14),reader.IsDBNull(15)?null:reader.GetString(15),reader.IsDBNull(16)?null:reader.GetString(16)); }
    public static string DisplayStatus(string status) => status.ToUpperInvariant() switch { "HELD" or "SUSPENDED" => "Order Confirmed", "COMPLETED" => "Completed", "CANCELLED" or "VOID" => "Cancelled", "RETURNED" => "Returned", "PARTIALLY_RETURNED" => "Partially Returned", _ => status };
    private static string NotificationStatus(string status) => status.ToUpperInvariant().Replace('_',' ');

    private static async Task<IReadOnlyCollection<WhatsAppCommerceProduct>> Products(SqlConnection connection, Guid warehouseId, Guid tenantId, CancellationToken token)
    {
        var rows = new List<WhatsAppCommerceProduct>();
        await using var command = new SqlCommand("""
            SELECT p.ProductId,p.ProductCode,p.Barcode,p.ProductName,p.ShortDescription,p.ImageUrl,p.SellingPrice,p.MRP,p.GSTPercentage,
                   ISNULL(SUM(b.QuantityAvailable),0) AvailableQuantity,p.CategoryId,c.CategoryName
            FROM master.Products p JOIN master.ProductCategories c ON c.ProductCategoryId=p.CategoryId
            LEFT JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId AND b.WarehouseId=@warehouse
            WHERE p.IsActive=1 AND p.IsDeleted=0
              AND c.IsActive=1 AND c.IsDeleted=0 AND p.TenantId=@tenant
            GROUP BY p.ProductId,p.ProductCode,p.Barcode,p.ProductName,p.ShortDescription,p.ImageUrl,p.SellingPrice,p.MRP,p.GSTPercentage,p.CategoryId,c.CategoryName
            HAVING ISNULL(SUM(b.QuantityAvailable),0)>0 ORDER BY p.ProductName;
            """, connection);
        command.Parameters.AddWithValue("@warehouse", warehouseId);
        command.Parameters.AddWithValue("@tenant", tenantId);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetGuid(10), reader.GetString(11), []));
        }
        foreach (var product in rows.ToArray())
        {
            await using var images = new SqlCommand("SELECT TOP(5) ProductImageId FROM master.ProductImages WHERE ProductId=@product AND IsActive=1 AND IsDeleted=0 ORDER BY IsPrimary DESC,CreatedOn;", connection);
            images.Parameters.AddWithValue("@product", product.ProductId);
            await using var imageReader = await images.ExecuteReaderAsync(token);
            var urls = new List<string>();
            while (await imageReader.ReadAsync(token)) urls.Add($"/api/products/{product.ProductId}/images/{imageReader.GetGuid(0)}");
            rows[rows.IndexOf(product)] = product with { ImageUrls = urls, ImageUrl = urls.FirstOrDefault() ?? product.ImageUrl };
        }
        return rows;
    }
    private sealed record SendConfig(string Mode, string? ApiVersion, string? PhoneNumberId, string? ProtectedToken, string? TestRecipient);
    private sealed record CustomerTarget(string Name, string? Mobile);
    private sealed record SendProduct(Guid ProductId, string ProductCode, string ProductName, decimal Price, string? ImageUrl, string? CatalogId, string? ExternalProductId);
    private sealed record SendProductSet(string Title, IReadOnlyCollection<SendProduct> Items);
    private static async Task<SendConfig?> SendConfiguration(SqlConnection connection, Guid tenantId, CancellationToken token)
    { await using var command = new SqlCommand("SELECT TOP(1) ProviderMode,ApiVersion,PhoneNumberId,AccessTokenProtected,TestRecipientNumber FROM integration.WhatsAppConfigurations WHERE TenantId=@tenant AND IsEnabled=1;", connection); command.Parameters.AddWithValue("@tenant", tenantId); await using var reader = await command.ExecuteReaderAsync(token); return await reader.ReadAsync(token) ? new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)) : null; }
    private static async Task<CustomerTarget?> Customer(SqlConnection connection, Guid tenantId, Guid customerId, CancellationToken token)
    { await using var command = new SqlCommand("SELECT TOP(1) CustomerName,Mobile FROM sales.Customers WHERE CustomerId=@id AND TenantId=@tenant AND IsActive=1 AND IsDeleted=0;", connection); command.Parameters.AddWithValue("@id", customerId); command.Parameters.AddWithValue("@tenant", tenantId); await using var reader = await command.ExecuteReaderAsync(token); return await reader.ReadAsync(token) ? new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)) : null; }
    private static async Task<SendProductSet> CollectionProducts(SqlConnection connection, Guid tenantId, Guid collectionId, CancellationToken token)
    { var rows = new List<SendProduct>(); string? title = null; await using var command = new SqlCommand(@"
        SELECT c.Name,p.ProductId,p.ProductCode,p.ProductName,p.SellingPrice,p.ImageUrl,
               CASE WHEN m.SyncStatus='MAPPED' THEN m.CatalogId END,
               CASE WHEN m.SyncStatus='MAPPED' THEN m.ExternalProductId END
        FROM commerce.Collections c JOIN commerce.CollectionProducts cp ON cp.CollectionId=c.CollectionId AND cp.TenantId=c.TenantId
        JOIN master.Products p ON p.ProductId=cp.ProductId AND p.TenantId=c.TenantId
        JOIN master.ProductCategories pc ON pc.ProductCategoryId=p.CategoryId AND pc.IsActive=1 AND pc.IsDeleted=0
        LEFT JOIN commerce.ProductChannelMappings m ON m.TenantId=c.TenantId AND m.ProductId=p.ProductId AND m.Provider='META'
        WHERE c.CollectionId=@collection AND c.TenantId=@tenant AND c.IsActive=1 AND c.IsDeleted=0
          AND (c.StartDate IS NULL OR c.StartDate<=SYSUTCDATETIME()) AND (c.EndDate IS NULL OR c.EndDate>=SYSUTCDATETIME())
          AND p.IsActive=1 AND p.IsDeleted=0 AND p.SellingPrice>=0
          AND EXISTS (SELECT 1 FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.WarehouseId=b.WarehouseId WHERE b.ProductId=p.ProductId AND b.QuantityAvailable>0 AND w.IsActive=1 AND w.IsDeleted=0)
        ORDER BY cp.DisplayOrder,p.ProductName;", connection); command.Parameters.AddWithValue("@collection", collectionId); command.Parameters.AddWithValue("@tenant", tenantId); await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) { title ??= reader.GetString(0); rows.Add(new(reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetDecimal(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7))); } if (title is null) throw new BusinessRuleException("The collection was not found or is not currently active."); return new(title, rows); }
    private static async Task<IReadOnlyCollection<WhatsAppCommerceCollection>> Collections(SqlConnection connection, Guid tenantId, ISet<Guid> eligibleProducts, CancellationToken token)
    { var map = new Dictionary<Guid,(string Name,string Slug,List<Guid> Products)>(); await using var command = new SqlCommand("SELECT c.CollectionId,c.Name,c.Slug,cp.ProductId FROM commerce.Collections c LEFT JOIN commerce.CollectionProducts cp ON cp.CollectionId=c.CollectionId AND cp.TenantId=c.TenantId LEFT JOIN master.Products p ON p.ProductId=cp.ProductId AND p.TenantId=c.TenantId AND p.IsActive=1 AND p.IsDeleted=0 WHERE c.TenantId=@tenant AND c.IsActive=1 AND c.IsDeleted=0 AND (c.StartDate IS NULL OR c.StartDate<=SYSUTCDATETIME()) AND (c.EndDate IS NULL OR c.EndDate>=SYSUTCDATETIME()) ORDER BY c.DisplayOrder,c.Name;", connection); command.Parameters.AddWithValue("@tenant", tenantId); await using var reader=await command.ExecuteReaderAsync(token); while(await reader.ReadAsync(token)){var id=reader.GetGuid(0);if(!map.TryGetValue(id,out var x)){x=(reader.GetString(1),reader.GetString(2),[]);map[id]=x;}if(!reader.IsDBNull(3)&&eligibleProducts.Contains(reader.GetGuid(3)))x.Products.Add(reader.GetGuid(3));}return map.Select(x=>new WhatsAppCommerceCollection(x.Key,x.Value.Name,x.Value.Slug,x.Value.Products)).ToArray(); }
    [GeneratedRegex("[^0-9]+", RegexOptions.CultureInvariant)] private static partial Regex Digits();
    private static async Task<string> ProviderMode(SqlConnection connection, Guid tenantId, CancellationToken token)
    { await using var command = new SqlCommand("SELECT ProviderMode FROM integration.WhatsAppConfigurations WHERE TenantId=@tenant AND IsEnabled=1;", connection); command.Parameters.AddWithValue("@tenant", tenantId); return await command.ExecuteScalarAsync(token) as string ?? throw new BusinessRuleException("WhatsApp Commerce is not enabled or configured for this tenant."); }
    private static async Task<T?> Scalar<T>(SqlConnection connection, string sql, CancellationToken token)
    { await using var command = new SqlCommand(sql, connection); var value = await command.ExecuteScalarAsync(token); return value is null or DBNull ? default : (T)value; }
}
