using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.OrderIntake;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.OrderIntake;

public sealed partial class OrderIntakeService(IConfiguration configuration, IStorefrontCheckoutService checkout) : IOrderIntakeService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<OrderIntakePage> ListAsync(Guid tenantId, string? search, string? channel, string? status,
        DateTimeOffset? from, DateTimeOffset? through, int page, int pageSize, bool revealMobile, CancellationToken token)
    {
        page = Math.Clamp(page, 1, 100_000);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var normalizedChannel = Filter(channel, OrderIntakeChannels.V1);
        var normalizedStatus = Filter(status, OrderIntakeStatuses.All);
        if (from.HasValue && through.HasValue && from >= through) throw new ArgumentException("The end date must be after the start date.");
        var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(search.Trim().Length, 120)];
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM commerce.OrderIntakeRequests r
            WHERE r.TenantId=@tenant AND (@channel IS NULL OR r.SourceChannel=@channel)
              AND (@status IS NULL OR r.IntakeStatus=@status)
              AND (@from IS NULL OR r.CreatedAtUtc>=@from) AND (@to IS NULL OR r.CreatedAtUtc<@to)
              AND (@search IS NULL OR r.RequestReference LIKE N'%'+@search+N'%' OR (@reveal=1 AND r.CustomerName LIKE N'%'+@search+N'%')
                   OR (@reveal=1 AND r.CustomerMobile LIKE N'%'+@search+N'%') OR (@reveal=1 AND r.OriginalRequest LIKE N'%'+@search+N'%'));
            SELECT r.OrderIntakeRequestId,r.RequestReference,r.SourceChannel,r.IntakeStatus,CASE WHEN @reveal=1 THEN r.CustomerName ELSE NULL END,
                   CASE WHEN @reveal=1 THEN r.CustomerMobile ELSE NULL END,r.CreatedAtUtc,r.LastCustomerInteractionAtUtc,
                   r.AssignedStaffId,u.UserName,COUNT(i.OrderIntakeItemId),
                   SUM(CASE WHEN i.OrderIntakeItemId IS NOT NULL AND i.MatchStatus<>'MATCHED' THEN 1 ELSE 0 END)
            FROM commerce.OrderIntakeRequests r
            LEFT JOIN commerce.OrderIntakeItems i ON i.TenantId=r.TenantId AND i.OrderIntakeRequestId=r.OrderIntakeRequestId
            LEFT JOIN core.Users u ON u.Id=r.AssignedStaffId AND u.TenantId=r.TenantId
            WHERE r.TenantId=@tenant AND (@channel IS NULL OR r.SourceChannel=@channel)
              AND (@status IS NULL OR r.IntakeStatus=@status)
              AND (@from IS NULL OR r.CreatedAtUtc>=@from) AND (@to IS NULL OR r.CreatedAtUtc<@to)
              AND (@search IS NULL OR r.RequestReference LIKE N'%'+@search+N'%' OR (@reveal=1 AND r.CustomerName LIKE N'%'+@search+N'%')
                   OR (@reveal=1 AND r.CustomerMobile LIKE N'%'+@search+N'%') OR (@reveal=1 AND r.OriginalRequest LIKE N'%'+@search+N'%'))
            GROUP BY r.OrderIntakeRequestId,r.RequestReference,r.SourceChannel,r.IntakeStatus,r.CustomerName,r.CustomerMobile,
                     r.CreatedAtUtc,r.LastCustomerInteractionAtUtc,r.AssignedStaffId,u.UserName
            ORDER BY r.CreatedAtUtc DESC OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;
            """, connection);
        AddTenant(command, tenantId);
        command.Parameters.AddWithValue("@channel", (object?)normalizedChannel ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (object?)normalizedStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@from", (object?)from?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@to", (object?)through?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@search", (object?)query ?? DBNull.Value);
        command.Parameters.AddWithValue("@reveal", revealMobile);
        command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@size", pageSize);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(token);
        var rows = new List<OrderIntakeSummary>();
        while (await reader.ReadAsync(token))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                NullableString(reader, 4), NullableString(reader, 5), Utc(reader.GetDateTime(6)), NullableUtc(reader, 7),
                NullableGuid(reader, 8), NullableString(reader, 9), reader.GetInt32(10), reader.GetInt32(11)));
        return new(rows, total, page, pageSize);
    }

    public async Task<OrderIntakeDetail> GetAsync(Guid tenantId, Guid requestId, bool revealMobile, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT r.RequestReference,r.SourceChannel,r.IntakeStatus,CASE WHEN @reveal=1 THEN r.CustomerName ELSE NULL END,
                   CASE WHEN @reveal=1 THEN r.CustomerMobile ELSE NULL END,CASE WHEN @reveal=1 THEN r.OriginalRequest ELSE N'[Customer request text requires Customer View permission.]' END,CASE WHEN @reveal=1 THEN r.Notes ELSE NULL END,r.FulfilmentPreference,
                   r.RequestedAtUtc,r.ProviderMessageId,r.ConversationReference,r.LastCustomerInteractionAtUtc,
                   r.AssignedStaffId,u.UserName,r.ConfirmationChannel,r.CustomerConfirmedAtUtc,r.CreatedAtUtc,r.Version,r.DeliveryAddressDraftJson
            FROM commerce.OrderIntakeRequests r LEFT JOIN core.Users u ON u.Id=r.AssignedStaffId AND u.TenantId=r.TenantId
            WHERE r.TenantId=@tenant AND r.OrderIntakeRequestId=@id;
            SELECT i.OrderIntakeItemId,CASE WHEN @reveal=1 THEN i.RequestedText ELSE N'[Restricted]' END,i.RequestedQuantity,i.RequestedUnit,i.MatchedProductId,i.MatchStatus,
                   p.ProductCode,p.ProductName,p.SellingPrice,
                   COALESCE(stock.AvailableQuantity,0),i.ItemNotes,i.CustomerApprovedSubstitution,i.ProposedSubstitution
            FROM commerce.OrderIntakeItems i
            LEFT JOIN master.Products p ON p.TenantId=i.TenantId AND p.ProductId=i.MatchedProductId
            OUTER APPLY(SELECT SUM(b.QuantityOnHand-b.QuantityReserved) AvailableQuantity
                FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.TenantId=b.TenantId AND w.WarehouseId=b.WarehouseId AND w.IsActive=1 AND w.IsDeleted=0
                WHERE b.TenantId=i.TenantId AND b.ProductId=i.MatchedProductId) stock
            WHERE i.TenantId=@tenant AND i.OrderIntakeRequestId=@id ORDER BY i.CreatedAtUtc,i.OrderIntakeItemId;
            SELECT EventType,PreviousStatus,NewStatus,Actor,Note,CreatedAtUtc
            FROM commerce.OrderIntakeEvents WHERE TenantId=@tenant AND OrderIntakeRequestId=@id ORDER BY CreatedAtUtc,OrderIntakeEventId;
            """, connection);
        AddTenant(command, tenantId); command.Parameters.AddWithValue("@id", requestId); command.Parameters.AddWithValue("@reveal", revealMobile);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Order intake request was not found.");
        var reference = reader.GetString(0); var channel = reader.GetString(1); var status = reader.GetString(2);
        var name = NullableString(reader, 3); var mobile = NullableString(reader, 4); var original = reader.GetString(5);
        var notes = NullableString(reader, 6); var fulfilment = NullableString(reader, 7); var requestedAt = NullableUtc(reader, 8);
        var providerMessage = NullableString(reader, 9); var conversation = NullableString(reader, 10); var interaction = NullableUtc(reader, 11);
        var assigned = NullableGuid(reader, 12); var assignedName = NullableString(reader, 13); var confirmation = NullableString(reader, 14);
        var confirmedAt = NullableUtc(reader, 15); var createdAt = Utc(reader.GetDateTime(16)); var version = (byte[])reader.GetValue(17);
        var addressJson = NullableString(reader, 18);
        var deliveryAddress = revealMobile && addressJson is not null
            ? System.Text.Json.JsonSerializer.Deserialize<OrderIntakeDeliveryAddressSnapshot>(addressJson) : null;
        await reader.NextResultAsync(token);
        var items = new List<OrderIntakeItem>();
        while (await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0), reader.GetString(1), NullableDecimal(reader, 2),
            NullableString(reader, 3), NullableGuid(reader, 4), reader.GetString(5), NullableString(reader, 6), NullableString(reader, 7),
            NullableDecimal(reader, 8), NullableDecimal(reader, 9), NullableString(reader, 10), reader.GetBoolean(11), reader.GetBoolean(12)));
        await reader.NextResultAsync(token);
        var history = new List<OrderIntakeAudit>();
        while (await reader.ReadAsync(token)) history.Add(new(reader.GetString(0), NullableString(reader, 1), NullableString(reader, 2),
            NullableString(reader, 3), NullableString(reader, 4), Utc(reader.GetDateTime(5))));
        return new(requestId, reference, channel, status, name, mobile, original, notes, fulfilment, requestedAt,
            providerMessage, conversation, interaction, assigned, assignedName, confirmation, confirmedAt, createdAt, version, items, history, deliveryAddress);
    }

    public Task<OrderIntakeDetail> CreateAsync(Guid tenantId, OrderIntakeCreateInput input, string? actor, CancellationToken token)
    {
        if (input.SourceChannel is not (OrderIntakeChannels.PhoneCall or OrderIntakeChannels.ManualList))
            throw new ArgumentException("Staff-created requests must use Phone Call or Manual List.");
        return CreateCoreAsync(tenantId, input, actor, null, null, token);
    }

    public async Task<IReadOnlyCollection<OrderIntakeSavedAddress>> GetLinkedCustomerAddressesAsync(Guid tenantId, Guid requestId, CancellationToken token)
    {
        // Only addresses for a customer already linked to this request by a trusted identity flow are eligible.
        // A mobile-number match is never used to discover or link customer addresses.
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT a.AddressId,a.RecipientName,a.Mobile,a.AddressLine1,a.AddressLine2,a.Landmark,a.City,a.State,a.PostalCode,a.AddressType,a.IsDefault
            FROM commerce.OrderIntakeRequests r
            JOIN sales.Customers c ON c.TenantId=r.TenantId AND c.CustomerId=r.CustomerId
            JOIN sales.CustomerAddresses a ON a.CustomerId=c.CustomerId
            WHERE r.TenantId=@tenant AND r.OrderIntakeRequestId=@request AND r.CustomerId IS NOT NULL
            ORDER BY a.IsDefault DESC,a.AddressId;
            """, connection);
        AddTenant(command, tenantId); command.Parameters.AddWithValue("@request", requestId);
        await using var reader = await command.ExecuteReaderAsync(token); var addresses = new List<OrderIntakeSavedAddress>();
        while (await reader.ReadAsync(token)) addresses.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            NullableString(reader, 4), NullableString(reader, 5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetBoolean(10)));
        return addresses;
    }

    public async Task<IReadOnlyCollection<OrderIntakeCustomerOption>> SearchCustomersAsync(Guid tenantId, string query, CancellationToken token)
    {
        var search = query.Trim(); if (search.Length is < 2 or > 100) throw new ArgumentException("Enter at least two characters to find an existing customer.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT TOP(20) CustomerId,CustomerCode,CustomerName FROM sales.Customers WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0 AND (CustomerName LIKE @q OR CustomerCode LIKE @q) ORDER BY CustomerName,CustomerCode;", connection);
        AddTenant(command, tenantId); command.Parameters.AddWithValue("@q", "%" + search.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%");
        await using var reader = await command.ExecuteReaderAsync(token); var customers = new List<OrderIntakeCustomerOption>();
        while (await reader.ReadAsync(token)) customers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return customers;
    }

    public async Task<OrderIntakeDetail> LinkVerifiedCustomerAsync(Guid tenantId, Guid requestId,
        OrderIntakeCustomerLinkInput input, string? actor, bool revealCustomerData, CancellationToken token)
    {
        if (input.CustomerId == Guid.Empty || input.Version is not { Length: 8 }) throw new ArgumentException("Select a customer and refresh the request before linking.");
        if (!input.StaffAcknowledged) throw new ArgumentException("Confirm that the customer's identity was verified before linking their saved addresses.");
        var method = input.VerificationMethod.Trim().ToUpperInvariant();
        if (method is not ("CALLBACK" or "IN_PERSON" or "VERIFIED_CUSTOMER")) throw new ArgumentException("Choose a supported customer identity verification method.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            string name; string? mobile; string status; byte[] version;
            await using (var customer = new SqlCommand("SELECT CustomerName,Mobile FROM sales.Customers WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0;", connection, transaction))
            {
                AddTenant(customer, tenantId); customer.Parameters.AddWithValue("@customer", input.CustomerId);
                await using var reader = await customer.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token)) throw new ArgumentException("The selected customer is not active in this retailer account.");
                name = reader.GetString(0); mobile = NullableString(reader, 1);
            }
            await using (var request = new SqlCommand("SELECT IntakeStatus,Version FROM commerce.OrderIntakeRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderIntakeRequestId=@request;", connection, transaction))
            {
                AddTenant(request, tenantId); request.Parameters.AddWithValue("@request", requestId);
                await using var reader = await request.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Order intake request was not found.");
                status = reader.GetString(0); version = (byte[])reader.GetValue(1);
            }
            if (!version.AsSpan().SequenceEqual(input.Version)) throw new DBConcurrencyException("The request changed. Refresh and retry.");
            if (status is "CONVERTED" or "REJECTED" or "CANCELLED") throw new InvalidOperationException("Customer identity cannot be changed for a closed request.");
            await using (var update = new SqlCommand("""
                UPDATE commerce.OrderIntakeQuotes SET QuoteStatus='SUPERSEDED' WHERE TenantId=@tenant AND OrderIntakeRequestId=@request AND QuoteStatus='CURRENT';
                UPDATE commerce.OrderIntakeRequests SET CustomerId=@customer,CustomerName=@name,CustomerMobile=@mobile,DeliveryAddressDraftJson=NULL,
                    IntakeStatus=CASE WHEN IntakeStatus IN('AWAITING_CUSTOMER','CONFIRMED') THEN 'UNDER_REVIEW' ELSE IntakeStatus END,
                    ConfirmationChannel=NULL,CustomerConfirmedAtUtc=NULL,ConfirmedQuoteRevision=NULL,ConfirmationVerificationMethod=NULL,
                    ConfirmationEvidenceReference=NULL,ConfirmationOperator=NULL,UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND OrderIntakeRequestId=@request AND Version=@version;
                """, connection, transaction))
            {
                AddTenant(update, tenantId); update.Parameters.AddWithValue("@request", requestId); update.Parameters.AddWithValue("@version", input.Version);
                update.Parameters.AddWithValue("@customer", input.CustomerId); update.Parameters.AddWithValue("@name", name);
                update.Parameters.AddWithValue("@mobile", (object?)mobile ?? DBNull.Value); update.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
                if (await update.ExecuteNonQueryAsync(token) < 1) throw new DBConcurrencyException("The request changed while the customer was being linked.");
            }
            await InsertEvent(connection, transaction, tenantId, requestId, "CUSTOMER_LINKED", status,
                status is "AWAITING_CUSTOMER" or "CONFIRMED" ? "UNDER_REVIEW" : status, actor,
                $"Linked tenant customer after staff identity verification by {method}; prior address, quote and confirmation were invalidated.", token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, revealCustomerData, token);
    }

    public async Task<OrderIntakeDetail> SaveDeliveryDetailsAsync(Guid tenantId, Guid requestId,
        OrderIntakeDeliveryDetailsInput input, string? actor, bool revealCustomerData, CancellationToken token)
    {
        if (input.Version is not { Length: 8 }) throw new ArgumentException("Refresh the request before saving fulfillment details.");
        var address = await NormalizeDeliveryAddressAsync(tenantId, requestId, input.Details, token);
        var fulfillment = NormalizeFulfillment(input.Details.FulfilmentPreference);
        if (fulfillment == "DELIVERY" && address is null) throw new ArgumentException("A complete delivery address is required for home delivery.");
        if (address is not null)
        {
            var availability = await checkout.CheckDeliveryForTenantAsync(tenantId, address.Pincode, token);
            if (!availability.DeliveryEnabled || !availability.IsPincodeServiceable)
                throw new ArgumentException("This retailer does not currently deliver to that PIN code. Verify the address or choose Store Pickup.");
        }
        var json = address is null ? null : System.Text.Json.JsonSerializer.Serialize(address);
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            string status;
            await using (var lockRequest = new SqlCommand("SELECT IntakeStatus,Version FROM commerce.OrderIntakeRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderIntakeRequestId=@id;", connection, transaction))
            {
                AddTenant(lockRequest, tenantId); lockRequest.Parameters.AddWithValue("@id", requestId);
                await using var reader = await lockRequest.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Order intake request was not found.");
                status = reader.GetString(0); var version = (byte[])reader.GetValue(1);
                if (!version.AsSpan().SequenceEqual(input.Version)) throw new DBConcurrencyException("The request changed. Refresh and retry.");
            }
            if (status is "CONVERTED" or "REJECTED" or "CANCELLED") throw new InvalidOperationException("Fulfillment details cannot be changed for a closed request.");
            await using (var update = new SqlCommand("""
                UPDATE commerce.OrderIntakeQuotes SET QuoteStatus='SUPERSEDED'
                WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND QuoteStatus='CURRENT';
                UPDATE commerce.OrderIntakeRequests SET FulfilmentPreference=@fulfillment,DeliveryAddressDraftJson=@address,
                    IntakeStatus=CASE WHEN IntakeStatus IN('AWAITING_CUSTOMER','CONFIRMED') THEN 'UNDER_REVIEW' ELSE IntakeStatus END,
                    ConfirmationChannel=NULL,CustomerConfirmedAtUtc=NULL,ConfirmedQuoteRevision=NULL,
                    ConfirmationVerificationMethod=NULL,ConfirmationEvidenceReference=NULL,ConfirmationOperator=NULL,
                    UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND Version=@version;
                """, connection, transaction))
            {
                AddTenant(update, tenantId); update.Parameters.AddWithValue("@id", requestId); update.Parameters.AddWithValue("@version", input.Version);
                update.Parameters.AddWithValue("@fulfillment", fulfillment); update.Parameters.AddWithValue("@address", (object?)json ?? DBNull.Value);
                update.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
                if (await update.ExecuteNonQueryAsync(token) < 1) throw new DBConcurrencyException("The request changed while fulfillment details were being saved.");
            }
            await InsertEvent(connection, transaction, tenantId, requestId, "DELIVERY_DETAILS_CHANGED", status,
                status is "AWAITING_CUSTOMER" or "CONFIRMED" ? "UNDER_REVIEW" : status, actor,
                fulfillment == "DELIVERY" ? "Delivery address or fulfillment changed; prior quote and customer acceptance were invalidated." : "Fulfillment changed to store pickup; prior quote and customer acceptance were invalidated.", token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, revealCustomerData, token);
    }

    private async Task<OrderIntakeDeliveryAddressSnapshot?> NormalizeDeliveryAddressAsync(Guid tenantId, Guid requestId,
        OrderIntakeDeliveryAddressInput input, CancellationToken token)
    {
        var fulfillment = NormalizeFulfillment(input.FulfilmentPreference);
        if (fulfillment == "PICKUP") return null;
        if (input.SavedAddressId is Guid addressId)
        {
            await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
            await using var command = new SqlCommand("""
                SELECT a.RecipientName,a.Mobile,a.AddressLine1,a.AddressLine2,a.Landmark,a.City,a.State,a.PostalCode,a.District
                FROM commerce.OrderIntakeRequests r
                JOIN sales.Customers c ON c.TenantId=r.TenantId AND c.CustomerId=r.CustomerId
                JOIN sales.CustomerAddresses a ON a.CustomerId=c.CustomerId
                WHERE r.TenantId=@tenant AND r.OrderIntakeRequestId=@request AND a.AddressId=@address;
                """, connection);
            AddTenant(command, tenantId); command.Parameters.AddWithValue("@request", requestId); command.Parameters.AddWithValue("@address", addressId);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new ArgumentException("That saved address is not linked to the verified customer on this request.");
            input = input with { RecipientName=reader.GetString(0), Mobile=reader.GetString(1), HouseStreet=reader.GetString(2),
                Locality=NullableString(reader,3) ?? NullableString(reader,8) ?? "", City=reader.GetString(5), State=reader.GetString(6), Pincode=reader.GetString(7),
                Landmark=NullableString(reader,4), SavedAddressId=addressId };
        }
        return NormalizeAddressFields(input);
    }

    private static OrderIntakeDeliveryAddressSnapshot NormalizeAddressFields(OrderIntakeDeliveryAddressInput input)
        => OrderIntakeDeliveryAddressPolicy.Normalize(input);

    private static string NormalizeFulfillment(string? value) => OrderIntakeDeliveryAddressPolicy.NormalizeFulfillment(value);

    public async Task<bool> CreateWhatsAppAsync(Guid tenantId, string phoneNumberId, string sender, string messageId,
        string? profileName, string text, CancellationToken token)
    {
        if (!LooksLikeOrderRequest(text) || string.IsNullOrWhiteSpace(phoneNumberId)
            || string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(messageId)) return false;
        try
        {
            await CreateCoreAsync(tenantId, new(OrderIntakeChannels.WhatsApp, profileName, sender, text,
                FulfilmentPreference: null), null, messageId, sender, token);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Provider retries resolve to the existing tenant-scoped message id.
        }
        return true;
    }

    private async Task<OrderIntakeDetail> CreateCoreAsync(Guid tenantId, OrderIntakeCreateInput input, string? actor,
        string? providerMessageId, string? customerMobile, CancellationToken token)
    {
        var channel = (input.SourceChannel ?? string.Empty).Trim().ToUpperInvariant();
        if (channel is not (OrderIntakeChannels.PhoneCall or OrderIntakeChannels.ManualList or OrderIntakeChannels.WhatsApp))
            throw new ArgumentException("Choose Phone Call or Manual List for a staff-created request.");
        var original = input.OriginalRequest?.Trim() ?? string.Empty;
        if (original.Length is < 2 or > 4000) throw new ArgumentException("Enter a request between 2 and 4,000 characters.");
        var name = Limit(input.CustomerName ?? input.DeliveryAddress?.RecipientName, 200);
        var mobile = NormalizeMobile(customerMobile ?? input.CustomerMobile ?? input.DeliveryAddress?.Mobile);
        if (channel == OrderIntakeChannels.PhoneCall && mobile is null)
            throw new ArgumentException("Enter a customer mobile number for a phone-call request.");
        var fulfilment = string.IsNullOrWhiteSpace(input.FulfilmentPreference) ? null : NormalizeFulfillment(input.FulfilmentPreference);
        if (fulfilment == "DELIVERY" && input.DeliveryAddress is null)
            throw new ArgumentException("Enter the complete delivery address for a home-delivery request.");
        if (input.DeliveryAddress?.SavedAddressId is not null)
            throw new ArgumentException("Select a saved address after the request is linked to the verified customer.");
        var deliveryAddress = fulfilment == "DELIVERY" && input.DeliveryAddress is not null
            ? NormalizeAddressFields(input.DeliveryAddress) : null;
        var deliveryAddressJson = deliveryAddress is null ? null : System.Text.Json.JsonSerializer.Serialize(deliveryAddress);
        var requestId = Guid.NewGuid();
        var reference = "OI-" + requestId.ToString("N")[..10].ToUpperInvariant();
        var items = ParseItems(original).ToArray();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await using (var insert = new SqlCommand("""
                INSERT commerce.OrderIntakeRequests(TenantId,OrderIntakeRequestId,RequestReference,SourceChannel,IntakeStatus,
                    CustomerName,CustomerMobile,OriginalRequest,Notes,FulfilmentPreference,DeliveryAddressDraftJson,RequestedAtUtc,ProviderMessageId,
                    ConversationReference,LastCustomerInteractionAtUtc,CreatedBy,UpdatedBy)
                VALUES(@tenant,@id,@reference,@channel,'NEW',@name,@mobile,@request,@notes,@fulfilment,@address,@requested,@message,
                    NULL,CASE WHEN @message IS NULL THEN NULL ELSE SYSUTCDATETIME() END,@actor,@actor);
                """, connection, transaction))
            {
                AddTenant(insert, tenantId); insert.Parameters.AddWithValue("@id", requestId); insert.Parameters.AddWithValue("@reference", reference);
                insert.Parameters.AddWithValue("@channel", channel); insert.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
                insert.Parameters.AddWithValue("@mobile", (object?)mobile ?? DBNull.Value); insert.Parameters.AddWithValue("@request", original);
                insert.Parameters.AddWithValue("@notes", (object?)Limit(input.Notes, 2000) ?? DBNull.Value);
                insert.Parameters.AddWithValue("@fulfilment", (object?)fulfilment ?? DBNull.Value);
                insert.Parameters.AddWithValue("@address", (object?)deliveryAddressJson ?? DBNull.Value);
                insert.Parameters.AddWithValue("@requested", (object?)input.RequestedAtUtc?.UtcDateTime ?? DBNull.Value);
                insert.Parameters.AddWithValue("@message", (object?)providerMessageId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(token);
            }
            var matchesByItem = await FindMatchesForItemsCoreAsync(connection, transaction, tenantId, items, token);
            for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                var parsed = items[itemIndex];
                var matches = matchesByItem.GetValueOrDefault(itemIndex, []);
                var exact = matches.Where(x => EqualsSearch(x.ProductName, parsed.ProductQuery) || EqualsSearch(x.ProductCode, parsed.ProductQuery)).ToArray();
                var selected = exact.Length == 1 ? exact[0] : null;
                var matchStatus = selected is not null ? "MATCHED" : matches.Count > 0 ? "REVIEW" : "UNMATCHED";
                await using var addItem = new SqlCommand("""
                    INSERT commerce.OrderIntakeItems(TenantId,OrderIntakeRequestId,OrderIntakeItemId,RequestedText,RequestedQuantity,RequestedUnit,MatchedProductId,MatchStatus)
                    VALUES(@tenant,@request,@id,@text,@qty,@unit,@product,@match);
                    """, connection, transaction);
                AddTenant(addItem, tenantId); addItem.Parameters.AddWithValue("@request", requestId); addItem.Parameters.AddWithValue("@id", Guid.NewGuid());
                addItem.Parameters.AddWithValue("@text", parsed.Text); addItem.Parameters.AddWithValue("@qty", (object?)parsed.Quantity ?? DBNull.Value);
                addItem.Parameters.AddWithValue("@unit", (object?)parsed.Unit ?? DBNull.Value); addItem.Parameters.AddWithValue("@product", (object?)selected?.ProductId ?? DBNull.Value);
                addItem.Parameters.AddWithValue("@match", matchStatus); await addItem.ExecuteNonQueryAsync(token);
            }
            await InsertEvent(connection, transaction, tenantId, requestId, "CREATED", null, "NEW", actor, null, token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, false, token);
    }

    public async Task<OrderIntakeDetail> AssignToSelfAsync(Guid tenantId, Guid requestId, Guid staffId, byte[]? version,
        string? actor, CancellationToken token)
    {
        if (version is not { Length: 8 }) throw new ArgumentException("Refresh the request before assigning it.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        try
        {
            await using var command = new SqlCommand("UPDATE commerce.OrderIntakeRequests SET AssignedStaffId=@staff,UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND Version=@version AND IntakeStatus NOT IN('CONVERTED','REJECTED','CANCELLED');", connection, transaction);
            AddTenant(command, tenantId); command.Parameters.AddWithValue("@id", requestId); command.Parameters.AddWithValue("@staff", staffId);
            command.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value); command.Parameters.AddWithValue("@version", version);
            if (await command.ExecuteNonQueryAsync(token) != 1) throw new DBConcurrencyException("The request changed. Refresh and try again.");
            await InsertEvent(connection, transaction, tenantId, requestId, "ASSIGNED", null, null, actor, "Assigned to current staff member.", token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, true, token);
    }

    public async Task<IReadOnlyCollection<OrderIntakeProductMatch>> FindMatchesAsync(Guid tenantId, string query, CancellationToken token)
    {
        var term = query.Trim(); if (term.Length is < 2 or > 120) throw new ArgumentException("Enter at least 2 characters to search this store's products.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        return await FindMatchesCoreAsync(connection, null, tenantId, term, 20, token);
    }

    public async Task<OrderIntakeDetail> UpdateItemAsync(Guid tenantId, Guid requestId, Guid itemId,
        OrderIntakeItemUpdateInput input, string? actor, bool revealMobile, CancellationToken token)
    {
        if (input.Version is not { Length: 8 }) throw new ArgumentException("Refresh the request before editing items.");
        if (input.Quantity is <= 0) throw new ArgumentException("Quantity must be greater than zero.");
        var requestedMatchStatus = string.IsNullOrWhiteSpace(input.MatchStatus) ? null : input.MatchStatus.Trim().ToUpperInvariant();
        if (requestedMatchStatus is not (null or "MATCHED" or "REVIEW" or "UNMATCHED" or "UNAVAILABLE"))
            throw new ArgumentException("Choose a supported item review status.");
        if (input.CustomerApprovedSubstitution)
            throw new InvalidOperationException("Only the quote-revision confirmation action can record customer approval of a proposed substitution.");
        if (input.ProposedSubstitution && !input.MatchedProductId.HasValue)
            throw new ArgumentException("Select the proposed replacement product before saving the substitution proposal.");
        if (input.ProposedSubstitution && input.RetailerConfirmedMatch)
            throw new ArgumentException("A product cannot be marked as both an exact match and a proposed substitution.");
        if (input.RetailerConfirmedMatch && !input.MatchedProductId.HasValue)
            throw new ArgumentException("Select a product before confirming the retailer match.");
        if (requestedMatchStatus == "UNAVAILABLE" && input.MatchedProductId.HasValue)
            throw new ArgumentException("An unavailable request item cannot have a matched product.");
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            if (input.MatchedProductId is Guid productId && !await ProductBelongsToTenant(connection, transaction, tenantId, productId, token))
                throw new ArgumentException("The selected product is not active in this retailer's catalog.");
            await using (var update = new SqlCommand("""
                UPDATE i SET MatchedProductId=@product,MatchStatus=CASE WHEN @forcedStatus='UNAVAILABLE' THEN 'UNAVAILABLE'
                        WHEN @product IS NULL THEN 'UNMATCHED'
                        WHEN @proposed=1 THEN 'REVIEW'
                        WHEN @retailerConfirmed=1 OR CHARINDEX(UPPER(LTRIM(RTRIM(p.ProductName))),UPPER(i.RequestedText))>0 THEN 'MATCHED'
                        ELSE 'REVIEW' END,
                    RequestedQuantity=@quantity,RequestedUnit=@unit,ItemNotes=@notes,CustomerApprovedSubstitution=0,ProposedSubstitution=@proposed,UpdatedAtUtc=SYSUTCDATETIME()
                FROM commerce.OrderIntakeItems i JOIN commerce.OrderIntakeRequests r ON r.TenantId=i.TenantId AND r.OrderIntakeRequestId=i.OrderIntakeRequestId
                LEFT JOIN master.Products p ON p.TenantId=i.TenantId AND p.ProductId=@product AND p.IsActive=1 AND p.IsDeleted=0
                WHERE i.TenantId=@tenant AND i.OrderIntakeRequestId=@request AND i.OrderIntakeItemId=@item
                  AND r.Version=@version AND r.IntakeStatus IN('NEW','UNDER_REVIEW');
                """, connection, transaction))
            {
                AddTenant(update, tenantId); update.Parameters.AddWithValue("@request", requestId); update.Parameters.AddWithValue("@item", itemId);
                update.Parameters.AddWithValue("@version", input.Version); update.Parameters.AddWithValue("@product", (object?)input.MatchedProductId ?? DBNull.Value);
                update.Parameters.AddWithValue("@quantity", (object?)input.Quantity ?? DBNull.Value); update.Parameters.AddWithValue("@unit", (object?)Limit(input.Unit, 60) ?? DBNull.Value);
                update.Parameters.AddWithValue("@notes", (object?)Limit(input.Notes, 500) ?? DBNull.Value);
                update.Parameters.AddWithValue("@retailerConfirmed", input.RetailerConfirmedMatch);
                update.Parameters.AddWithValue("@proposed", input.ProposedSubstitution);
                update.Parameters.AddWithValue("@forcedStatus", (object?)requestedMatchStatus ?? DBNull.Value);
                if (await update.ExecuteNonQueryAsync(token) != 1) throw new DBConcurrencyException("The request changed or is no longer editable. Refresh and try again.");
            }
            await using (var bump = new SqlCommand("UPDATE commerce.OrderIntakeRequests SET IntakeStatus=CASE WHEN IntakeStatus='AWAITING_CUSTOMER' THEN 'UNDER_REVIEW' ELSE IntakeStatus END,ConfirmationChannel=NULL,CustomerConfirmedAtUtc=NULL,UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderIntakeRequestId=@request AND Version=@version;", connection, transaction))
            {
                AddTenant(bump, tenantId); bump.Parameters.AddWithValue("@request", requestId); bump.Parameters.AddWithValue("@version", input.Version);
                bump.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
                if (await bump.ExecuteNonQueryAsync(token) != 1) throw new DBConcurrencyException("The request changed. Refresh and try again.");
            }
            await InsertEvent(connection, transaction, tenantId, requestId, "ITEM_UPDATED", null, null, actor, "Matched catalog item reviewed.", token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, revealMobile, token);
    }

    public async Task<OrderIntakeDetail> UpdateStatusAsync(Guid tenantId, Guid requestId, OrderIntakeStatusInput input,
        string? actor, bool revealMobile, CancellationToken token)
    {
        var target = input.Status.Trim().ToUpperInvariant();
        if (!OrderIntakeStatuses.All.Contains(target)) throw new ArgumentException("The requested intake status is invalid.");
        if (target == OrderIntakeStatuses.Converted) throw new InvalidOperationException("Use the confirmed-quotation conversion action to create a non-posting Sales Order.");
        if (target == OrderIntakeStatuses.AwaitingCustomer) throw new InvalidOperationException("Generate an authoritative quotation to move the request to Awaiting Customer.");
        if (target == OrderIntakeStatuses.Confirmed) throw new InvalidOperationException("Use the quote-revision confirmation action to record explicit customer acceptance.");
        if (target == OrderIntakeStatuses.Confirmed && string.IsNullOrWhiteSpace(input.ConfirmationChannel))
            throw new ArgumentException("Record how the customer confirmed the reviewed request.");
        var confirmation = string.IsNullOrWhiteSpace(input.ConfirmationChannel) ? null : input.ConfirmationChannel.Trim().ToUpperInvariant();
        if (confirmation is not null && confirmation is not ("WHATSAPP" or "PHONE_CALL" or "IN_PERSON" or "CUSTOMER_APP"))
            throw new ArgumentException("Choose a supported customer-confirmation channel.");
        if (target == OrderIntakeStatuses.Rejected && string.IsNullOrWhiteSpace(input.Note))
            throw new ArgumentException("Enter a reason before rejecting the request.");
        if (input.Version is not { Length: 8 }) throw new ArgumentException("Refresh the request before changing its status.");

        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            string previous;
            await using (var read = new SqlCommand("SELECT IntakeStatus FROM commerce.OrderIntakeRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND Version=@version;", connection, transaction))
            {
                AddTenant(read, tenantId); read.Parameters.AddWithValue("@id", requestId); read.Parameters.AddWithValue("@version", input.Version);
                previous = (string?)await read.ExecuteScalarAsync(token) ?? throw new DBConcurrencyException("The request changed or was not found. Refresh and try again.");
            }
            if (!CanTransition(previous, target)) throw new ArgumentException($"The request cannot move from {previous} to {target}.");
            if (target == OrderIntakeStatuses.AwaitingCustomer || target == OrderIntakeStatuses.Confirmed)
            {
                await using var pending = new SqlCommand("SELECT COUNT(1) FROM commerce.OrderIntakeItems WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND (MatchStatus<>'MATCHED' OR MatchedProductId IS NULL OR RequestedQuantity IS NULL);", connection, transaction);
                AddTenant(pending, tenantId); pending.Parameters.AddWithValue("@id", requestId);
                if ((int)(await pending.ExecuteScalarAsync(token) ?? 0) > 0) throw new ArgumentException("Resolve every requested item and quantity before asking for customer confirmation.");
            }
            if (target is "UNDER_REVIEW" or "REJECTED" or "CANCELLED")
            {
                await using var invalidate = new SqlCommand("UPDATE commerce.OrderIntakeQuotes SET QuoteStatus='SUPERSEDED' WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND QuoteStatus='CURRENT';", connection, transaction);
                AddTenant(invalidate, tenantId); invalidate.Parameters.AddWithValue("@id", requestId); await invalidate.ExecuteNonQueryAsync(token);
                await using var clearSubstitutions = new SqlCommand("UPDATE commerce.OrderIntakeItems SET CustomerApprovedSubstitution=0,MatchStatus=CASE WHEN ProposedSubstitution=1 THEN 'REVIEW' ELSE MatchStatus END WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND ProposedSubstitution=1;", connection, transaction);
                AddTenant(clearSubstitutions, tenantId); clearSubstitutions.Parameters.AddWithValue("@id", requestId); await clearSubstitutions.ExecuteNonQueryAsync(token);
            }
            await using (var update = new SqlCommand("""
                UPDATE commerce.OrderIntakeRequests SET IntakeStatus=@status,ConfirmationChannel=CASE WHEN @status='CONFIRMED' THEN @channel ELSE NULL END,
                    CustomerConfirmedAtUtc=CASE WHEN @status='CONFIRMED' THEN SYSUTCDATETIME() ELSE NULL END,
                    LastCustomerInteractionAtUtc=CASE WHEN @status IN('AWAITING_CUSTOMER','CONFIRMED') THEN SYSUTCDATETIME() ELSE LastCustomerInteractionAtUtc END,
                    UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND Version=@version;
                """, connection, transaction))
            {
                AddTenant(update, tenantId); update.Parameters.AddWithValue("@id", requestId); update.Parameters.AddWithValue("@status", target);
                update.Parameters.AddWithValue("@channel", (object?)confirmation ?? DBNull.Value); update.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
                update.Parameters.AddWithValue("@version", input.Version);
                if (await update.ExecuteNonQueryAsync(token) != 1) throw new DBConcurrencyException("The request changed. Refresh and try again.");
            }
            await InsertEvent(connection, transaction, tenantId, requestId, "STATUS_CHANGED", previous, target, actor, Limit(input.Note, 1000), token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, revealMobile, token);
    }

    public async Task<OrderIntakeQuoteDto> CreateQuoteAsync(Guid tenantId, Guid requestId, OrderIntakeQuoteInput input,
        string? actor, bool revealCustomerContext, CancellationToken token)
    {
        if (input.Version is not { Length: 8 }) throw new ArgumentException("Refresh the request before preparing a quotation.");
        if (input.ValidityMinutes is < 5 or > 1440) throw new ArgumentException("Quote validity must be between 5 minutes and 24 hours.");
        var detail = await GetAsync(tenantId, requestId, true, token);
        if (detail.Status != OrderIntakeStatuses.UnderReview) throw new InvalidOperationException("Move the request to Under Review before preparing a quotation.");
        if (detail.Items.Count is 0 or > 100 || detail.Items.Any(x => (x.MatchStatus != "MATCHED" && !(x.ProposedSubstitution && x.MatchStatus == "REVIEW")) || !x.MatchedProductId.HasValue || !x.RequestedQuantity.HasValue))
            throw new InvalidOperationException("Match every requested product, explicitly mark any proposed substitution, and enter a quantity before preparing a quotation.");
        var fulfillment = NormalizeFulfillment(detail.FulfilmentPreference);
        var deliveryAddress = detail.DeliveryAddress;
        if (fulfillment == "DELIVERY" && deliveryAddress is null)
            throw new InvalidOperationException("Save a complete customer delivery address before preparing a home-delivery quotation.");
        var priced = await checkout.QuoteIntakeAsync(tenantId,
            detail.Items.Select(x => new StorefrontCheckoutItem(x.MatchedProductId!.Value, x.RequestedQuantity!.Value)).ToArray(),
            fulfillment, deliveryAddress?.Pincode, input.PromotionCode, token);
        var quotedItems = MapQuoteItems(detail, priced.Items);

        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            string status; int revision;
            await using (var lockRequest = new SqlCommand("SELECT IntakeStatus,Version,COALESCE((SELECT MAX(QuoteRevision) FROM commerce.OrderIntakeQuotes WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderIntakeRequestId=@id),0)+1 FROM commerce.OrderIntakeRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderIntakeRequestId=@id;", connection, transaction))
            {
                AddTenant(lockRequest, tenantId); lockRequest.Parameters.AddWithValue("@id", requestId);
                await using var reader = await lockRequest.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Order intake request was not found.");
                status = reader.GetString(0); var currentVersion = (byte[])reader.GetValue(1); revision = reader.GetInt32(2);
                if (!currentVersion.AsSpan().SequenceEqual(input.Version)) throw new DBConcurrencyException("The request changed while its quotation was being prepared. Refresh and retry.");
            }
            if (status != OrderIntakeStatuses.UnderReview) throw new DBConcurrencyException("The request status changed. Refresh and retry.");
            await using (var expire = new SqlCommand("UPDATE commerce.OrderIntakeQuotes SET QuoteStatus='SUPERSEDED' WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND QuoteStatus='CURRENT';", connection, transaction))
            { AddTenant(expire, tenantId); expire.Parameters.AddWithValue("@id", requestId); await expire.ExecuteNonQueryAsync(token); }
            var quoteId = Guid.NewGuid(); var created = DateTimeOffset.UtcNow; var expires = created.AddMinutes(input.ValidityMinutes);
            var quote = new OrderIntakeQuoteDto(quoteId, revision, "CURRENT", fulfillment,
                deliveryAddress?.Pincode, created, expires, priced.Pricing, quotedItems, deliveryAddress);
            var snapshot = System.Text.Json.JsonSerializer.Serialize(quote);
            await using (var insert = new SqlCommand("""
                INSERT commerce.OrderIntakeQuotes(TenantId,QuoteId,OrderIntakeRequestId,QuoteRevision,QuoteStatus,FulfilmentPreference,Pincode,DeliveryAddressSnapshotJson,
                    PricingSnapshotJson,MerchandiseSubtotal,MerchandiseAmount,MerchandiseTaxAmount,IncludedTaxAmount,AdditionalTaxAmount,
                    ProductSavings,PromotionDiscount,PromotionId,DeliveryCharge,PayableAmount,ExpiresAtUtc,CreatedBy)
                VALUES(@tenant,@quote,@request,@revision,'CURRENT',@fulfilment,@pincode,@address,@snapshot,@subtotal,@merchandise,@tax,@included,@additional,
                    @savings,@discount,@promotion,@delivery,@payable,@expires,@actor);
                INSERT commerce.OrderIntakeQuoteItems(TenantId,QuoteId,QuoteItemId,ProductId,ProductCode,ProductName,RequestedText,IsSubstitution,UnitName,UnitShortName,Quantity,UnitPrice,TaxPercentage,CompareAtPrice,GstPricingType)
                SELECT @tenant,@quote,NEWID(),j.ProductId,j.ProductCode,j.ProductName,j.RequestedText,j.IsSubstitution,j.UnitName,j.UnitShortName,j.Quantity,j.UnitPrice,j.TaxPercentage,j.CompareAtPrice,j.GstPricingType
                FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,ProductCode nvarchar(100),ProductName nvarchar(250),RequestedText nvarchar(300),IsSubstitution bit,UnitName nvarchar(100),UnitShortName nvarchar(32),Quantity decimal(18,4),
                    UnitPrice decimal(18,4),TaxPercentage decimal(9,4),CompareAtPrice decimal(18,4),GstPricingType varchar(16)) j;
                UPDATE commerce.OrderIntakeRequests SET IntakeStatus='AWAITING_CUSTOMER',ConfirmedQuoteRevision=NULL,
                    ConfirmationVerificationMethod=NULL,ConfirmationEvidenceReference=NULL,ConfirmationOperator=NULL,
                    ConfirmationChannel=NULL,CustomerConfirmedAtUtc=NULL,UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND OrderIntakeRequestId=@request AND Version=@version;
                """, connection, transaction))
            {
                AddTenant(insert, tenantId); insert.Parameters.AddWithValue("@quote", quoteId); insert.Parameters.AddWithValue("@request", requestId);
                insert.Parameters.AddWithValue("@revision", revision); insert.Parameters.AddWithValue("@fulfilment", quote.FulfilmentPreference);
                insert.Parameters.AddWithValue("@pincode", (object?)quote.Pincode ?? DBNull.Value); insert.Parameters.AddWithValue("@snapshot", snapshot);
                insert.Parameters.AddWithValue("@address", deliveryAddress is null ? DBNull.Value : System.Text.Json.JsonSerializer.Serialize(deliveryAddress));
                var p = priced.Pricing;
                insert.Parameters.AddWithValue("@subtotal", p.MerchandiseSubtotal); insert.Parameters.AddWithValue("@merchandise", p.MerchandiseAmount);
                insert.Parameters.AddWithValue("@tax", p.MerchandiseTaxAmount); insert.Parameters.AddWithValue("@included", p.IncludedTaxAmount);
                insert.Parameters.AddWithValue("@additional", p.AdditionalTaxAmount); insert.Parameters.AddWithValue("@savings", p.ProductSavings);
                insert.Parameters.AddWithValue("@discount", p.PromotionDiscount); insert.Parameters.AddWithValue("@promotion", (object?)p.PromotionId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@delivery", p.DeliveryCharge); insert.Parameters.AddWithValue("@payable", p.FinalPayableAmount);
                insert.Parameters.AddWithValue("@expires", expires.UtcDateTime); insert.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
                insert.Parameters.AddWithValue("@items", System.Text.Json.JsonSerializer.Serialize(priced.Items)); insert.Parameters.AddWithValue("@version", input.Version);
                if (await insert.ExecuteNonQueryAsync(token) < 2) throw new DBConcurrencyException("The request changed before the quotation could be saved.");
            }
            await InsertEvent(connection, transaction, tenantId, requestId, "QUOTE_CREATED", status, "AWAITING_CUSTOMER", actor,
                $"Quotation revision {revision} created; payable {priced.Pricing.FinalPayableAmount:0.00} INR. No customer message was sent.", token);
            await transaction.CommitAsync(token);
            return revealCustomerContext ? quote : quote with { Pincode = null, DeliveryAddress = null };
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<OrderIntakeQuoteDto?> GetCurrentQuoteAsync(Guid tenantId, Guid requestId, bool revealCustomerContext, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT TOP(1) PricingSnapshotJson,QuoteStatus,ExpiresAtUtc FROM commerce.OrderIntakeQuotes WHERE TenantId=@tenant AND OrderIntakeRequestId=@id ORDER BY QuoteRevision DESC;", connection);
        AddTenant(command, tenantId); command.Parameters.AddWithValue("@id", requestId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var value = System.Text.Json.JsonSerializer.Deserialize<OrderIntakeQuoteDto>(reader.GetString(0));
        if (value is null) return null;
        var status = reader.GetString(1); var expiry = Utc(reader.GetDateTime(2));
        return value with { Status = status == "CURRENT" && expiry <= DateTimeOffset.UtcNow ? "EXPIRED" : status,
            Pincode = revealCustomerContext ? value.Pincode : null,
            DeliveryAddress = revealCustomerContext ? value.DeliveryAddress : null };
    }

    public async Task<OrderIntakeDetail> MarkConfirmationRequestedAsync(Guid tenantId, Guid requestId,
        OrderIntakeConfirmationRequestInput input, string? actor, bool revealMobile, CancellationToken token)
    {
        if (input.Version is not { Length: 8 }) throw new ArgumentException("Refresh the request before recording this action.");
        var channel = input.Channel.Trim().ToUpperInvariant();
        if (channel is not ("PHONE_CALL" or "WHATSAPP" or "IN_PERSON")) throw new ArgumentException("Choose a supported confirmation channel.");
        await EnsureQuoteStillValidAsync(tenantId, requestId, input.QuoteRevision, token);
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await using var command = new SqlCommand("SELECT r.IntakeStatus,r.Version,q.QuoteStatus,q.ExpiresAtUtc FROM commerce.OrderIntakeRequests r WITH(UPDLOCK,HOLDLOCK) JOIN commerce.OrderIntakeQuotes q WITH(UPDLOCK,HOLDLOCK) ON q.TenantId=r.TenantId AND q.OrderIntakeRequestId=r.OrderIntakeRequestId AND q.QuoteRevision=@revision WHERE r.TenantId=@tenant AND r.OrderIntakeRequestId=@id;", connection, transaction);
            AddTenant(command, tenantId); command.Parameters.AddWithValue("@id", requestId); command.Parameters.AddWithValue("@revision", input.QuoteRevision);
            string status, quoteStatus; byte[] version; DateTime expiry;
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("The quotation was not found.");
                status = reader.GetString(0); version = (byte[])reader.GetValue(1); quoteStatus = reader.GetString(2); expiry = reader.GetDateTime(3);
            }
            if (status != OrderIntakeStatuses.AwaitingCustomer || !version.AsSpan().SequenceEqual(input.Version)) throw new DBConcurrencyException("The request changed. Refresh and retry.");
            if (quoteStatus != "CURRENT" || expiry <= DateTime.UtcNow) throw new InvalidOperationException("This quotation is no longer current.");
            await InsertEvent(connection, transaction, tenantId, requestId, "CONFIRMATION_REQUESTED", status, status, actor,
                $"Staff recorded that quotation revision {input.QuoteRevision} was presented through {channel}. No message was sent by the system.", token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, revealMobile, token);
    }

    public async Task<OrderIntakeDetail> RecordConfirmationAsync(Guid tenantId, Guid requestId,
        OrderIntakeConfirmationInput input, string? actor, bool revealMobile, CancellationToken token)
    {
        if (input.Version is not { Length: 8 }) throw new ArgumentException("Refresh the request before recording customer confirmation.");
        if (!input.StaffAcknowledged) throw new ArgumentException("Confirm that the customer explicitly accepted this quotation and its revision.");
        var channel = input.Channel.Trim().ToUpperInvariant(); var verification = input.VerificationMethod.Trim().ToUpperInvariant();
        if (channel is not ("PHONE_CALL" or "WHATSAPP" or "IN_PERSON" or "CUSTOMER_APP")) throw new ArgumentException("Choose a supported confirmation channel.");
        if (verification is not ("VERIFIED_CUSTOMER" or "CALLBACK" or "IN_PERSON" or "CUSTOMER_APP")) throw new ArgumentException("Choose how the customer's identity was verified.");
        if (channel == "PHONE_CALL" && verification != "CALLBACK" && verification != "VERIFIED_CUSTOMER")
            throw new ArgumentException("For phone confirmation, record a verified customer or callback verification method.");
        if (input.EvidenceReference?.Length > 500) throw new ArgumentException("The evidence reference is too long.");
        await EnsureQuoteStillValidAsync(tenantId, requestId, input.QuoteRevision, token);
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await using var command = new SqlCommand("""
                SELECT r.IntakeStatus,r.Version,q.QuoteStatus,q.ExpiresAtUtc
                FROM commerce.OrderIntakeRequests r WITH(UPDLOCK,HOLDLOCK)
                JOIN commerce.OrderIntakeQuotes q WITH(UPDLOCK,HOLDLOCK) ON q.TenantId=r.TenantId AND q.OrderIntakeRequestId=r.OrderIntakeRequestId AND q.QuoteRevision=@revision
                WHERE r.TenantId=@tenant AND r.OrderIntakeRequestId=@id;
                """, connection, transaction);
            AddTenant(command, tenantId); command.Parameters.AddWithValue("@id", requestId); command.Parameters.AddWithValue("@revision", input.QuoteRevision);
            string status; byte[] version; string quoteStatus; DateTime expiry;
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("The requested quotation was not found.");
                status = reader.GetString(0); version = (byte[])reader.GetValue(1); quoteStatus = reader.GetString(2); expiry = reader.GetDateTime(3);
            }
            if (!version.AsSpan().SequenceEqual(input.Version) || status != OrderIntakeStatuses.AwaitingCustomer)
                throw new DBConcurrencyException("The request changed. Refresh before recording confirmation.");
            if (quoteStatus != "CURRENT" || expiry <= DateTime.UtcNow) throw new InvalidOperationException("This quotation has expired or been replaced. Generate a new quotation and obtain customer confirmation again.");
            await using (var confirmSubstitutions = new SqlCommand("UPDATE commerce.OrderIntakeItems SET CustomerApprovedSubstitution=1,MatchStatus='MATCHED',UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND ProposedSubstitution=1;", connection, transaction))
            { AddTenant(confirmSubstitutions, tenantId); confirmSubstitutions.Parameters.AddWithValue("@id", requestId); await confirmSubstitutions.ExecuteNonQueryAsync(token); }
            await using var update = new SqlCommand("UPDATE commerce.OrderIntakeRequests SET IntakeStatus='CONFIRMED',ConfirmationChannel=@channel,CustomerConfirmedAtUtc=SYSUTCDATETIME(),ConfirmedQuoteRevision=@revision,ConfirmationVerificationMethod=@verification,ConfirmationEvidenceReference=@evidence,ConfirmationOperator=@actor,LastCustomerInteractionAtUtc=SYSUTCDATETIME(),UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderIntakeRequestId=@id AND Version=@version;", connection, transaction);
            AddTenant(update, tenantId); update.Parameters.AddWithValue("@id", requestId); update.Parameters.AddWithValue("@version", input.Version);
            update.Parameters.AddWithValue("@channel", channel); update.Parameters.AddWithValue("@revision", input.QuoteRevision); update.Parameters.AddWithValue("@verification", verification);
            update.Parameters.AddWithValue("@evidence", (object?)Limit(input.EvidenceReference, 500) ?? DBNull.Value); update.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
            if (await update.ExecuteNonQueryAsync(token) != 1) throw new DBConcurrencyException("The request changed while confirmation was recorded.");
            await InsertEvent(connection, transaction, tenantId, requestId, "CUSTOMER_CONFIRMED", status, "CONFIRMED", actor,
                $"Quote revision {input.QuoteRevision}; channel {channel}; verification {verification}; evidence reference recorded.", token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return await GetAsync(tenantId, requestId, revealMobile, token);
    }

    public async Task<IntakeSalesOrderDto> ConvertToSalesOrderAsync(Guid tenantId, Guid requestId,
        OrderIntakeConversionInput input, string? actor, CancellationToken token)
    {
        if (input.Version is not { Length: 8 }) throw new ArgumentException("Refresh the confirmed request before creating its Sales Order.");
        var detail = await GetAsync(tenantId, requestId, true, token);
        if (detail.Status != OrderIntakeStatuses.Confirmed || string.IsNullOrWhiteSpace(detail.FulfilmentPreference))
            throw new InvalidOperationException("Only a customer-confirmed request with fulfillment selected can become a Sales Order.");
        var quote = await GetCurrentQuoteAsync(tenantId, requestId, true, token)
            ?? throw new InvalidOperationException("The confirmed quotation is unavailable.");
        if (quote.Revision < 1)
            throw new InvalidOperationException("The quotation revision is invalid.");
        if (quote.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new InvalidOperationException("The customer quotation has expired. Obtain confirmation on a new quotation.");
        var fresh = await checkout.QuoteIntakeAsync(tenantId,
            detail.Items.Select(x => new StorefrontCheckoutItem(x.MatchedProductId ?? Guid.Empty, x.RequestedQuantity ?? 0)).ToArray(),
            quote.FulfilmentPreference, quote.DeliveryAddress?.Pincode, quote.Pricing.PromotionCode, token);
        if (quote.FulfilmentPreference == "DELIVERY" && quote.DeliveryAddress is null)
            throw new InvalidOperationException("The confirmed delivery quotation has no address snapshot. Return the request to review and prepare a new quote.");
        if (quote.DeliveryAddress != detail.DeliveryAddress || !SameQuote(quote.Pricing, fresh.Pricing) || !quote.Items.SequenceEqual(MapQuoteItems(detail, fresh.Items)))
            throw new InvalidOperationException("Price, discount, stock, schedule or availability changed after confirmation. Return the request to review and issue a new quotation.");

        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await using var existing = new SqlCommand("SELECT SalesOrderId FROM commerce.IntakeSalesOrders WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderIntakeRequestId=@id;", connection, transaction);
            AddTenant(existing, tenantId); existing.Parameters.AddWithValue("@id", requestId);
            if (await existing.ExecuteScalarAsync(token) is Guid existingId)
            {
                await transaction.CommitAsync(token);
                return (await GetSalesOrderAsync(tenantId, requestId, false, token))!;
            }
            await using var locked = new SqlCommand("SELECT IntakeStatus,Version,ConfirmedQuoteRevision FROM commerce.OrderIntakeRequests WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND OrderIntakeRequestId=@id;", connection, transaction);
            AddTenant(locked, tenantId); locked.Parameters.AddWithValue("@id", requestId);
            string status; byte[] version; int revision;
            await using (var reader = await locked.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Order intake request was not found.");
                status = reader.GetString(0); version = (byte[])reader.GetValue(1); revision = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            }
            if (status != OrderIntakeStatuses.Confirmed || revision != quote.Revision || !version.AsSpan().SequenceEqual(input.Version))
                throw new DBConcurrencyException("The customer confirmation or request changed. Refresh before creating the Sales Order.");
            var orderId = Guid.NewGuid(); var orderNumber = "SOI-" + orderId.ToString("N")[..12].ToUpperInvariant();
            var snapshot = System.Text.Json.JsonSerializer.Serialize(quote);
            await using (var insert = new SqlCommand("""
                INSERT commerce.IntakeSalesOrders(TenantId,SalesOrderId,OrderNumber,OrderIntakeRequestId,QuoteId,QuoteRevision,OrderStatus,FulfilmentPreference,CustomerName,CustomerMobile,Pincode,DeliveryAddressSnapshotJson,PricingSnapshotJson,PayableAmount,ConfirmedAtUtc,CreatedBy)
                SELECT @tenant,@order,@number,r.OrderIntakeRequestId,@quote,@revision,'CONFIRMED',q.FulfilmentPreference,r.CustomerName,r.CustomerMobile,q.Pincode,q.DeliveryAddressSnapshotJson,@snapshot,q.PayableAmount,r.CustomerConfirmedAtUtc,@actor
                FROM commerce.OrderIntakeRequests r JOIN commerce.OrderIntakeQuotes q ON q.TenantId=r.TenantId AND q.OrderIntakeRequestId=r.OrderIntakeRequestId AND q.QuoteRevision=@revision AND q.QuoteStatus='CURRENT'
                WHERE r.TenantId=@tenant AND r.OrderIntakeRequestId=@request AND q.ExpiresAtUtc>SYSUTCDATETIME();
                IF @@ROWCOUNT<>1 THROW 80020,N'Confirmed current quotation is unavailable.',1;
                INSERT commerce.IntakeSalesOrderItems(TenantId,SalesOrderId,SalesOrderItemId,ProductId,ProductCode,ProductName,RequestedText,IsSubstitution,UnitName,UnitShortName,Quantity,UnitPrice,TaxPercentage,CompareAtPrice,GstPricingType)
                SELECT TenantId,@order,NEWID(),ProductId,ProductCode,ProductName,RequestedText,IsSubstitution,UnitName,UnitShortName,Quantity,UnitPrice,TaxPercentage,CompareAtPrice,GstPricingType FROM commerce.OrderIntakeQuoteItems WHERE TenantId=@tenant AND QuoteId=@quote;
                INSERT commerce.IntakeSalesOrderEvents(TenantId,EventId,SalesOrderId,PreviousStatus,NewStatus,Actor,Note) VALUES(@tenant,NEWID(),@order,NULL,'CONFIRMED',@actor,N'Created from confirmed intake quotation. Non-posting Sales Order; no invoice, payment or inventory movement created.');
                UPDATE commerce.OrderIntakeRequests SET IntakeStatus='CONVERTED',ConvertedOrderId=@order,UpdatedBy=@actor,UpdatedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@tenant AND OrderIntakeRequestId=@request AND Version=@version AND IntakeStatus='CONFIRMED';
                IF @@ROWCOUNT<>1 THROW 80021,N'Intake request changed during Sales Order conversion.',1;
                """, connection, transaction))
            {
                AddTenant(insert, tenantId); insert.Parameters.AddWithValue("@order", orderId); insert.Parameters.AddWithValue("@number", orderNumber);
                insert.Parameters.AddWithValue("@request", requestId); insert.Parameters.AddWithValue("@quote", quote.Id); insert.Parameters.AddWithValue("@revision", quote.Revision);
                insert.Parameters.AddWithValue("@snapshot", snapshot); insert.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
                insert.Parameters.AddWithValue("@version", input.Version); await insert.ExecuteNonQueryAsync(token);
            }
            await InsertEvent(connection, transaction, tenantId, requestId, "SALES_ORDER_CREATED", status, "CONVERTED", actor,
                $"Created non-posting Sales Order {orderNumber} from confirmed quote revision {quote.Revision}.", token);
            await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return (await GetSalesOrderAsync(tenantId, requestId, false, token))!;
    }

    public async Task<IntakeSalesOrderDto?> GetSalesOrderAsync(Guid tenantId, Guid requestId, bool revealCustomerData, CancellationToken token)
    {
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT SalesOrderId,OrderNumber,QuoteId,QuoteRevision,OrderStatus,FulfilmentPreference,PayableAmount,CreatedAtUtc,Version,DeliveryAddressSnapshotJson FROM commerce.IntakeSalesOrders WHERE TenantId=@tenant AND OrderIntakeRequestId=@request; SELECT EventId,PreviousStatus,NewStatus,Actor,Note,CreatedAtUtc FROM commerce.IntakeSalesOrderEvents WHERE TenantId=@tenant AND SalesOrderId=(SELECT SalesOrderId FROM commerce.IntakeSalesOrders WHERE TenantId=@tenant AND OrderIntakeRequestId=@request) ORDER BY CreatedAtUtc,EventId;", connection);
        AddTenant(command, tenantId); command.Parameters.AddWithValue("@request", requestId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var id = reader.GetGuid(0); var number = reader.GetString(1); var quoteId = reader.GetGuid(2); var revision = reader.GetInt32(3);
        var status = reader.GetString(4); var fulfillment = reader.GetString(5); var payable = reader.GetDecimal(6); var created = Utc(reader.GetDateTime(7)); var version = (byte[])reader.GetValue(8);
        var address = revealCustomerData && !reader.IsDBNull(9) ? System.Text.Json.JsonSerializer.Deserialize<OrderIntakeDeliveryAddressSnapshot>(reader.GetString(9)) : null;
        await reader.NextResultAsync(token); var history = new List<OrderIntakeAudit>();
        while (await reader.ReadAsync(token)) history.Add(new("SALES_ORDER_STATUS", NullableString(reader, 1), NullableString(reader, 2), NullableString(reader, 3), NullableString(reader, 4), Utc(reader.GetDateTime(5))));
        return new(id, number, requestId, quoteId, revision, status, fulfillment, payable, created, version, history, address);
    }

    public async Task<IntakeSalesOrderDto> TransitionSalesOrderAsync(Guid tenantId, Guid requestId, string status,
        byte[]? version, string? actor, CancellationToken token)
    {
        if (version is not { Length: 8 }) throw new ArgumentException("Refresh the Sales Order before changing its status.");
        var target = status.Trim().ToUpperInvariant();
        if (target is "COMPLETED") throw new InvalidOperationException("Completion remains gated until billing, payment and fulfillment reconciliation are integrated.");
        if (target is not ("PROCESSING" or "READY" or "CANCELLED" or "FAILED")) throw new ArgumentException("Choose a supported Sales Order status.");
        if (target is "PROCESSING" or "READY")
        {
            var quote = await GetCurrentQuoteAsync(tenantId, requestId, true, token)
                ?? throw new InvalidOperationException("The Sales Order quotation is unavailable.");
            var request = await GetAsync(tenantId, requestId, true, token);
            var fresh = await checkout.QuoteIntakeAsync(tenantId,
                request.Items.Select(x => new StorefrontCheckoutItem(x.MatchedProductId ?? Guid.Empty, x.RequestedQuantity ?? 0)).ToArray(),
                quote.FulfilmentPreference, quote.DeliveryAddress?.Pincode, quote.Pricing.PromotionCode, token);
            if (quote.FulfilmentPreference == "DELIVERY" && quote.DeliveryAddress is null)
                throw new InvalidOperationException("The Sales Order has no valid delivery-address snapshot.");
            if (quote.ExpiresAtUtc <= DateTimeOffset.UtcNow || quote.DeliveryAddress != request.DeliveryAddress || !SameQuote(quote.Pricing, fresh.Pricing) || !quote.Items.SequenceEqual(MapQuoteItems(request, fresh.Items)))
                throw new InvalidOperationException("Price, promotion, stock, schedule or serviceability changed. Return the request to review and obtain fresh customer confirmation before advancing fulfillment.");
        }
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            string previous;
            await using (var update = new SqlCommand("UPDATE commerce.IntakeSalesOrders SET OrderStatus=@target OUTPUT deleted.OrderStatus WHERE TenantId=@tenant AND OrderIntakeRequestId=@request AND Version=@version AND ((OrderStatus='CONFIRMED' AND @target IN('PROCESSING','CANCELLED','FAILED')) OR (OrderStatus='PROCESSING' AND @target IN('READY','CANCELLED','FAILED')) OR (OrderStatus='READY' AND @target IN('CANCELLED','FAILED')));", connection, transaction))
            {
                AddTenant(update, tenantId); update.Parameters.AddWithValue("@request", requestId); update.Parameters.AddWithValue("@version", version); update.Parameters.AddWithValue("@target", target);
                previous = (string?)await update.ExecuteScalarAsync(token) ?? throw new DBConcurrencyException("The Sales Order changed or this status transition is not allowed.");
            }
            await using var find = new SqlCommand("SELECT SalesOrderId FROM commerce.IntakeSalesOrders WHERE TenantId=@tenant AND OrderIntakeRequestId=@request;", connection, transaction);
            AddTenant(find, tenantId); find.Parameters.AddWithValue("@request", requestId); var orderId = (Guid)(await find.ExecuteScalarAsync(token))!;
            await using var evt = new SqlCommand("INSERT commerce.IntakeSalesOrderEvents(TenantId,EventId,SalesOrderId,PreviousStatus,NewStatus,Actor,Note) VALUES(@tenant,NEWID(),@order,@previous,@target,@actor,N'Status changed; this transition does not post billing, payment, inventory or delivery completion.');", connection, transaction);
            AddTenant(evt, tenantId); evt.Parameters.AddWithValue("@order", orderId); evt.Parameters.AddWithValue("@previous", previous); evt.Parameters.AddWithValue("@target", target); evt.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value);
            await evt.ExecuteNonQueryAsync(token); await transaction.CommitAsync(token);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return (await GetSalesOrderAsync(tenantId, requestId, false, token))!;
    }

    private async Task EnsureQuoteStillValidAsync(Guid tenantId, Guid requestId, int revision, CancellationToken token)
    {
        var request = await GetAsync(tenantId, requestId, true, token);
        var quote = await GetCurrentQuoteAsync(tenantId, requestId, true, token)
            ?? throw new InvalidOperationException("The current quotation is unavailable.");
        if (quote.Revision != revision || quote.Status != "CURRENT" || quote.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The quotation is expired or no longer current. Prepare a fresh quote.");
        if (quote.FulfilmentPreference == "DELIVERY" && (quote.DeliveryAddress is null || quote.DeliveryAddress != request.DeliveryAddress))
            throw new InvalidOperationException("The delivery address changed after quotation. Prepare a new quote before requesting or recording customer acceptance.");
        var fresh = await checkout.QuoteIntakeAsync(tenantId,
            request.Items.Select(x => new StorefrontCheckoutItem(x.MatchedProductId ?? Guid.Empty, x.RequestedQuantity ?? 0)).ToArray(),
            quote.FulfilmentPreference, quote.DeliveryAddress?.Pincode, quote.Pricing.PromotionCode, token);
        if (!SameQuote(quote.Pricing, fresh.Pricing) || !quote.Items.SequenceEqual(MapQuoteItems(request, fresh.Items)))
            throw new InvalidOperationException("Price, delivery fee, promotion, stock or serviceability changed. Prepare a new quotation before customer confirmation.");
    }

    private static bool SameQuote(StorefrontCartQuoteDto a, StorefrontCartQuoteDto b) =>
        a.MerchandiseSubtotal == b.MerchandiseSubtotal && a.MerchandiseAmount == b.MerchandiseAmount &&
        a.MerchandiseTaxAmount == b.MerchandiseTaxAmount && a.IncludedTaxAmount == b.IncludedTaxAmount &&
        a.AdditionalTaxAmount == b.AdditionalTaxAmount && a.ProductSavings == b.ProductSavings &&
        a.PromotionDiscount == b.PromotionDiscount && a.PromotionId == b.PromotionId &&
        a.DeliveryCharge == b.DeliveryCharge && a.FinalPayableAmount == b.FinalPayableAmount;

    private static OrderIntakeQuotedItem[] MapQuoteItems(OrderIntakeDetail detail,
        IReadOnlyCollection<StorefrontIntakeQuoteLine> pricedItems)
    {
        return detail.Items.Select(requested =>
        {
            var product = pricedItems.Single(x => x.ProductId == requested.MatchedProductId);
            if (!string.IsNullOrWhiteSpace(requested.RequestedUnit)
                && !string.Equals(requested.RequestedUnit.Trim(), product.UnitName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(requested.RequestedUnit.Trim(), product.UnitShortName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Requested unit '{requested.RequestedUnit.Trim()}' does not match {product.ProductName}'s catalog unit. Select a compatible product or correct the quantity/unit; no conversion was assumed.");
            return new OrderIntakeQuotedItem(product.ProductId, product.ProductCode, product.ProductName, requested.RequestedText,
                requested.RequestedQuantity ?? throw new InvalidOperationException("A quoted item has no quantity."), requested.RequestedUnit,
                product.UnitName, product.UnitShortName, product.UnitPrice, product.TaxPercentage, product.CompareAtPrice,
                product.GstPricingType, requested.ProposedSubstitution);
        }).ToArray();
    }

    private static async Task<IReadOnlyCollection<OrderIntakeProductMatch>> FindMatchesCoreAsync(SqlConnection connection,
        SqlTransaction? transaction, Guid tenantId, string query, int limit, CancellationToken token)
    {
        var term = query.Trim(); if (term.Length is < 2 or > 120) return [];
        await using var command = new SqlCommand("""
            SELECT TOP(@limit) p.ProductId,p.ProductCode,p.ProductName,p.SellingPrice,
                COALESCE(stock.AvailableQuantity,0)
            FROM master.Products p
            OUTER APPLY(SELECT SUM(b.QuantityOnHand-b.QuantityReserved) AvailableQuantity
                FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.TenantId=b.TenantId AND w.WarehouseId=b.WarehouseId AND w.IsActive=1 AND w.IsDeleted=0
                WHERE b.TenantId=p.TenantId AND b.ProductId=p.ProductId) stock
            WHERE p.TenantId=@tenant AND p.IsActive=1 AND p.IsDeleted=0
              AND (CHARINDEX(@term,p.ProductName)>0 OR CHARINDEX(@term,p.ProductCode)>0)
            ORDER BY CASE WHEN UPPER(LTRIM(RTRIM(p.ProductName)))=UPPER(@term) OR UPPER(LTRIM(RTRIM(p.ProductCode)))=UPPER(@term) THEN 0 ELSE 1 END,p.ProductName;
            """, connection, transaction);
        command.Parameters.AddWithValue("@limit", limit); command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@term", term);
        var results = new List<OrderIntakeProductMatch>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) results.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4)));
        return results;
    }

    private static async Task<Dictionary<int, IReadOnlyCollection<OrderIntakeProductMatch>>> FindMatchesForItemsCoreAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, IReadOnlyCollection<ParsedItem> items, CancellationToken token)
    {
        var result = new Dictionary<int, IReadOnlyCollection<OrderIntakeProductMatch>>();
        var usable = items.Select((item, index) => (item, index)).Where(x => x.item.ProductQuery.Length is >= 2 and <= 120).ToArray();
        if (usable.Length == 0) return result;
        var values = string.Join(",", usable.Select(x => $"({x.index},@term{x.index})"));
        await using var command = new SqlCommand($"""
            WITH terms(ItemIndex,SearchTerm) AS (SELECT v.ItemIndex,v.SearchTerm FROM (VALUES {values}) v(ItemIndex,SearchTerm)),
            candidates AS (
                SELECT t.ItemIndex,p.ProductId,p.ProductCode,p.ProductName,p.SellingPrice,
                    ROW_NUMBER() OVER(PARTITION BY t.ItemIndex ORDER BY
                        CASE WHEN UPPER(LTRIM(RTRIM(p.ProductName)))=UPPER(t.SearchTerm)
                                  OR UPPER(LTRIM(RTRIM(p.ProductCode)))=UPPER(t.SearchTerm) THEN 0 ELSE 1 END,p.ProductName) AS MatchNumber
                FROM terms t JOIN master.Products p ON p.TenantId=@tenant AND p.IsActive=1 AND p.IsDeleted=0
                  AND (CHARINDEX(t.SearchTerm,p.ProductName)>0 OR CHARINDEX(t.SearchTerm,p.ProductCode)>0)
            )
            SELECT c.ItemIndex,c.ProductId,c.ProductCode,c.ProductName,c.SellingPrice,COALESCE(stock.AvailableQuantity,0)
            FROM candidates c OUTER APPLY(
                SELECT SUM(b.QuantityOnHand-b.QuantityReserved) AvailableQuantity
                FROM inventory.InventoryBalances b JOIN inventory.Warehouses w ON w.TenantId=b.TenantId
                    AND w.WarehouseId=b.WarehouseId AND w.IsActive=1 AND w.IsDeleted=0
                WHERE b.TenantId=@tenant AND b.ProductId=c.ProductId
            ) stock WHERE c.MatchNumber<=6 ORDER BY c.ItemIndex,c.MatchNumber;
            """, connection, transaction);
        AddTenant(command, tenantId);
        foreach (var term in usable) command.Parameters.AddWithValue($"@term{term.index}", term.item.ProductQuery);
        var rows = new Dictionary<int, List<OrderIntakeProductMatch>>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var index = reader.GetInt32(0);
            if (!rows.TryGetValue(index, out var matches)) rows[index] = matches = [];
            matches.Add(new(reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5)));
        }
        foreach (var row in rows) result[row.Key] = row.Value;
        return result;
    }

    private static async Task<bool> ProductBelongsToTenant(SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, Guid productId, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT CASE WHEN EXISTS(SELECT 1 FROM master.Products WHERE TenantId=@tenant AND ProductId=@product AND IsActive=1 AND IsDeleted=0) THEN 1 ELSE 0 END;", connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@product", productId);
        return (int)(await command.ExecuteScalarAsync(token) ?? 0) == 1;
    }

    private static async Task InsertEvent(SqlConnection connection, SqlTransaction? transaction, Guid tenantId,
        Guid requestId, string type, string? previous, string? next, string? actor, string? note, CancellationToken token)
    {
        await using var command = new SqlCommand("INSERT commerce.OrderIntakeEvents(TenantId,OrderIntakeEventId,OrderIntakeRequestId,EventType,PreviousStatus,NewStatus,Actor,Note) VALUES(@tenant,NEWID(),@request,@type,@previous,@next,@actor,@note);", connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@request", requestId); command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@previous", (object?)previous ?? DBNull.Value); command.Parameters.AddWithValue("@next", (object?)next ?? DBNull.Value);
        command.Parameters.AddWithValue("@actor", (object?)Limit(actor, 256) ?? DBNull.Value); command.Parameters.AddWithValue("@note", (object?)Limit(note, 1000) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static void AddTenant(SqlCommand command, Guid tenantId) => command.Parameters.AddWithValue("@tenant", tenantId);
    private static string? NullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? NullableGuid(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static decimal? NullableDecimal(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static DateTimeOffset? NullableUtc(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Utc(reader.GetDateTime(ordinal));
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string? Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string? Filter(string? value, IReadOnlySet<string> allowed) { if (string.IsNullOrWhiteSpace(value)) return null; var key=value.Trim().ToUpperInvariant(); return allowed.Contains(key) ? key : throw new ArgumentException("An intake filter is invalid."); }
    private static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length is < 7 or > 30 || !PhoneCharacters().IsMatch(trimmed)) throw new ArgumentException("Enter a valid customer mobile number.");
        return trimmed;
    }
    private static bool EqualsSearch(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool CanTransition(string from, string to) => (from, to) switch
    {
        ("NEW", "UNDER_REVIEW" or "REJECTED" or "CANCELLED") => true,
        ("UNDER_REVIEW", "AWAITING_CUSTOMER" or "REJECTED" or "CANCELLED") => true,
        ("AWAITING_CUSTOMER", "UNDER_REVIEW" or "CONFIRMED" or "CANCELLED") => true,
        ("CONFIRMED", "UNDER_REVIEW" or "CANCELLED") => true,
        _ => false
    };

    private static IReadOnlyCollection<ParsedItem> ParseItems(string request)
    {
        var parts = request.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0).ToArray();
        if (parts.Length > 100) throw new ArgumentException("A request can contain up to 100 requested items. Split the list into separate requests.");
        return parts.Select(ParseItem).ToArray();
    }

    private static ParsedItem ParseItem(string text)
    {
        var match = QuantityPrefix().Match(text);
        if (match.Success && decimal.TryParse(match.Groups["quantity"].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
        {
            var rest = match.Groups["rest"].Value.Trim(); var unitMatch = LeadingUnit().Match(rest);
            var unit = unitMatch.Success ? unitMatch.Value : null; var product = unitMatch.Success ? rest[unitMatch.Length..].Trim() : rest;
            if (product.Length > 0) return new(text[..Math.Min(text.Length,300)], quantity, unit, product[..Math.Min(product.Length,120)]);
        }
        var suffix = QuantitySuffix().Match(text);
        if (suffix.Success && decimal.TryParse(suffix.Groups["quantity"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var suffixQuantity))
        {
            var product = suffix.Groups["rest"].Value.Trim(); var unit = suffix.Groups["unit"].Success ? suffix.Groups["unit"].Value : null;
            if (product.Length > 0) return new(text[..Math.Min(text.Length,300)], suffixQuantity, unit, product[..Math.Min(product.Length,120)]);
        }
        var plain = text.Trim(); return new(plain[..Math.Min(plain.Length,300)], null, null, plain[..Math.Min(plain.Length,120)]);
    }

    private static bool LooksLikeOrderRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || text.Trim().Length < 3) return false;
        var trimmed = text.Trim();
        if (KnownCommerceCommand().IsMatch(trimmed)) return false;
        return QuantityPrefix().IsMatch(trimmed) || QuantitySuffix().IsMatch(trimmed)
            || trimmed.Any(c => c is '\n' or ';')
            || trimmed.Split(',').Length >= 3;
    }

    private sealed record ParsedItem(string Text, decimal? Quantity, string? Unit, string ProductQuery);
    [GeneratedRegex("^[0-9]+$|^(hi|hello|menu|confirm|place order|cart|browse|order status|pay online|cod)(\\b|$)|^search\\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KnownCommerceCommand();
    [GeneratedRegex("^\\s*(?<quantity>[0-9]+(?:[.,][0-9]+)?)\\s+(?<rest>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityPrefix();
    [GeneratedRegex("^\\s*(?<rest>.+?)\\s+(?<quantity>[0-9]+(?:\\.[0-9]+)?)\\s*(?<unit>kg|g|gm|gram|grams|litre|liter|l|ml|packet|packets|pcs|piece|pieces)?\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantitySuffix();
    [GeneratedRegex("^(kg|g|gm|gram|grams|litre|liter|l|ml|packet|packets|pcs|piece|pieces)\\b\\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingUnit();
    [GeneratedRegex("^[0-9+()\\- ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneCharacters();
}
