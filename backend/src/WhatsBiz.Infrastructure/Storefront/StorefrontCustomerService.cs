using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Exceptions;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontCustomerService(
    IConfiguration configuration,
    IDataProtectionProvider dataProtection,
    IStorefrontService storefront,
    IProductImageOptimizer optimizer,
    IStorefrontMediaStorage mediaStorage) : IStorefrontCustomerService
{
    private static readonly Regex StoreKeyPattern = new("^[A-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IDataProtector protector = dataProtection.CreateProtector("WhatsBiz.Storefront.CustomerSession.v1");
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<string> IssueSessionAsync(string storeKey, Guid tenantId, Guid customerId, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("SELECT COUNT(1) FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@customer", customerId);
        if ((int)(await command.ExecuteScalarAsync(token) ?? 0) != 1) throw new InvalidOperationException("Customer is unavailable.");
        var payload = new SessionPayload(storeKey.Trim().ToUpperInvariant(), tenantId, customerId, DateTimeOffset.UtcNow.AddDays(90));
        return protector.Protect(JsonSerializer.Serialize(payload));
    }

    public async Task<StorefrontCustomerDto?> GetSessionAsync(string storeKey, string sessionToken, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("SELECT CustomerId,CustomerName,Email,Mobile,ProfileMediaId FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0;", connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : $"/api/store/{Uri.EscapeDataString(storeKey)}/session/profile-image") : null;
    }

    public async Task<IReadOnlyCollection<StorefrontCustomerOrderDto>?> GetOrdersAsync(string storeKey, string sessionToken, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("""
            SELECT TOP(100) i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,i.Status,i.GrandTotal,
              CASE WHEN d.DeliveryStatus=N'UNASSIGNED' AND d.ReadyAt IS NOT NULL THEN N'READY_FOR_PICKUP' ELSE d.DeliveryStatus END,w.TrackingNumber,COALESCE(cp.PaymentMethod,w.PaymentType,N'UNKNOWN'),COALESCE(cp.Status,N'PENDING'),
              (SELECT COUNT(1) FROM sales.SalesInvoiceItems x WHERE x.InvoiceId=i.InvoiceId)
            FROM sales.SalesInvoices i
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            LEFT JOIN commerce.OrderDeliveries d ON d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId
            OUTER APPLY(SELECT TOP(1) p.PaymentMethod,p.Status FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId ORDER BY p.AttemptNumber DESC) cp
            WHERE i.TenantId=@tenant AND i.CustomerId=@customer
            ORDER BY i.InvoiceDate DESC;
            """, connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        var result = new List<StorefrontCustomerOrderDto>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var invoiceStatus = reader.GetString(3); var delivery = reader.IsDBNull(5) ? null : reader.GetString(5);
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2), StorefrontOrderPresentation.Status(invoiceStatus, delivery),
                reader.GetDecimal(4), delivery, reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetInt32(9)));
        }
        return result;
    }

    public async Task<StorefrontCustomerOrderDto?> GetOrderAsync(string storeKey, string sessionToken, Guid orderId, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null || orderId == Guid.Empty) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("""
            SELECT i.InvoiceNumber,i.InvoiceDate,i.Status,i.GrandTotal,CASE WHEN d.DeliveryStatus=N'UNASSIGNED' AND d.ReadyAt IS NOT NULL THEN N'READY_FOR_PICKUP' ELSE d.DeliveryStatus END,w.TrackingNumber,
              COALESCE(cp.PaymentMethod,w.PaymentType,N'UNKNOWN'),COALESCE(cp.Status,N'PENDING'),d.ReadyAt,d.OutForDeliveryAt,d.DeliveredAt,d.FailedAt,d.UpdatedAt,i.DeliveryCharge,i.PromotionDiscountAmount,i.AppliedPromotionName,i.Subtotal+i.TaxAmount,i.TaxAmount,i.Subtotal
            FROM sales.SalesInvoices i
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            LEFT JOIN commerce.OrderDeliveries d ON d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId
            OUTER APPLY(SELECT TOP(1) p.PaymentMethod,p.Status FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId ORDER BY p.AttemptNumber DESC) cp
            WHERE i.TenantId=@tenant AND i.CustomerId=@customer AND i.InvoiceId=@order;
            """, connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId); command.Parameters.AddWithValue("@customer", session.CustomerId); command.Parameters.AddWithValue("@order", orderId);
        string number, invoiceStatus, paymentMethod, paymentStatus; string? delivery, tracking; DateTimeOffset placed; decimal total;
        DateTimeOffset? packed, outAt, delivered, failed, cancelled; decimal deliveryCharge,promotionDiscount,merchandiseAmount,merchandiseTaxAmount,merchandiseSubtotal; string? promotionName;
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return null;
            number=reader.GetString(0); placed=reader.GetDateTimeOffset(1); invoiceStatus=reader.GetString(2); total=reader.GetDecimal(3);
            delivery=reader.IsDBNull(4)?null:reader.GetString(4); tracking=reader.IsDBNull(5)?null:reader.GetString(5); paymentMethod=reader.GetString(6); paymentStatus=reader.GetString(7);
            packed=reader.IsDBNull(8)?null:reader.GetDateTimeOffset(8); outAt=reader.IsDBNull(9)?null:reader.GetDateTimeOffset(9); delivered=reader.IsDBNull(10)?null:reader.GetDateTimeOffset(10); failed=reader.IsDBNull(11)?null:reader.GetDateTimeOffset(11);
            cancelled=StorefrontOrderPresentation.Status(invoiceStatus,delivery)=="Cancelled"&&!reader.IsDBNull(12)?reader.GetDateTimeOffset(12):null;
            deliveryCharge=reader.GetDecimal(13);promotionDiscount=reader.GetDecimal(14);promotionName=reader.IsDBNull(15)?null:reader.GetString(15);merchandiseAmount=reader.GetDecimal(16);merchandiseTaxAmount=reader.GetDecimal(17);merchandiseSubtotal=reader.GetDecimal(18);
        }
        var lines = new List<StorefrontCustomerOrderLineDto>();
        await using (var items = new SqlCommand("""SELECT x.ProductId,p.ProductName,p.PackSize,x.Quantity,x.LineTotal FROM sales.SalesInvoiceItems x JOIN master.Products p ON p.ProductId=x.ProductId AND p.TenantId=@tenant WHERE x.InvoiceId=@order ORDER BY p.ProductName;""", connection))
        {
            items.Parameters.AddWithValue("@tenant",session.TenantId);items.Parameters.AddWithValue("@order",orderId);
            await using var reader=await items.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))lines.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetDecimal(3),reader.GetDecimal(4)));
        }
        var status=StorefrontOrderPresentation.Status(invoiceStatus,delivery);
        return new(orderId,number,placed,status,total,delivery,tracking,paymentMethod,paymentStatus,lines.Count,lines,
            StorefrontOrderPresentation.Timeline(invoiceStatus,delivery,placed,packed,outAt,delivered,failed,cancelled),
            deliveryCharge,promotionDiscount,promotionName,merchandiseAmount,null,merchandiseTaxAmount,merchandiseSubtotal);
    }

    public async Task<IReadOnlyCollection<StorefrontProductDto>?> GetWishlistAsync(string storeKey, string sessionToken, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("SELECT ProductId FROM commerce.StorefrontWishlistItems WHERE TenantId=@tenant AND CustomerId=@customer ORDER BY CreatedAt DESC;", connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) ids.Add(reader.GetGuid(0));
        var products = new List<StorefrontProductDto>();
        foreach (var id in ids)
        {
            var product = await storefront.GetProductAsync(storeKey, id, token);
            if (product is not null) products.Add(product);
        }
        return products;
    }

    public Task<bool> AddWishlistAsync(string storeKey, string sessionToken, Guid productId, CancellationToken token)
        => ChangeWishlist(storeKey, sessionToken, productId, true, token);
    public Task<bool> RemoveWishlistAsync(string storeKey, string sessionToken, Guid productId, CancellationToken token)
        => ChangeWishlist(storeKey, sessionToken, productId, false, token);

    public async Task<bool> MergeWishlistAsync(string storeKey, string sessionToken, IReadOnlyCollection<Guid> productIds, CancellationToken token)
    {
        if (productIds.Count > 200 || await GetSessionAsync(storeKey, sessionToken, token) is null) return false;
        foreach (var productId in productIds.Distinct())
            await ChangeWishlist(storeKey, sessionToken, productId, true, token);
        return true;
    }

    private async Task<bool> ChangeWishlist(string storeKey, string sessionToken, Guid productId, bool add, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token);
        if (session is null || productId == Guid.Empty) return false;
        if (add && await storefront.GetProductAsync(storeKey, productId, token) is null) return false;
        await using var connection = await Open(session.TenantId, token);
        var sql = add
            ? "IF NOT EXISTS(SELECT 1 FROM commerce.StorefrontWishlistItems WHERE TenantId=@tenant AND CustomerId=@customer AND ProductId=@product) INSERT commerce.StorefrontWishlistItems(TenantId,CustomerId,ProductId) VALUES(@tenant,@customer,@product);"
            : "DELETE commerce.StorefrontWishlistItems WHERE TenantId=@tenant AND CustomerId=@customer AND ProductId=@product;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenant", session.TenantId);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        command.Parameters.AddWithValue("@product", productId);
        await command.ExecuteNonQueryAsync(token);
        return true;
    }

    public async Task<IReadOnlyCollection<StorefrontCustomerAddressDto>?> GetAddressesAsync(string storeKey, string sessionToken, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token); if (session is null) return null;
        await using var connection = await Open(session.TenantId, token);
        await using var command = new SqlCommand("SELECT AddressId,RecipientName,Mobile,AddressLine1,AddressLine2,Landmark,City,State,PostalCode,AddressType,IsDefault FROM sales.CustomerAddresses WHERE CustomerId=@customer ORDER BY IsDefault DESC,AddressId;", connection);
        command.Parameters.AddWithValue("@customer", session.CustomerId);
        var result = new List<StorefrontCustomerAddressDto>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetBoolean(10)));
        return result;
    }

    public async Task<StorefrontCustomerAddressDto?> SaveAddressAsync(string storeKey, string sessionToken, Guid? addressId, StorefrontCustomerAddressInput input, CancellationToken token)
    {
        var session = await Resolve(storeKey, sessionToken, token); if (session is null) return null;
        var recipient = input.RecipientName?.Trim(); var mobile = input.Mobile?.Trim(); var line1 = input.AddressLine1?.Trim(); var city = input.City?.Trim(); var state = input.State?.Trim(); var pincode = input.Pincode?.Trim(); var type = input.AddressType?.Trim();
        if (string.IsNullOrWhiteSpace(recipient) || recipient.Length > 250 || string.IsNullOrWhiteSpace(mobile) || mobile.Length > 18 || string.IsNullOrWhiteSpace(line1) || line1.Length > 250 || string.IsNullOrWhiteSpace(city) || city.Length > 100 || string.IsNullOrWhiteSpace(state) || state.Length > 100 || !Regex.IsMatch(pincode ?? "", "^[0-9]{6}$") || type is not ("Home" or "Work" or "Other")) throw new BusinessRuleException("Enter complete valid delivery address details.");
        await using var connection = await Open(session.TenantId, token); await using var transaction = await connection.BeginTransactionAsync(token);
        if (input.IsDefault) { await using var clear = new SqlCommand("UPDATE sales.CustomerAddresses SET IsDefault=0 WHERE CustomerId=@customer;",connection,(SqlTransaction)transaction); clear.Parameters.AddWithValue("@customer",session.CustomerId); await clear.ExecuteNonQueryAsync(token); }
        var id = addressId ?? Guid.NewGuid();
        await using var command = new SqlCommand(addressId.HasValue ? "UPDATE sales.CustomerAddresses SET RecipientName=@name,Mobile=@mobile,AddressLine1=@line1,AddressLine2=@line2,Landmark=@landmark,City=@city,State=@state,PostalCode=@postal,AddressType=@type,IsDefault=@default WHERE AddressId=@id AND CustomerId=@customer;" : "INSERT sales.CustomerAddresses(AddressId,CustomerId,RecipientName,Mobile,AddressLine1,AddressLine2,Landmark,City,State,PostalCode,AddressType,IsDefault) VALUES(@id,@customer,@name,@mobile,@line1,@line2,@landmark,@city,@state,@postal,@type,@default);",connection,(SqlTransaction)transaction);
        command.Parameters.AddWithValue("@id",id); command.Parameters.AddWithValue("@customer",session.CustomerId); command.Parameters.AddWithValue("@name",recipient); command.Parameters.AddWithValue("@mobile",mobile); command.Parameters.AddWithValue("@line1",line1); command.Parameters.AddWithValue("@line2",(object?)input.AddressLine2?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("@landmark",(object?)input.Landmark?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("@city",city); command.Parameters.AddWithValue("@state",state); command.Parameters.AddWithValue("@postal",pincode); command.Parameters.AddWithValue("@type",type); command.Parameters.AddWithValue("@default",input.IsDefault); if (await command.ExecuteNonQueryAsync(token)!=1){await transaction.RollbackAsync(token);return null;} await transaction.CommitAsync(token);
        return (await GetAddressesAsync(storeKey,sessionToken,token))?.SingleOrDefault(x=>x.AddressId==id);
    }

    public async Task<bool> DeleteAddressAsync(string storeKey, string sessionToken, Guid addressId, CancellationToken token) => await ChangeAddress(storeKey,sessionToken,addressId,"DELETE FROM sales.CustomerAddresses WHERE AddressId=@id AND CustomerId=@customer;",token);
    public async Task<bool> SetDefaultAddressAsync(string storeKey, string sessionToken, Guid addressId, CancellationToken token)
    {
        var session = await Resolve(storeKey,sessionToken,token); if(session is null)return false; await using var connection=await Open(session.TenantId,token); await using var transaction=await connection.BeginTransactionAsync(token);
        await using(var clear=new SqlCommand("UPDATE sales.CustomerAddresses SET IsDefault=0 WHERE CustomerId=@customer;",connection,(SqlTransaction)transaction)){clear.Parameters.AddWithValue("@customer",session.CustomerId);await clear.ExecuteNonQueryAsync(token);}
        await using var set=new SqlCommand("UPDATE sales.CustomerAddresses SET IsDefault=1 WHERE AddressId=@id AND CustomerId=@customer;",connection,(SqlTransaction)transaction);set.Parameters.AddWithValue("@id",addressId);set.Parameters.AddWithValue("@customer",session.CustomerId);var changed=await set.ExecuteNonQueryAsync(token);if(changed!=1){await transaction.RollbackAsync(token);return false;}await transaction.CommitAsync(token);return true;
    }
    private async Task<bool> ChangeAddress(string storeKey,string sessionToken,Guid addressId,string sql,CancellationToken token){var session=await Resolve(storeKey,sessionToken,token);if(session is null)return false;await using var connection=await Open(session.TenantId,token);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@id",addressId);command.Parameters.AddWithValue("@customer",session.CustomerId);return await command.ExecuteNonQueryAsync(token)==1;}

    public async Task<StorefrontCustomerDto?> UploadProfileImageAsync(string storeKey,string sessionToken,string fileName,Stream content,CancellationToken token)
    {
        var session=await Resolve(storeKey,sessionToken,token);if(session is null)return null;
        await using var buffer=new MemoryStream();await content.CopyToAsync(buffer,token);
        var optimized=await optimizer.OptimizeAsync(fileName,null,buffer.ToArray(),token);
        var stored=await mediaStorage.StoreStorefrontAsync(new(session.TenantId,"customer-profile",session.CustomerId,optimized.CatalogData,optimized.ThumbnailData,optimized.ContentType),token);
        var database=stored.Provider.Equals(ProductImageStorageProviders.Database,StringComparison.OrdinalIgnoreCase);
        await using var connection=await Open(session.TenantId,token);
        Guid? old=null;StorefrontMediaStorageDeleteRequest? oldStorage=null;
        await using(var select=new SqlCommand("SELECT c.ProfileMediaId,m.StorageProvider,m.ObjectKey,m.ThumbnailObjectKey FROM sales.Customers c LEFT JOIN commerce.StorefrontMedia m ON m.TenantId=c.TenantId AND m.MediaId=c.ProfileMediaId WHERE c.TenantId=@tenant AND c.CustomerId=@customer;",connection))
        {select.Parameters.AddWithValue("@tenant",session.TenantId);select.Parameters.AddWithValue("@customer",session.CustomerId);await using var reader=await select.ExecuteReaderAsync(token);if(await reader.ReadAsync(token)){old=reader.IsDBNull(0)?null:reader.GetGuid(0);if(!reader.IsDBNull(1))oldStorage=new(session.TenantId,reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3));}}
        await using var transaction=await connection.BeginTransactionAsync(token);
        try
        {
            var id=old??Guid.NewGuid();
            await using var command=new SqlCommand(old.HasValue?"UPDATE commerce.StorefrontMedia SET FileName=@file,ContentType=@content,ThumbnailContentType=N'image/webp',StorageProvider=@provider,ObjectKey=@object,ThumbnailObjectKey=@thumb,ImageData=@data,ThumbnailData=@thumbdata,ContentHash=@hash,CreatedAt=SYSUTCDATETIME() WHERE MediaId=@id AND TenantId=@tenant AND CustomerId=@customer AND ResourceType=N'customer-profile';":"INSERT commerce.StorefrontMedia(MediaId,TenantId,CustomerId,ResourceType,FileName,ContentType,ThumbnailContentType,StorageProvider,ObjectKey,ThumbnailObjectKey,ImageData,ThumbnailData,ContentHash,CreatedAt) VALUES(@id,@tenant,@customer,N'customer-profile',@file,@content,N'image/webp',@provider,@object,@thumb,@data,@thumbdata,@hash,SYSUTCDATETIME());",connection,(SqlTransaction)transaction);
            command.Parameters.AddWithValue("@id",id);command.Parameters.AddWithValue("@tenant",session.TenantId);command.Parameters.AddWithValue("@customer",session.CustomerId);command.Parameters.AddWithValue("@file",optimized.FileName);command.Parameters.AddWithValue("@content",optimized.ContentType);command.Parameters.AddWithValue("@provider",stored.Provider);command.Parameters.AddWithValue("@object",(object?)stored.ObjectKey??DBNull.Value);command.Parameters.AddWithValue("@thumb",(object?)stored.ThumbnailObjectKey??DBNull.Value);command.Parameters.AddWithValue("@data",(object?)(database?optimized.CatalogData:null)??DBNull.Value);command.Parameters.AddWithValue("@thumbdata",(object?)(database?optimized.ThumbnailData:null)??DBNull.Value);command.Parameters.AddWithValue("@hash",stored.ContentHash);
            if(await command.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("The existing customer profile media record could not be updated.");
            await using var profile=new SqlCommand("UPDATE sales.Customers SET ProfileMediaId=@id WHERE TenantId=@tenant AND CustomerId=@customer;",connection,(SqlTransaction)transaction);profile.Parameters.AddWithValue("@id",id);profile.Parameters.AddWithValue("@tenant",session.TenantId);profile.Parameters.AddWithValue("@customer",session.CustomerId);if(await profile.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("The customer profile media reference could not be updated.");
            await transaction.CommitAsync(token);
        }
        catch
        {await transaction.RollbackAsync(CancellationToken.None);await mediaStorage.DeleteStorefrontAsync(new(session.TenantId,stored.Provider,stored.ObjectKey,stored.ThumbnailObjectKey),CancellationToken.None);throw;}
        if(oldStorage is not null&&!oldStorage.Provider.Equals(ProductImageStorageProviders.Database,StringComparison.OrdinalIgnoreCase)&&(!string.Equals(oldStorage.ObjectKey,stored.ObjectKey,StringComparison.Ordinal)||!string.Equals(oldStorage.ThumbnailObjectKey,stored.ThumbnailObjectKey,StringComparison.Ordinal)))await mediaStorage.DeleteStorefrontAsync(oldStorage,CancellationToken.None);
        return await GetSessionAsync(storeKey,sessionToken,token);
    }
    public async Task<StorefrontCustomerDto?> RemoveProfileImageAsync(string storeKey,string sessionToken,CancellationToken token){var session=await Resolve(storeKey,sessionToken,token);if(session is null)return null;await using var connection=await Open(session.TenantId,token);Guid? old=null;await using(var q=new SqlCommand("SELECT ProfileMediaId FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer;",connection)){q.Parameters.AddWithValue("@tenant",session.TenantId);q.Parameters.AddWithValue("@customer",session.CustomerId);old=(Guid?)(await q.ExecuteScalarAsync(token) is Guid previous?previous:null);}await using(var q=new SqlCommand("UPDATE sales.Customers SET ProfileMediaId=NULL WHERE TenantId=@tenant AND CustomerId=@customer;",connection)){q.Parameters.AddWithValue("@tenant",session.TenantId);q.Parameters.AddWithValue("@customer",session.CustomerId);await q.ExecuteNonQueryAsync(token);}if(old.HasValue)await DeleteProfileMedia(session.TenantId,old.Value,token);return await GetSessionAsync(storeKey,sessionToken,token);}
    public async Task<StorefrontImage?> GetProfileImageAsync(string storeKey,string sessionToken,CancellationToken token){var session=await Resolve(storeKey,sessionToken,token);if(session is null)return null;await using var connection=await Open(session.TenantId,token);await using var q=new SqlCommand("SELECT m.StorageProvider,m.ObjectKey,m.ImageData,m.ContentType FROM sales.Customers c JOIN commerce.StorefrontMedia m ON m.TenantId=c.TenantId AND m.MediaId=c.ProfileMediaId WHERE c.TenantId=@tenant AND c.CustomerId=@customer;",connection);q.Parameters.AddWithValue("@tenant",session.TenantId);q.Parameters.AddWithValue("@customer",session.CustomerId);await using var reader=await q.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;var content=await mediaStorage.ReadStorefrontAsync(new(session.TenantId,reader.GetString(0),reader.IsDBNull(1)?null:reader.GetString(1),reader.IsDBNull(2)?[]:(byte[])reader.GetValue(2),reader.GetString(3)),token);return content is null?null:new(content.ContentType,content.Content);}
    private async Task DeleteProfileMedia(Guid tenant,Guid mediaId,CancellationToken token){await using var connection=await Open(tenant,token);await using var q=new SqlCommand("SELECT StorageProvider,ObjectKey,ThumbnailObjectKey FROM commerce.StorefrontMedia WHERE TenantId=@tenant AND MediaId=@id;",connection);q.Parameters.AddWithValue("@tenant",tenant);q.Parameters.AddWithValue("@id",mediaId);await using var r=await q.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))return;var provider=r.GetString(0);var key=r.IsDBNull(1)?null:r.GetString(1);var thumb=r.IsDBNull(2)?null:r.GetString(2);await r.CloseAsync();await using var d=new SqlCommand("DELETE commerce.StorefrontMedia WHERE TenantId=@tenant AND MediaId=@id;",connection);d.Parameters.AddWithValue("@tenant",tenant);d.Parameters.AddWithValue("@id",mediaId);await d.ExecuteNonQueryAsync(token);await mediaStorage.DeleteStorefrontAsync(new(tenant,provider,key,thumb),token);}

    private async Task<SessionPayload?> Resolve(string storeKey, string tokenValue, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(tokenValue) || tokenValue.Length > 4096) return null;
        SessionPayload? payload;
        try { payload = JsonSerializer.Deserialize<SessionPayload>(protector.Unprotect(tokenValue)); }
        catch { return null; }
        if (payload is null || payload.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        var normalized = storeKey?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !StoreKeyPattern.IsMatch(normalized) || !string.Equals(payload.StoreKey, normalized, StringComparison.Ordinal)) return null;
        await using var connection = await Open(payload.TenantId, token);
        await using var command = new SqlCommand("""
            SELECT COUNT(1) FROM core.Tenants t
            JOIN sales.Customers c ON c.TenantId=t.TenantId AND c.CustomerId=@customer AND c.IsActive=1 AND c.IsDeleted=0
            WHERE t.TenantId=@tenant AND t.TenantKey=@storeKey AND t.IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@tenant", payload.TenantId);
        command.Parameters.AddWithValue("@customer", payload.CustomerId);
        command.Parameters.AddWithValue("@storeKey", normalized);
        return (int)(await command.ExecuteScalarAsync(token) ?? 0) == 1 ? payload : null;
    }

    private async Task<SqlConnection> Open(Guid tenantId, CancellationToken token)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", connection);
        context.Parameters.AddWithValue("@tenant", tenantId);
        await context.ExecuteNonQueryAsync(token);
        return connection;
    }

    private sealed record SessionPayload(string StoreKey, Guid TenantId, Guid CustomerId, DateTimeOffset ExpiresAt);
}
