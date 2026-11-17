using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Warehouses;

namespace WhatsBiz.Infrastructure.Warehouses;

public sealed partial class WarehouseStorefrontFulfilmentService(
    IConfiguration configuration,
    ICurrentUserService currentUser,
    IFeatureService features) : IWarehouseStorefrontFulfilmentService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<WarehouseStorefrontFulfilmentDto> GetAsync(Guid warehouseId, CancellationToken token)
    {
        var tenantId = currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required.");
        return await GetAsync(tenantId, warehouseId, token);
    }

    public async Task<WarehouseStorefrontFulfilmentDto> SaveAsync(
        Guid warehouseId, WarehouseStorefrontFulfilmentInput input, CancellationToken token)
    {
        var tenantId = currentUser.TenantId ?? throw new BusinessRuleException("A tenant context is required.");
        var areas = Normalize(input.SellerAreas);
        await using var connection = await Open(tenantId, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var warehouseActive = await ReadWarehouseActive(connection, transaction, tenantId, warehouseId, token);
            if (areas.Any(x => x.IsEnabled) && !warehouseActive)
                throw new BusinessRuleException("Activate this warehouse before enabling storefront fulfilment.");

            var (deliveryEnabled, mode) = await ReadStorefrontSettings(connection, transaction, tenantId, token);
            if (areas.Any(x => x.IsEnabled) && !deliveryEnabled)
                throw new BusinessRuleException("Enable storefront delivery before enabling warehouse fulfilment.");

            var partnerIds = areas.Where(x => x.SellerKind == "PARTNER").Select(x => x.PartnerSellerId!.Value).Distinct().ToArray();
            if (partnerIds.Length > 0 && (!await features.IsEnabledAsync(tenantId, FeatureKeys.PartnerSellerManagement, token)
                || !await features.IsEnabledAsync(tenantId, FeatureKeys.CityMarketplace, token)))
                throw new BusinessRuleException("Partner seller fulfilment is not enabled for this retailer.");
            foreach (var sellerId in partnerIds)
            {
                await using var seller = new SqlCommand("SELECT COUNT(1) FROM commerce.PartnerSellers WITH(UPDLOCK,HOLDLOCK) WHERE OperatingTenantId=@tenant AND PartnerSellerId=@seller AND SellerStatus='ACTIVE' AND VerificationStatus='VERIFIED';", connection, transaction);
                seller.Parameters.AddWithValue("@tenant", tenantId);
                seller.Parameters.AddWithValue("@seller", sellerId);
                if (Convert.ToInt32(await seller.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 1)
                    throw new BusinessRuleException("Only an active, verified partner retailer belonging to this tenant can be configured.");
            }

            foreach (var area in areas.Where(x => x.IsEnabled))
                foreach (var pincode in area.Pincodes)
                    if (!await IsRetailerPincodeServiceable(connection, transaction, tenantId, mode, pincode, token))
                        throw new BusinessRuleException($"Pincode {pincode} is not currently serviceable by this storefront.");

            await using (var clear = new SqlCommand("DELETE commerce.WarehouseStorefrontServiceAreas WHERE TenantId=@tenant AND WarehouseId=@warehouse;", connection, transaction))
            {
                clear.Parameters.AddWithValue("@tenant", tenantId);
                clear.Parameters.AddWithValue("@warehouse", warehouseId);
                await clear.ExecuteNonQueryAsync(token);
            }

            foreach (var area in areas)
            foreach (var pincode in area.Pincodes)
            {
                await using var insert = new SqlCommand("INSERT commerce.WarehouseStorefrontServiceAreas(TenantId,WarehouseId,SellerKind,PartnerSellerId,Pincode,IsEnabled) VALUES(@tenant,@warehouse,@kind,@seller,@pincode,@enabled);", connection, transaction);
                insert.Parameters.AddWithValue("@tenant", tenantId);
                insert.Parameters.AddWithValue("@warehouse", warehouseId);
                insert.Parameters.AddWithValue("@kind", area.SellerKind);
                insert.Parameters.AddWithValue("@seller", (object?)area.PartnerSellerId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@pincode", pincode);
                insert.Parameters.AddWithValue("@enabled", area.IsEnabled);
                await insert.ExecuteNonQueryAsync(token);
            }

            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        return await GetAsync(warehouseId, token);
    }

    // This method is for trusted backend allocation planning only. TenantId must come from the
    // resolved storefront tenant, never from customer input. It performs no stock mutation.
    public async Task<WarehouseFulfilmentEligibilityResult> CheckAsync(
        WarehouseFulfilmentEligibilityRequest request, CancellationToken token)
    {
        var validation = ValidateRequest(request);
        if (validation.Result is not null) return validation.Result;
        await using var connection = await Open(request.TenantId, token);
        return await CheckCoreAsync(request, validation.Kind!, validation.Pincode!, connection, null, token);
    }

    // The marketplace order transaction calls this same eligibility service while retaining
    // serializable locks. Inventory quantity is evaluated separately by the allocator.
    internal async Task<WarehouseFulfilmentEligibilityResult> CheckWithinTransactionAsync(
        WarehouseFulfilmentEligibilityRequest request, SqlConnection connection, SqlTransaction transaction, CancellationToken token)
    {
        var validation = ValidateRequest(request);
        if (validation.Result is not null) return validation.Result;
        await using var context = new SqlCommand("SELECT TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));", connection, transaction);
        if (await context.ExecuteScalarAsync(token) is not Guid sessionTenant || sessionTenant != request.TenantId)
            return new(false, "WAREHOUSE_NOT_FOUND", "The warehouse is not available for this retailer.");
        return await CheckCoreAsync(request, validation.Kind!, validation.Pincode!, connection, transaction, token);
    }

    private async Task<WarehouseFulfilmentEligibilityResult> CheckCoreAsync(
        WarehouseFulfilmentEligibilityRequest request, string kind, string pincode,
        SqlConnection connection, SqlTransaction? transaction, CancellationToken token)
    {
        if (kind == "PARTNER" && (!await features.IsEnabledAsync(request.TenantId, FeatureKeys.PartnerSellerManagement, token)
            || !await features.IsEnabledAsync(request.TenantId, FeatureKeys.CityMarketplace, token)))
            return new(false, "SELLER_INELIGIBLE", "Partner seller fulfilment is not enabled for this retailer.");
        bool tenantWarehouse; bool active; bool configured; bool delivery; bool retailerServiceable; bool warehouseServiceable; bool sellerEligible;
        await using (var command = new SqlCommand("""
            SELECT w.WarehouseId,w.IsActive,w.IsDeleted,
              CONVERT(bit,CASE WHEN c.DeliveryEnabled=1 THEN 1 ELSE 0 END),COALESCE(c.DeliveryCoverageMode,'ALLOWLIST'),
              CONVERT(bit,CASE WHEN a.WarehouseId IS NULL THEN 0 ELSE 1 END),
              CONVERT(bit,CASE WHEN a.IsEnabled=1 THEN 1 ELSE 0 END),
              CONVERT(bit,CASE WHEN @kind='OWNED' AND @seller IS NULL AND NOT EXISTS(
                SELECT 1 FROM commerce.PartnerProductListings l WITH(UPDLOCK,HOLDLOCK) WHERE l.OperatingTenantId=@tenant AND l.ProductId=@product)
                THEN 1 WHEN @kind='PARTNER' AND EXISTS(
                  SELECT 1 FROM commerce.PartnerProductListings l WITH(UPDLOCK,HOLDLOCK)
                  JOIN commerce.PartnerSellers s WITH(UPDLOCK,HOLDLOCK) ON s.OperatingTenantId=l.OperatingTenantId AND s.PartnerSellerId=l.PartnerSellerId
                  JOIN commerce.MarketplaceCities mc WITH(UPDLOCK,HOLDLOCK) ON mc.CityId=s.CityId AND mc.IsActive=1 AND mc.IsPublished=1
                  JOIN commerce.CityRetailers cr WITH(UPDLOCK,HOLDLOCK) ON cr.CityId=mc.CityId AND cr.TenantId=@tenant AND cr.IsActive=1
                  WHERE l.OperatingTenantId=@tenant AND l.ProductId=@product AND l.PartnerSellerId=@seller
                    AND l.ListingStatus='APPROVED' AND s.SellerStatus='ACTIVE' AND s.VerificationStatus='VERIFIED'
                    AND s.CityId=@city)
                THEN 1 ELSE 0 END)
            FROM inventory.Warehouses w WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN commerce.StorefrontConfigurations c WITH(UPDLOCK,HOLDLOCK) ON c.TenantId=w.TenantId
            LEFT JOIN commerce.WarehouseStorefrontServiceAreas a WITH(UPDLOCK,HOLDLOCK) ON a.TenantId=w.TenantId AND a.WarehouseId=w.WarehouseId
              AND a.SellerKind=@kind AND ISNULL(a.PartnerSellerId,'00000000-0000-0000-0000-000000000000')=ISNULL(@seller,'00000000-0000-0000-0000-000000000000')
              AND a.Pincode=@pincode AND a.IsEnabled=1
            WHERE w.TenantId=@tenant AND w.WarehouseId=@warehouse;
            """, connection, transaction))
        {
            AddEligibilityParameters(command, request, kind!);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                return WarehouseStorefrontEligibilityPolicy.Evaluate(false, false, false, false, false, false, false);
            tenantWarehouse = true;
            active = reader.GetBoolean(1) && !reader.GetBoolean(2);
            delivery = reader.GetBoolean(3);
            var coverageMode = reader.GetString(4);
            configured = reader.GetBoolean(5);
            warehouseServiceable = reader.GetBoolean(6);
            sellerEligible = reader.GetBoolean(7);
            await reader.CloseAsync();
            retailerServiceable = transaction is null
                ? await IsRetailerPincodeServiceable(connection, request.TenantId, coverageMode, pincode, token)
                : await IsRetailerPincodeServiceable(connection, transaction, request.TenantId, coverageMode, pincode, token);
        }
        return WarehouseStorefrontEligibilityPolicy.Evaluate(tenantWarehouse, active, configured, sellerEligible, delivery, retailerServiceable, warehouseServiceable);
    }

    private static (string? Kind, string? Pincode, WarehouseFulfilmentEligibilityResult? Result) ValidateRequest(WarehouseFulfilmentEligibilityRequest request)
    {
        var pincode = request.Pincode?.Trim() ?? string.Empty;
        if (request.TenantId == Guid.Empty || request.WarehouseId == Guid.Empty || request.ProductId == Guid.Empty
            || !PincodeRegex().IsMatch(pincode))
            return (null, null, new(false, "INVALID_REQUEST", "Warehouse fulfilment details are incomplete."));
        var kind = request.SellerKind?.Trim().ToUpperInvariant();
        if (kind is not ("OWNED" or "PARTNER") || (kind == "OWNED" && request.PartnerSellerId is not null)
            || (kind == "PARTNER" && (request.PartnerSellerId is null || request.MarketplaceCityId is null)))
            return (null, null, new(false, "SELLER_INELIGIBLE", "The product seller is not eligible for this warehouse."));
        return (kind, pincode, null);
    }

    private static List<NormalizedArea> Normalize(IReadOnlyCollection<WarehouseStorefrontSellerAreaInput>? input)
    {
        if (input is null || input.Count > 101)
            throw new BusinessRuleException("Provide a valid set of warehouse seller service areas.");
        var result = new List<NormalizedArea>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in input)
        {
            var kind = source.SellerKind?.Trim().ToUpperInvariant();
            if (kind is not ("OWNED" or "PARTNER") || (kind == "OWNED" && source.PartnerSellerId is not null)
                || (kind == "PARTNER" && source.PartnerSellerId is null))
                throw new BusinessRuleException("Choose retailer-owned inventory or a valid partner seller.");
            var key = $"{kind}:{source.PartnerSellerId?.ToString("D") ?? "OWNED"}";
            if (!keys.Add(key)) throw new BusinessRuleException("A seller can only be configured once per warehouse.");
            var pins = (source.Pincodes ?? []).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (pins.Length > 1000 || pins.Any(x => !PincodeRegex().IsMatch(x)))
                throw new BusinessRuleException("Enter supported six-digit service-area pincodes.");
            if (source.IsEnabled && pins.Length == 0)
                throw new BusinessRuleException("Add at least one serviceable pincode before enabling this seller at the warehouse.");
            result.Add(new(kind!, source.PartnerSellerId, source.IsEnabled, pins));
        }
        return result;
    }

    private async Task<WarehouseStorefrontFulfilmentDto> GetAsync(Guid tenant, Guid warehouseId, CancellationToken token)
    {
        await using var connection = await Open(tenant, token);
        var active = await ReadWarehouseActive(connection, tenant, warehouseId, token);
        var (deliveryEnabled, mode) = await ReadStorefrontSettings(connection, tenant, token);
        var serviceable = await ReadRetailerServiceablePincodes(connection, tenant, mode, token);
        var rows = await ReadConfiguredAreas(connection, tenant, warehouseId, token);
        var options = await ReadSellerOptions(connection, tenant, token);
        var areas = new List<WarehouseStorefrontSellerAreaDto> { BuildArea("OWNED", null, "Retailer-owned inventory", rows) };
        areas.AddRange(options.Select(x => BuildArea("PARTNER", x.Id, x.Name, rows)));
        var eligible = false;
        if (active && deliveryEnabled)
            foreach (var area in areas.Where(x => x.IsEnabled))
                foreach (var pin in area.Pincodes)
                    if (await IsRetailerPincodeServiceable(connection, tenant, mode, pin, token))
                    {
                        eligible = true;
                        break;
                    }
        return new(warehouseId, active, deliveryEnabled, mode, eligible, serviceable.Order().ToArray(), areas);
    }

    private static async Task<List<AreaRow>> ReadConfiguredAreas(SqlConnection connection, Guid tenant, Guid warehouse, CancellationToken token)
    {
        var rows = new List<AreaRow>();
        await using var command = new SqlCommand("SELECT a.SellerKind,a.PartnerSellerId,COALESCE(s.SellerName,N'Retailer-owned inventory'),a.Pincode,a.IsEnabled FROM commerce.WarehouseStorefrontServiceAreas a LEFT JOIN commerce.PartnerSellers s ON s.OperatingTenantId=a.TenantId AND s.PartnerSellerId=a.PartnerSellerId WHERE a.TenantId=@tenant AND a.WarehouseId=@warehouse ORDER BY a.SellerKind,s.SellerName,a.Pincode;", connection);
        command.Parameters.AddWithValue("@tenant", tenant); command.Parameters.AddWithValue("@warehouse", warehouse);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetString(3).Trim(), reader.GetBoolean(4)));
        return rows;
    }

    private static WarehouseStorefrontSellerAreaDto BuildArea(string kind, Guid? seller, string name, IReadOnlyCollection<AreaRow> rows)
    {
        var matching = rows.Where(x => x.SellerKind == kind && x.PartnerSellerId == seller).ToArray();
        return new(kind, seller, name, matching.Any(x => x.IsEnabled), matching.Select(x => x.Pincode).Distinct().Order().ToArray());
    }

    private static async Task<IReadOnlyCollection<SellerOption>> ReadEligibleSellers(SqlConnection connection, Guid tenant, CancellationToken token)
    {
        var result = new List<SellerOption>();
        await using var command = new SqlCommand("""
            SELECT s.PartnerSellerId,s.SellerName
            FROM commerce.PartnerSellers s
            JOIN commerce.MarketplaceCities c ON c.CityId=s.CityId AND c.IsActive=1 AND c.IsPublished=1
            JOIN commerce.CityRetailers r ON r.CityId=c.CityId AND r.TenantId=s.OperatingTenantId AND r.IsActive=1
            WHERE s.OperatingTenantId=@tenant AND s.SellerStatus='ACTIVE' AND s.VerificationStatus='VERIFIED'
            ORDER BY s.SellerName;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }

    private async Task<IReadOnlyCollection<SellerOption>> ReadSellerOptions(SqlConnection connection, Guid tenant, CancellationToken token)
    {
        if (!await features.IsEnabledAsync(tenant, FeatureKeys.PartnerSellerManagement, token)
            || !await features.IsEnabledAsync(tenant, FeatureKeys.CityMarketplace, token))
            return [];
        return await ReadEligibleSellers(connection, tenant, token);
    }

    private static async Task<HashSet<string>> ReadRetailerServiceablePincodes(SqlConnection connection, Guid tenant, string mode, CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!mode.Equals("ALLOWLIST", StringComparison.OrdinalIgnoreCase)) return result;
        await using var command = new SqlCommand("SELECT Pincode FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND IsActive=1 AND IsExcluded=0;", connection);
        command.Parameters.AddWithValue("@tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(0).Trim());
        return result;
    }

    private static async Task<bool> IsRetailerPincodeServiceable(SqlConnection connection, SqlTransaction? transaction, Guid tenant, string mode, string pincode, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT CASE WHEN @delivery=0 THEN 0 WHEN @mode='PAN_INDIA' THEN CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1 AND IsExcluded=1) THEN 0 ELSE 1 END ELSE CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1 AND IsExcluded=0) THEN 1 ELSE 0 END END FROM commerce.StorefrontConfigurations WHERE TenantId=@tenant;", connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenant); command.Parameters.AddWithValue("@pincode", pincode); command.Parameters.AddWithValue("@mode", mode); command.Parameters.AddWithValue("@delivery", true);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token) ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<(bool Enabled, string Mode)> ReadStorefrontSettings(SqlConnection connection, Guid tenant, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT CONVERT(bit,CASE WHEN DeliveryEnabled=1 THEN 1 ELSE 0 END),COALESCE(DeliveryCoverageMode,'ALLOWLIST') FROM commerce.StorefrontConfigurations WHERE TenantId=@tenant;", connection);
        command.Parameters.AddWithValue("@tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetBoolean(0), reader.GetString(1)) : (false, "ALLOWLIST");
    }

    private static async Task<(bool Enabled, string Mode)> ReadStorefrontSettings(SqlConnection connection, SqlTransaction transaction, Guid tenant, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT CONVERT(bit,CASE WHEN DeliveryEnabled=1 THEN 1 ELSE 0 END),COALESCE(DeliveryCoverageMode,'ALLOWLIST') FROM commerce.StorefrontConfigurations WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant;", connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetBoolean(0), reader.GetString(1)) : (false, "ALLOWLIST");
    }

    private static async Task<bool> ReadWarehouseActive(SqlConnection connection, Guid tenant, Guid warehouse, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT IsActive FROM inventory.Warehouses WHERE TenantId=@tenant AND WarehouseId=@warehouse AND IsDeleted=0;", connection);
        command.Parameters.AddWithValue("@tenant", tenant); command.Parameters.AddWithValue("@warehouse", warehouse);
        var value = await command.ExecuteScalarAsync(token);
        if (value is null) throw new EntityNotFoundException("Warehouse not found.");
        return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ReadWarehouseActive(SqlConnection connection, SqlTransaction transaction, Guid tenant, Guid warehouse, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT IsActive FROM inventory.Warehouses WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND WarehouseId=@warehouse AND IsDeleted=0;", connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenant); command.Parameters.AddWithValue("@warehouse", warehouse);
        var value = await command.ExecuteScalarAsync(token);
        if (value is null) throw new EntityNotFoundException("Warehouse not found.");
        return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> IsRetailerPincodeServiceable(SqlConnection connection, Guid tenant, string mode, string pincode, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT CASE WHEN DeliveryEnabled=0 THEN 0 WHEN @mode='PAN_INDIA' THEN CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1 AND IsExcluded=1) THEN 0 ELSE 1 END ELSE CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1 AND IsExcluded=0) THEN 1 ELSE 0 END END FROM commerce.StorefrontConfigurations WHERE TenantId=@tenant;", connection);
        command.Parameters.AddWithValue("@tenant", tenant); command.Parameters.AddWithValue("@pincode", pincode); command.Parameters.AddWithValue("@mode", mode);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token) ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    private static void AddEligibilityParameters(SqlCommand command, WarehouseFulfilmentEligibilityRequest request, string kind)
    {
        command.Parameters.AddWithValue("@tenant", request.TenantId); command.Parameters.AddWithValue("@warehouse", request.WarehouseId);
        command.Parameters.AddWithValue("@product", request.ProductId); command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@seller", (object?)request.PartnerSellerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@city", (object?)request.MarketplaceCityId ?? DBNull.Value);
        command.Parameters.AddWithValue("@pincode", request.Pincode);
    }

    private async Task<SqlConnection> Open(Guid tenant, CancellationToken token)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", connection);
        context.Parameters.AddWithValue("@tenant", tenant);
        await context.ExecuteNonQueryAsync(token);
        await using var check = new SqlCommand("SELECT CASE WHEN EXISTS(SELECT 1 FROM core.Tenants WHERE TenantId=@tenant AND IsActive=1) THEN 1 ELSE 0 END;", connection);
        check.Parameters.AddWithValue("@tenant", tenant);
        if (Convert.ToInt32(await check.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 1)
        {
            await connection.DisposeAsync();
            throw new WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException("Tenant context is not available.");
        }
        return connection;
    }

    [GeneratedRegex("^[0-9]{6}$", RegexOptions.CultureInvariant)] private static partial Regex PincodeRegex();
    private sealed record NormalizedArea(string SellerKind, Guid? PartnerSellerId, bool IsEnabled, IReadOnlyCollection<string> Pincodes);
    private sealed record AreaRow(string SellerKind, Guid? PartnerSellerId, string SellerName, string Pincode, bool IsEnabled);
    private sealed record SellerOption(Guid Id, string Name);
}
