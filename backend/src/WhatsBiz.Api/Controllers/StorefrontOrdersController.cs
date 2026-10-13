using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using BusinessRuleException = WhatsBiz.Application.Common.Exceptions.BusinessRuleException;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

public sealed record RetailerStorefrontOrderDto(Guid Id, string OrderNumber, DateTimeOffset PlacedAt, string CustomerName,
    string? CustomerMobile, int ItemCount, decimal Total, string PaymentMethod, string PaymentStatus, string Status,
    string? DeliveryStatus, string? DeliveryAgent, Guid? DeliveryId, decimal DeliveryCharge, decimal PromotionDiscount, string? PromotionName,
    string? CancellationRequestStatus, string? CancellationReason, string RefundStatus, decimal RefundAmount, DateTimeOffset? RefundedAt, decimal RefundableAmount,
    Guid? RefundId, string? RefundAttemptStatus);

public sealed record StorefrontShippingLabelDto(
    Guid Id, string OrderNumber, DateTimeOffset OrderDate, string CustomerName, string? CustomerMobile,
    string DeliveryAddress, string? DeliveryPincode, string PaymentMethod, string PaymentStatus,
    bool PaymentDue, decimal DueAmount, string StoreName, string SenderAddress, string? SenderPhone, string? SenderEmail,
    string? SenderLogoUrl, string? RetailerDisplayName, string? DeliveryLocality, string? DeliveryCity, string? DeliveryDistrict,
    string? DeliveryState, string? QrCodeUrl, string? BarcodeUrl, IReadOnlyCollection<StorefrontShippingLabelItemDto> Items,
    bool ShowProductDetails);
public sealed record StorefrontShippingLabelItemDto(string ProductName, string? Sku, string? Variant, decimal Quantity);

[ApiController, Authorize, Route("api/storefront-orders")]
[HasPermission(Permissions.POS.View)]
public sealed class StorefrontOrdersController(IConfiguration configuration, ICurrentUserService current, IStorefrontCancellationService cancellations, IStorefrontReturnService returns, IPrintingService printing) : ControllerBase
{
    [HttpPost("{orderId:guid}/cancellation/approve"), HasPermission(Permissions.POS.Void)]
    public async Task<ActionResult<StorefrontCancellationDto>> ApproveCancellation(
        Guid orderId, StorefrontCancellationDecisionInput input, CancellationToken token)
    {
        try
        {
            return Ok(await cancellations.DecideAsync(
                current.TenantId ?? throw new UnauthorizedAccessException(),
                orderId, current.UserId ?? throw new UnauthorizedAccessException(), true, input.Note, token));
        }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPost("{orderId:guid}/cancellation/reject"), HasPermission(Permissions.POS.Void)]
    public async Task<ActionResult<StorefrontCancellationDto>> RejectCancellation(
        Guid orderId, StorefrontCancellationDecisionInput input, CancellationToken token)
    {
        try
        {
            return Ok(await cancellations.DecideAsync(
                current.TenantId ?? throw new UnauthorizedAccessException(),
                orderId, current.UserId ?? throw new UnauthorizedAccessException(), false, input.Note, token));
        }
        catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); }
    }
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<RetailerStorefrontOrderDto>>> List(
        [FromQuery] string? search, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? orderStatus, [FromQuery] string? paymentStatus, [FromQuery] string? deliveryStatus,
        CancellationToken token)
    {
        var tenant = current.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
        if (from is not null && to is not null && from >= to) return BadRequest(new { message = "The end date must be after the start date." });
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using (var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant", connection))
        {
            context.Parameters.AddWithValue("@tenant", tenant);
            await context.ExecuteNonQueryAsync(token);
        }
        await using var command = new SqlCommand("""
            SELECT TOP(200) i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,c.CustomerName,c.Mobile,
              (SELECT COUNT(1) FROM sales.SalesInvoiceItems x WHERE x.InvoiceId=i.InvoiceId),i.GrandTotal,
              COALESCE(cp.Provider,w.PaymentType,N'UNKNOWN'),COALESCE(cp.Status,N'PENDING'),i.Status,
              CASE WHEN d.DeliveryStatus=N'UNASSIGNED' AND d.ReadyAt IS NOT NULL THEN N'READY_FOR_PICKUP' ELSE d.DeliveryStatus END,a.DisplayName,d.OrderDeliveryId,i.DeliveryCharge,i.PromotionDiscountAmount,i.AppliedPromotionName,
              req.Status,req.Reason,COALESCE(refund.RefundStatus,reversal.RefundStatus),
              COALESCE(refund.RefundAmount,reversal.RefundRequiredAmount),refund.RefundedAt,app.Amount,
              refund.RefundId,attempt.Status
            FROM sales.SalesInvoices i
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            JOIN sales.Customers c ON c.TenantId=i.TenantId AND c.CustomerId=i.CustomerId
            LEFT JOIN commerce.OrderDeliveries d ON d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId
            LEFT JOIN commerce.DeliveryAgents a ON a.TenantId=i.TenantId AND a.DeliveryAgentId=d.DeliveryAgentId
            OUTER APPLY(SELECT TOP(1) p.Provider,p.Status FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId ORDER BY p.AttemptNumber DESC) cp
            OUTER APPLY(SELECT TOP(1) r.Status,r.Reason FROM commerce.StorefrontCancellationRequests r WHERE r.TenantId=i.TenantId AND r.InvoiceId=i.InvoiceId ORDER BY r.RequestedAt DESC) req
            OUTER APPLY(SELECT TOP(1) r.RefundId,r.RefundStatus,r.RefundAmount,r.RefundedAt FROM commerce.StorefrontRefunds r WHERE r.TenantId=i.TenantId AND r.InvoiceId=i.InvoiceId ORDER BY r.RequestedAt DESC) refund
            OUTER APPLY(SELECT TOP(1) v.RefundStatus,v.RefundRequiredAmount FROM sales.FullSaleReversals v WHERE v.TenantId=i.TenantId AND v.InvoiceId=i.InvoiceId) reversal
            OUTER APPLY(SELECT TOP(1) a.Status FROM commerce.StorefrontRefundAttempts a WHERE a.TenantId=i.TenantId AND a.RefundId=refund.RefundId ORDER BY a.AttemptNumber DESC) attempt
            OUTER APPLY(SELECT TOP(1) a.Amount FROM commerce.PaymentApplications a WHERE a.TenantId=i.TenantId AND a.InvoiceId=i.InvoiceId) app
            WHERE i.TenantId=@tenant
              AND (@search IS NULL OR i.InvoiceNumber LIKE N'%'+@search+N'%' OR c.CustomerName LIKE N'%'+@search+N'%' OR c.Mobile LIKE N'%'+@search+N'%')
              AND (@from IS NULL OR i.InvoiceDate>=@from) AND (@to IS NULL OR i.InvoiceDate<@to)
              AND (@payment IS NULL OR COALESCE(cp.Status,N'PENDING')=@payment)
              AND (@delivery IS NULL OR COALESCE(d.DeliveryStatus,N'UNASSIGNED')=@delivery)
              AND (@order IS NULL OR CASE
                WHEN i.Status IN(N'CANCELLED',N'VOID') OR d.DeliveryStatus=N'CANCELLED' THEN N'CANCELLED'
                WHEN d.DeliveryStatus=N'DELIVERED' THEN N'DELIVERED'
                WHEN d.DeliveryStatus=N'DELIVERY_FAILED' THEN N'DELIVERY_FAILED'
                WHEN d.DeliveryStatus IN(N'PICKED_UP',N'OUT_FOR_DELIVERY') THEN N'OUT_FOR_DELIVERY'
                WHEN d.DeliveryStatus=N'READY_FOR_PICKUP' OR (d.DeliveryStatus=N'UNASSIGNED' AND d.ReadyAt IS NOT NULL) THEN N'PACKED'
                ELSE N'ORDER_CONFIRMED' END=@order)
            ORDER BY i.InvoiceDate DESC;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenant);
        command.Parameters.AddWithValue("@search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim()[..Math.Min(search.Trim().Length, 100)]);
        command.Parameters.AddWithValue("@from", (object?)from ?? DBNull.Value);
        command.Parameters.AddWithValue("@to", (object?)to ?? DBNull.Value);
        command.Parameters.AddWithValue("@order", string.IsNullOrWhiteSpace(orderStatus) ? DBNull.Value : orderStatus.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@payment", string.IsNullOrWhiteSpace(paymentStatus) ? DBNull.Value : paymentStatus.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@delivery", string.IsNullOrWhiteSpace(deliveryStatus) ? DBNull.Value : deliveryStatus.Trim().ToUpperInvariant());
        var rows = new List<RetailerStorefrontOrderDto>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var invoiceStatus = reader.GetString(9); var delivery = reader.IsDBNull(10) ? null : reader.GetString(10);
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5), reader.GetDecimal(6),
                reader.GetString(7), reader.GetString(8), StorefrontOrderPresentation.Status(invoiceStatus, delivery),
                delivery, reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetGuid(12),reader.GetDecimal(13),reader.GetDecimal(14),reader.IsDBNull(15)?null:reader.GetString(15),
                reader.IsDBNull(16)?null:reader.GetString(16),reader.IsDBNull(17)?null:reader.GetString(17),
                reader.IsDBNull(18)?"NONE":reader.GetString(18),reader.IsDBNull(19)?0:reader.GetDecimal(19),
                reader.IsDBNull(20)?null:reader.GetDateTimeOffset(20),reader.IsDBNull(21)?0:Math.Min(reader.GetDecimal(6),reader.GetDecimal(21)),
                reader.IsDBNull(22)?null:reader.GetGuid(22),reader.IsDBNull(23)?null:reader.GetString(23)));
        }
        return Ok(rows);
    }

    [HttpGet("return-requests")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontReturnRequestDto>>> ReturnRequests(CancellationToken token)
        => Ok(await returns.RetailerRequestsAsync(current.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), token));

    [HttpPost("return-requests/{requestId:guid}/decision"), HasPermission(Permissions.POS.Return)]
    public async Task<ActionResult<StorefrontReturnRequestDto>> DecideReturn(Guid requestId, StorefrontReturnDecisionInput input, CancellationToken token)
    { try { return Ok(await returns.DecideAsync(current.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), requestId, current.UserId ?? throw new UnauthorizedAccessException(), input, token)); } catch (BusinessRuleException exception) { return BadRequest(new { message = exception.Message }); } }

    [HttpGet("{orderId:guid}/shipping-label")]
    public async Task<ActionResult<StorefrontShippingLabelDto>> ShippingLabel(Guid orderId, CancellationToken token)
    {
        var tenant = current.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using (var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant", connection))
        { context.Parameters.AddWithValue("@tenant", tenant); await context.ExecuteNonQueryAsync(token); }
        await using var command = new SqlCommand("""
            SELECT i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,cu.CustomerName,cu.Mobile,
              COALESCE(NULLIF(d.DeliveryAddressSnapshot,N''),NULLIF(w.DeliveryAddress,N''),N'Address not provided'),
              i.ServicePincode,COALESCE(cp.Provider,w.PaymentType,N'UNKNOWN'),COALESCE(cp.Status,N'PENDING'),
              CONVERT(bit,CASE WHEN COALESCE(d.CodRequired,CASE WHEN w.PaymentType=N'COD' THEN 1 ELSE 0 END)=1 AND i.BalanceAmount>0 THEN 1 ELSE 0 END),
              CASE WHEN COALESCE(d.CodRequired,CASE WHEN w.PaymentType=N'COD' THEN 1 ELSE 0 END)=1 THEN i.BalanceAmount ELSE 0 END,
              co.CompanyName,CONCAT_WS(N', ',co.AddressLine1,co.AddressLine2,co.City,co.State,co.PostalCode),co.Phone,co.Email,
              t.TenantKey,COALESCE(NULLIF(t.DisplayName,N''),t.Name),s.ShowProductDetailsOnShippingLabel
            FROM sales.SalesInvoices i
            JOIN integration.WhatsAppCommerceOrders w ON w.TenantId=i.TenantId AND w.InvoiceId=i.InvoiceId AND w.SourceChannel=N'STOREFRONT'
            JOIN sales.Customers cu ON cu.TenantId=i.TenantId AND cu.CustomerId=i.CustomerId
            LEFT JOIN commerce.OrderDeliveries d ON d.TenantId=i.TenantId AND d.OrderId=i.InvoiceId
            OUTER APPLY(SELECT TOP(1) p.Provider,p.Status FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId ORDER BY p.AttemptNumber DESC) cp
            JOIN admin.Companies co ON co.TenantId=i.TenantId AND co.IsActive=1
            JOIN core.Tenants t ON t.TenantId=i.TenantId AND t.IsActive=1
            LEFT JOIN commerce.StorefrontConfigurations s ON s.TenantId=i.TenantId
            WHERE i.TenantId=@tenant AND i.InvoiceId=@order
              AND i.Status NOT IN(N'CANCELLED',N'VOID')
              AND COALESCE(d.DeliveryStatus,N'')<>N'CANCELLED'
            ORDER BY co.CreatedOn;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenant); command.Parameters.AddWithValue("@order", orderId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return NotFound();
        var label = new StorefrontShippingLabelDto(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetBoolean(9), reader.GetDecimal(10), reader.GetString(11), reader.IsDBNull(12) ? "" : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14), null, reader.IsDBNull(16) ? null : reader.GetString(16), null, null, null, null, null, null, [], reader.IsDBNull(17) || reader.GetBoolean(17));
        var tenantKey = reader.IsDBNull(15) ? null : reader.GetString(15);
        var items = new List<StorefrontShippingLabelItemDto>();
        await reader.CloseAsync();
        await using (var itemCommand = new SqlCommand("""SELECT p.ProductName,p.ProductCode,CAST(NULL AS nvarchar(200)),x.Quantity FROM sales.SalesInvoiceItems x JOIN sales.SalesInvoices i ON i.InvoiceId=x.InvoiceId AND i.TenantId=@tenant JOIN master.Products p ON p.ProductId=x.ProductId AND p.TenantId=@tenant WHERE x.InvoiceId=@order ORDER BY x.InvoiceItemId;""", connection))
        {
            itemCommand.Parameters.AddWithValue("@tenant", tenant); itemCommand.Parameters.AddWithValue("@order", orderId);
            await using var itemsReader = await itemCommand.ExecuteReaderAsync(token);
            while (await itemsReader.ReadAsync(token)) items.Add(new(itemsReader.GetString(0), itemsReader.IsDBNull(1) ? null : itemsReader.GetString(1), null, itemsReader.GetDecimal(3)));
        }
        var showProducts = label.ShowProductDetails;
        var codeValue = label.OrderNumber;
        var qr = Convert.ToBase64String(printing.QrCode(new(codeValue)).Data);
        var barcode = Convert.ToBase64String(printing.Barcode(new(codeValue)).Data);
        return Ok(label with { SenderLogoUrl = tenantKey is null ? null : $"/api/store/{Uri.EscapeDataString(tenantKey)}/presentation/logo", QrCodeUrl = $"data:image/svg+xml;base64,{qr}", BarcodeUrl = $"data:image/svg+xml;base64,{barcode}", Items = items, ShowProductDetails = showProducts });
    }
}
