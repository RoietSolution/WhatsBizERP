using System.Net.Mail;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed partial class StorefrontCheckoutService(
    IConfiguration configuration,
    IPOSEngine pos,
    ICommercePaymentService payments,
    IStorefrontCustomerService customers,
    IStorefrontCustomerBlacklistService blacklist,
    IStorefrontAutomatedNotificationService automatedNotifications,
    IStorefrontAvailabilityService availability,
    ILogger<StorefrontCheckoutService> logger,
    IFeatureService features,
    IMarketplaceOrderAllocationService marketplaceAllocation) : IStorefrontCheckoutService
{
    private sealed record CheckoutIdempotencyRequest(
        Guid TenantId,
        Guid CustomerId,
        string CustomerName,
        string RecipientName,
        string Mobile,
        string? Email,
        string DeliveryAddress,
        string? Pincode,
        string? AddressLine1,
        string? AddressLine2,
        string? Landmark,
        string? City,
        string? District,
        string? State,
        string Country,
        IReadOnlyCollection<StorefrontCheckoutItem> Items,
        string? PromoCode,
        decimal ExpectedPayableAmount,
        string PaymentMethod);

    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    private async Task RejectPartnerSellerCheckout(Guid tenantId,IReadOnlyCollection<StorefrontCheckoutItem> items,CancellationToken token)
    {
        if (await ContainsPartnerSellerItems(tenantId, items, token))
            throw new BusinessRuleException("This partner-seller product is listed for discovery only. Online ordering will be enabled after seller, payment and invoicing arrangements are approved.");
    }

    private async Task<bool> ContainsPartnerSellerItems(Guid tenantId,IReadOnlyCollection<StorefrontCheckoutItem> items,CancellationToken token)
    {
        await using var connection=await Open(tenantId,token);
        await using var command=new SqlCommand("SELECT TOP(1) 1 FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,Quantity decimal(18,4)) j JOIN commerce.PartnerProductListings l ON l.OperatingTenantId=@tenant AND l.ProductId=j.ProductId",connection);
        command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@items",JsonSerializer.Serialize(items));
        return await command.ExecuteScalarAsync(token)is not null;
    }

    public async Task<StorefrontDeliveryAvailabilityDto?> CheckDeliveryAsync(string storeKey, string pincode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(pincode) || !PincodePattern().IsMatch(pincode)) throw new BusinessRuleException("Enter a valid six-digit Indian pincode.");
        var tenantId = await ResolveTenant(storeKey, token);
        if (tenantId is null) return null;
        await using var connection = await Open(tenantId.Value, token);
        await using var settings = new SqlCommand("SELECT CONVERT(bit,CASE WHEN s.DeliveryEnabled=1 THEN 1 ELSE 0 END),COALESCE(s.DeliveryCoverageMode,'ALLOWLIST') FROM commerce.StorefrontConfigurations s WHERE s.TenantId=@tenant", connection);
        settings.Parameters.AddWithValue("@tenant", tenantId.Value);
        await using var reader = await settings.ExecuteReaderAsync(token);
        var enabled = false;
        var mode = "ALLOWLIST";
        if (await reader.ReadAsync(token)) { enabled = reader.GetBoolean(0); mode = reader.IsDBNull(1) ? "ALLOWLIST" : reader.GetString(1); }
        await reader.DisposeAsync();
        await using var serviceable = new SqlCommand("SELECT CASE WHEN @mode='PAN_INDIA' THEN CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsExcluded=1 AND IsActive=1) THEN 0 ELSE 1 END ELSE CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1 AND IsExcluded=0) THEN 1 ELSE 0 END END", connection);
        serviceable.Parameters.AddWithValue("@tenant", tenantId.Value); serviceable.Parameters.AddWithValue("@pincode", pincode); serviceable.Parameters.AddWithValue("@mode", mode);
        return new(enabled, Convert.ToInt32(await serviceable.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 1);
    }

    public async Task<StorefrontDeliveryAvailabilityDto> CheckDeliveryForTenantAsync(Guid tenantId, string pincode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(pincode) || !PincodePattern().IsMatch(pincode))
            throw new BusinessRuleException("Enter a valid six-digit Indian pincode.");
        await using var connection = await Open(tenantId, token);
        var mode = "ALLOWLIST"; var enabled = false;
        await using (var settings = new SqlCommand("SELECT CONVERT(bit,CASE WHEN DeliveryEnabled=1 THEN 1 ELSE 0 END),COALESCE(DeliveryCoverageMode,'ALLOWLIST') FROM commerce.StorefrontConfigurations WHERE TenantId=@tenant", connection))
        {
            settings.Parameters.AddWithValue("@tenant", tenantId);
            await using var reader = await settings.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) { enabled = reader.GetBoolean(0); mode = reader.IsDBNull(1) ? "ALLOWLIST" : reader.GetString(1); }
        }
        await using var serviceable = new SqlCommand("SELECT CASE WHEN @mode='PAN_INDIA' THEN CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsExcluded=1 AND IsActive=1) THEN 0 ELSE 1 END ELSE CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1 AND IsExcluded=0) THEN 1 ELSE 0 END END", connection);
        serviceable.Parameters.AddWithValue("@tenant", tenantId); serviceable.Parameters.AddWithValue("@pincode", pincode); serviceable.Parameters.AddWithValue("@mode", mode);
        return new(enabled, Convert.ToInt32(await serviceable.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 1);
    }
    public async Task<StorefrontCartQuoteDto?> QuoteAsync(string storeKey, StorefrontCartQuoteInput input,
        string? customerSessionToken, CancellationToken token)
    {
        var tenantId = await ResolveTenant(storeKey, token);
        if (tenantId is null) return null;
        var items = NormalizeItems(input.Items);
        var marketplaceEnabled = await features.IsEnabledAsync(tenantId.Value, FeatureKeys.MarketplaceCheckout, token);
        var (_, lines, initialWarnings) = await PriceCart(tenantId.Value, JsonSerializer.Serialize(items), items.Length, false, token,
            checkCommonWarehouse: !marketplaceEnabled);
        IReadOnlyCollection<StorefrontCartFulfillmentWarning> warnings = initialWarnings;
        var mayArriveSeparately = false;
        if (marketplaceEnabled && !string.IsNullOrWhiteSpace(input.Pincode))
        {
            var eligibility = await marketplaceAllocation.CheckAvailabilityAsync(tenantId.Value, items, input.Pincode, token);
            mayArriveSeparately = eligibility.IsEligible && eligibility.MayArriveSeparately;
            warnings = eligibility.IsEligible ? [] : [new(eligibility.ReasonCode ?? "MARKETPLACE_UNAVAILABLE",
                eligibility.CustomerMessage ?? "These items are not currently available for delivery to this address.")];
        }
        var authenticated = string.IsNullOrWhiteSpace(customerSessionToken) ? null
            : await customers.GetSessionAsync(storeKey, customerSessionToken, token);
        var quote = await CalculateQuote(tenantId.Value, lines, input.Pincode, authenticated?.Id, input.PromoCode, warnings, token);
        return quote with { MayArriveSeparately = mayArriveSeparately };
    }

    public async Task<StorefrontIntakeQuoteDto> QuoteIntakeAsync(Guid tenantId,
        IReadOnlyCollection<StorefrontCheckoutItem> items, string fulfilmentPreference,
        string? pincode, string? promotionCode, CancellationToken token)
    {
        var normalizedItems = NormalizeItems(items);
        var fulfillment = fulfilmentPreference.Trim().ToUpperInvariant();
        if (fulfillment is not ("PICKUP" or "DELIVERY"))
            throw new BusinessRuleException("Choose pickup or delivery before preparing a quotation.");
        if (fulfillment == "DELIVERY" && (string.IsNullOrWhiteSpace(pincode) || !PincodePattern().IsMatch(pincode)))
            throw new BusinessRuleException("A valid delivery pincode is required before quoting delivery.");
        if (fulfillment == "PICKUP") pincode = null;

        var (_, lines, warnings) = await PriceCart(tenantId, JsonSerializer.Serialize(normalizedItems), normalizedItems.Length, false, token);
        if (warnings.Count != 0)
            throw new BusinessRuleException("The selected products are not currently available together from a fulfillment warehouse.");
        await availability.EnsureOrderEligibleAsync(tenantId, normalizedItems.Select(x => x.ProductId).ToArray(), token);
        var pricing = await CalculateQuote(tenantId, lines, pincode, null, promotionCode, [], token,
            pickup: fulfillment == "PICKUP");
        if (fulfillment == "DELIVERY" && (!pricing.IsDeliveryEnabled || !pricing.IsPincodeServiceable))
            throw new BusinessRuleException("Delivery is not available for the supplied pincode. Choose pickup or verify the address with the retailer.");
        return new(pricing, lines.Select(x => new StorefrontIntakeQuoteLine(x.ProductId, x.ProductCode,
            x.ProductName, x.Quantity, x.UnitPrice, x.TaxPercentage, x.CompareAtPrice, x.GstPricingType, x.UnitName, x.UnitShortName)).ToArray());
    }

    private static StorefrontCheckoutItem[] NormalizeItems(IReadOnlyCollection<StorefrontCheckoutItem>? source)
    {
        var items = (source ?? []).GroupBy(x => x.ProductId)
            .Select(x => new StorefrontCheckoutItem(x.Key, x.Sum(y => y.Quantity))).ToArray();
        if (items.Length is 0 or > 100 || items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0 || x.Quantity > 9999))
            throw new BusinessRuleException("The cart contains invalid items or quantities.");
        return items;
    }

    private async Task<StorefrontCartQuoteDto> CalculateQuote(Guid tenantId, IReadOnlyCollection<PricedLine> lines,
        string? pincode, Guid? verifiedCustomerId, string? promotionCode, IReadOnlyCollection<StorefrontCartFulfillmentWarning> fulfillmentWarnings, CancellationToken token,
        bool pickup = false)
    {
        if (!string.IsNullOrWhiteSpace(pincode) && !PincodePattern().IsMatch(pincode))
            throw new BusinessRuleException("Enter a valid six-digit Indian pincode.");
        await using var connection = await Open(tenantId, token);
        StorefrontPricingConfiguration settings;
        var coverageMode = "ALLOWLIST";
        await using (var command = new SqlCommand("SELECT CONVERT(bit,CASE WHEN s.DeliveryEnabled=1 THEN 1 ELSE 0 END),s.StandardDeliveryCharge,s.FreeDeliveryEnabled,s.FreeDeliveryThreshold,COALESCE(s.DeliveryCoverageMode,'ALLOWLIST') FROM commerce.StorefrontConfigurations s WHERE s.TenantId=@tenant", connection))
        {
            command.Parameters.AddWithValue("@tenant", tenantId);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                settings = new(reader.GetBoolean(0), reader.GetDecimal(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetDecimal(3));
                coverageMode = reader.IsDBNull(4) ? "ALLOWLIST" : reader.GetString(4);
            }
            else
            {
                settings = new(false, 0, false, null);
            }
        if (pickup) settings = new(false, 0, false, null);
        }
        var serviceable = false;
        if (pincode is not null && PincodePattern().IsMatch(pincode))
        {
            await using var command = new SqlCommand("SELECT CASE WHEN @mode='PAN_INDIA' THEN CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsExcluded=1 AND IsActive=1) THEN 0 ELSE 1 END ELSE CASE WHEN EXISTS(SELECT 1 FROM commerce.StorefrontServiceablePincodes WHERE TenantId=@tenant AND Pincode=@pincode AND IsActive=1 AND IsExcluded=0) THEN 1 ELSE 0 END END", connection);
            command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@pincode", pincode); command.Parameters.AddWithValue("@mode", coverageMode);
            serviceable = (int)(await command.ExecuteScalarAsync(token) ?? 0) == 1;
        }
        bool hasOrder = false;
        if (verifiedCustomerId is Guid customerId)
        {
            await using var command = new SqlCommand("""
                SELECT CASE WHEN EXISTS(
                  SELECT 1 FROM sales.SalesInvoices i
                  JOIN integration.WhatsAppCommerceOrders w ON w.InvoiceId=i.InvoiceId AND w.TenantId=i.TenantId AND w.SourceChannel=N'STOREFRONT'
                  WHERE i.TenantId=@tenant AND i.CustomerId=@customer AND i.Status=N'COMPLETED'
                ) OR EXISTS(
                  SELECT 1 FROM commerce.StorefrontPromotionUses u
                  JOIN sales.SalesInvoices x ON x.InvoiceId=u.InvoiceId AND x.TenantId=u.TenantId
                  OUTER APPLY(SELECT TOP(1) cp.Status FROM commerce.CommercePayments cp WHERE cp.TenantId=x.TenantId AND cp.InvoiceId=x.InvoiceId ORDER BY cp.AttemptNumber DESC) payment
                  WHERE u.TenantId=@tenant AND u.CustomerId=@customer AND x.Status NOT IN(N'CANCELLED',N'VOID') AND (x.Status=N'COMPLETED' OR payment.Status IS NULL OR payment.Status<>N'FAILED')
                ) THEN 1 ELSE 0 END;
                """, connection);
            command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@customer", customerId);
            hasOrder = (int)(await command.ExecuteScalarAsync(token) ?? 0) > 0;
        }
        var offers = new List<StorefrontPromotionCandidate>();
        await using (var command = new SqlCommand("""
            SELECT p.PromotionId,p.OfferName,p.OfferType,p.MinimumPurchaseAmount,p.DiscountType,p.DiscountValue,p.MaximumDiscount,
              p.StartsAt,p.EndsAt,p.IsActive,p.UsageLimitPerCustomer,p.PromoCode,
              (SELECT COUNT(1) FROM commerce.StorefrontPromotionUses u JOIN sales.SalesInvoices i ON i.InvoiceId=u.InvoiceId AND i.TenantId=u.TenantId
                OUTER APPLY(SELECT TOP(1) cp.Status FROM commerce.CommercePayments cp WHERE cp.TenantId=i.TenantId AND cp.InvoiceId=i.InvoiceId ORDER BY cp.AttemptNumber DESC) payment
                WHERE u.TenantId=@tenant AND u.PromotionId=p.PromotionId AND u.CustomerId=@customer AND i.Status NOT IN(N'CANCELLED',N'VOID') AND (i.Status=N'COMPLETED' OR payment.Status IS NULL OR payment.Status<>N'FAILED'))
            FROM commerce.StorefrontPromotions p WHERE p.TenantId=@tenant AND p.IsDeleted=0 AND (@promoCode IS NULL OR p.PromoCode=@promoCode);
            """, connection))
        {
            command.Parameters.AddWithValue("@tenant", tenantId);
            command.Parameters.AddWithValue("@customer", verifiedCustomerId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@promoCode", string.IsNullOrWhiteSpace(promotionCode) ? DBNull.Value : promotionCode.Trim());
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                offers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3),
                    reader.GetString(4), reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7), reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
                    reader.GetBoolean(9), reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.GetInt32(12), reader.IsDBNull(11) ? null : reader.GetString(11)));
        }
        var requestedCode = string.IsNullOrWhiteSpace(promotionCode) ? null : promotionCode.Trim();
        if (requestedCode is not null)
        {
            var selected = offers.FirstOrDefault();
            if (selected is null)
                throw new BusinessRuleException("Promo code is not valid.");
            var now = DateTimeOffset.UtcNow;
            if (!selected.IsActive || selected.EndsAt is not null && selected.EndsAt <= now)
                throw new BusinessRuleException("This promo code has expired.");
            if (selected.StartsAt is not null && selected.StartsAt > now)
                throw new BusinessRuleException("Promo code is not valid.");
            var merchandise = lines.Sum(x => x.Quantity * (StorefrontPricingPolicy.IsInclusive(x.GstPricingType)
                ? x.UnitPrice
                : x.UnitPrice * (1m + x.TaxPercentage / 100m)));
            if (selected.MinimumPurchaseAmount > merchandise)
            {
                var remaining = selected.MinimumPurchaseAmount - merchandise;
                throw new BusinessRuleException($"Add {remaining.ToString("C0", CultureInfo.GetCultureInfo("en-IN"))} more to use this promo code.");
            }
            if (selected.OfferType == "FIRST_ORDER" && (!verifiedCustomerId.HasValue || hasOrder))
                throw new BusinessRuleException("This promo code is not applicable to this order.");
            if (selected.UsageLimitPerCustomer is not null && (!verifiedCustomerId.HasValue || selected.PreviousUses >= selected.UsageLimitPerCustomer.Value))
                throw new BusinessRuleException("This promo code is no longer available.");
        }
        var quote = StorefrontPricingPolicy.Calculate(lines.Select(x => new StorefrontPricedItem(x.Quantity, x.UnitPrice, x.TaxPercentage, x.CompareAtPrice, x.GstPricingType)).ToArray(),
            settings, pincode, serviceable, offers, verifiedCustomerId is not null, hasOrder, DateTimeOffset.UtcNow, requestedCode);
        return quote with { FulfillmentWarnings = fulfillmentWarnings };
    }
    public async Task<StorefrontCheckoutResult> CheckoutAsync(string storeKey, StorefrontCheckoutInput input,
        string idempotencyKey, string? customerSessionToken, CancellationToken token)
    {
        var customerName = input.CustomerName?.Trim();
        var mobile = Digits().Replace(input.Mobile ?? string.Empty, string.Empty);
        var email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        var pincode = input.Pincode?.Trim();
        var recipientName = string.IsNullOrWhiteSpace(input.RecipientName) ? customerName : input.RecipientName.Trim();
        var line1 = input.AddressLine1?.Trim();
        var line2 = NullIfBlank(input.AddressLine2);
        var landmark = NullIfBlank(input.Landmark);
        var city = NullIfBlank(input.City);
        var district = NullIfBlank(input.District);
        var state = NullIfBlank(input.State);
        var country = string.IsNullOrWhiteSpace(input.Country) ? "India" : input.Country.Trim();
        var structuredAddress = !string.IsNullOrWhiteSpace(line1) || !string.IsNullOrWhiteSpace(city) || !string.IsNullOrWhiteSpace(state);
        var address = structuredAddress
            ? string.Join(", ", new[] { line1, line2, landmark, city, district, state, country }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : input.DeliveryAddress?.Trim();
        if (string.IsNullOrWhiteSpace(customerName) || customerName.Length > 250)
            throw new BusinessRuleException("Enter a valid customer name.");
        if (string.IsNullOrWhiteSpace(recipientName) || recipientName.Length > 250)
            throw new BusinessRuleException("Enter a valid recipient name.");
        if (mobile.Length is < 10 or > 15)
            throw new BusinessRuleException("Enter a valid mobile number.");
        if (email is not null && (!MailAddress.TryCreate(email, out _) || email.Length > 256))
            throw new BusinessRuleException("Enter a valid email address.");
        if (structuredAddress && (string.IsNullOrWhiteSpace(line1) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state)))
            throw new BusinessRuleException("Enter address line 1, city and state.");
        if (structuredAddress && (line1!.Length > 500 || line2?.Length > 500 || landmark?.Length > 250 || city!.Length > 150 || district?.Length > 150 || state!.Length > 100 || country.Length > 100))
            throw new BusinessRuleException("One or more delivery address fields are too long.");
        if (string.IsNullOrWhiteSpace(address) || address.Length > 1000)
            throw new BusinessRuleException("Enter a valid delivery address.");
        if (!Guid.TryParse(idempotencyKey, out var requestKey) || requestKey == Guid.Empty)
            throw new BusinessRuleException("A valid idempotency key is required.");

        var items = NormalizeItems(input.Items);

        var tenantId = await ResolveTenant(storeKey, token)
            ?? throw new EntityNotFoundException("Store was not found.");
        var marketplaceFeatureEnabled = await features.IsEnabledAsync(tenantId, FeatureKeys.MarketplaceCheckout, token);
        var containsPartnerProduct = await ContainsPartnerSellerItems(tenantId, items, token);
        if (!marketplaceFeatureEnabled && containsPartnerProduct)
            throw new BusinessRuleException("This partner-seller product is listed for discovery only. Online ordering will be enabled after seller, payment and invoicing arrangements are approved.");
        var method = NormalizePaymentMethod(input.PaymentMethod);
        var provider = method == "COD" ? PaymentProviders.Cod : PaymentProviders.Razorpay;
        var methods = await payments.GetEnabledMethodsForTenantAsync(tenantId, token);
        if (methods.All(enabled => enabled.Code != method))
            throw new BusinessRuleException("The selected payment method is not currently available for this store.");

        var cartJson = JsonSerializer.Serialize(items);
        var (warehouseId, lines, _) = await PriceCart(tenantId, cartJson, items.Length, !marketplaceFeatureEnabled, token,
            checkCommonWarehouse: true);
        var useMarketplaceOrder = marketplaceFeatureEnabled && (containsPartnerProduct || warehouseId is null);
        var authenticated = string.IsNullOrWhiteSpace(customerSessionToken) ? null : await customers.GetSessionAsync(storeKey, customerSessionToken, token);
        if (await blacklist.IsBlockedAsync(tenantId, authenticated?.Mobile ?? mobile, token))
            throw new BusinessRuleException("Orders are temporarily unavailable for this phone number. You may continue to browse and contact the store for assistance.");
        var quote = await CalculateQuote(tenantId, lines, pincode, authenticated?.Id, input.PromoCode, [], token);
        // Revalidate immediately before creating the invoice so a booking window
        // closing during checkout cannot be bypassed by a stale product response.
        await availability.EnsureOrderEligibleAsync(tenantId, items.Select(x => x.ProductId).ToArray(), token);
        if (!quote.IsDeliveryEnabled)
            throw new BusinessRuleException("Delivery is not currently available for this store.");
        if (!quote.IsPincodeServiceable)
            throw new BusinessRuleException("Sorry, delivery is not available at this pincode yet.");
        if (input.ExpectedPayableAmount is null)
            throw new BusinessRuleException("Refresh the order total before checkout.");
        StorefrontPricingPolicy.EnsureExpectedPayable(input.ExpectedPayableAmount, quote.FinalPayableAmount);
        var customerId = authenticated?.Id ?? await FindOrCreateCustomer(tenantId, customerName, mobile, email, token);
        var idempotencyRequest = new CheckoutIdempotencyRequest(tenantId, customerId, customerName, recipientName,
            mobile, email, address, pincode, line1, line2, landmark, city, district, state, country,
            items.OrderBy(item => item.ProductId).ToArray(),
            string.IsNullOrWhiteSpace(input.PromoCode) ? null : input.PromoCode.Trim().ToUpperInvariant(),
            quote.FinalPayableAmount, method);
        var idempotencyFingerprint = JsonSerializer.Serialize(idempotencyRequest);
        var catalogueItems = lines.Select(line => new POSItemInput(line.ProductId, null, line.Quantity,
            line.UnitPrice, 0, 0, line.TaxPercentage)).ToArray();
        var posItems = lines.Select(line => new POSItemInput(line.ProductId, null, line.Quantity,
            useMarketplaceOrder ? StorefrontPricingPolicy.TaxExclusiveUnitPrice(line.UnitPrice, line.TaxPercentage, line.GstPricingType) : line.UnitPrice,
            0, 0, line.TaxPercentage)).ToArray();
        var orderRequest = new POSPostRequest(null, null, customerId, warehouseId ?? Guid.Empty, null,
            JsonSerializer.Serialize(posItems), "[]", quote.PromotionDiscount, 0, "Storefront order awaiting payment/collection",
            "HELD", false, null, "STOREFRONT", tenantId, "STOREFRONT",
            DeliveryCharge: quote.DeliveryCharge, PromotionDiscountAmount: quote.PromotionDiscount,
            AppliedPromotionId: quote.PromotionId, AppliedPromotionName: quote.PromotionName,
            FreeDeliveryApplied: quote.IsFreeDeliveryUnlocked, FreeDeliveryThresholdSnapshot: quote.FreeDeliveryThreshold,
            ServicePincode: pincode);
        Guid orderId; string orderNumber; IReadOnlyCollection<MarketplaceFulfilmentAllocationGroup> fulfilmentGroups = [];
        if (useMarketplaceOrder)
        {
            var allocated = await marketplaceAllocation.CreateHeldOrderAsync(tenantId,
                $"STOREFRONT:{tenantId:N}:{requestKey:N}",
                new(orderRequest, address, pincode ?? string.Empty, null, DateTimeOffset.UtcNow.AddMinutes(20),
                    JsonSerializer.Serialize(catalogueItems), quote.FinalPayableAmount, idempotencyFingerprint), token);
            orderId = allocated.OrderId; orderNumber = allocated.OrderNumber;
            fulfilmentGroups = allocated.FulfilmentGroups;
        }
        else
        {
            var order = await pos.PostForTenant(orderRequest, tenantId, $"STOREFRONT:{tenantId:N}:{requestKey:N}", token,
                idempotencyRequest);
            orderId = order.InvoiceId; orderNumber = order.InvoiceNumber;
        }

        await UpdateOrderMetadata(tenantId, orderId, address, method, recipientName, mobile, line1, line2, landmark, city, district, state, country, token);
        var existing = await ExistingPayment(tenantId, orderId, provider, method, token);
        if (existing is not null)
        {
            await QueueOrderNotificationSafely(tenantId, orderId, token);
            return new(orderId, orderNumber, existing.Value.Amount, "INR", existing.Value.PaymentId,
                provider == PaymentProviders.Cod ? null : existing.Value.Url, provider, existing.Value.Status, CustomerMessage(method, fulfilmentGroups.Count > 1), null, method, fulfilmentGroups.Count > 1);
        }

        var attempt = await payments.CreateAttemptForTenantAsync(tenantId,
            new(orderId, provider, method, provider == PaymentProviders.Razorpay ? PaymentReturnUrl(storeKey, orderId) : null), "STOREFRONT", token);
        var checkoutUrl = attempt.PaymentAction ?? attempt.Payment.PaymentLink;
        if (provider == PaymentProviders.Cod) checkoutUrl = null;
        if (provider == PaymentProviders.Razorpay && (string.IsNullOrWhiteSpace(checkoutUrl)
            || !Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var checkoutUri)
            || checkoutUri.Scheme != Uri.UriSchemeHttps))
        {
            StorefrontCheckoutLogs.InvalidPaymentAction(logger, tenantId, orderId, provider);
            throw new BusinessRuleException("Online payment is currently unavailable. Please try again.");
        }
        await QueueOrderNotificationSafely(tenantId, orderId, token);
        return new(orderId, orderNumber, attempt.Payment.Amount,
            attempt.Payment.Currency, attempt.Payment.PaymentId, checkoutUrl, provider, attempt.Payment.Status, CustomerMessage(method, fulfilmentGroups.Count > 1), null, method, fulfilmentGroups.Count > 1);
    }

    public async Task<StorefrontPaymentReturnDto?> GetPaymentStatusAsync(string storeKey, Guid orderId,
        string? customerSessionToken, CancellationToken token)
    {
        var session = string.IsNullOrWhiteSpace(customerSessionToken)
            ? null
            : await customers.GetSessionAsync(storeKey, customerSessionToken, token);
        if (session is null) return null;
        var tenantId = await ResolveTenant(storeKey, token);
        if (tenantId is null) return null;
        await using var connection = await Open(tenantId.Value, token);
        await using var command = new SqlCommand(@"SELECT TOP(1) i.InvoiceNumber,p.PaymentId,p.Status
FROM sales.SalesInvoices i
JOIN commerce.CommercePayments p ON p.InvoiceId=i.InvoiceId AND p.TenantId=i.TenantId
WHERE i.TenantId=@tenant AND i.InvoiceId=@order AND i.CustomerId=@customer AND p.Provider=N'RAZORPAY'
ORDER BY p.AttemptNumber DESC;", connection);
        command.Parameters.AddWithValue("@tenant", tenantId.Value);
        command.Parameters.AddWithValue("@order", orderId);
        command.Parameters.AddWithValue("@customer", session.Id);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var status = reader.GetString(2).Trim().ToUpperInvariant();
        var message = status switch
        {
            CommercePaymentStatuses.Paid => "Payment verified successfully.",
            CommercePaymentStatuses.Failed or CommercePaymentStatuses.Cancelled => "Payment was not completed. You can retry payment for this existing order.",
            _ => "Payment is still being confirmed. We will update this order when Razorpay confirms it."
        };
        return new(orderId, reader.GetString(0), reader.GetGuid(1), status, message);
    }

    private string? PaymentReturnUrl(string storeKey, Guid orderId)
    {
        var customMapping = configuration.GetSection("StorefrontDomains:Mappings").GetChildren()
            .Where(item => bool.TryParse(item["Verified"], out var verified) && verified
                && string.Equals(item["StoreKey"]?.Trim(), storeKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => bool.TryParse(item["Canonical"], out var canonical) && canonical)
            .FirstOrDefault();
        var customOrigin = customMapping?["Origin"];
        if (Uri.TryCreate(customOrigin, UriKind.Absolute, out var customRoot)
            && customRoot.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(customRoot.UserInfo) && customRoot.AbsolutePath == "/"
            && string.IsNullOrEmpty(customRoot.Query) && string.IsNullOrEmpty(customRoot.Fragment))
            return new Uri(customRoot, $"payment-return?orderId={Uri.EscapeDataString(orderId.ToString("D"))}").ToString();
        var configured = configuration["Storefront:PublicBaseUrl"] ?? configuration["Storefront:BaseUrl"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var root) || root.Scheme != Uri.UriSchemeHttps && root.Scheme != Uri.UriSchemeHttp)
            return null;
        return new Uri(root, $"{Uri.EscapeDataString(storeKey)}/payment-return?orderId={Uri.EscapeDataString(orderId.ToString("D"))}").ToString();
    }

    public async Task<StorefrontCheckoutResult?> RetryPaymentAsync(string storeKey, Guid orderId,
        string? customerSessionToken, CancellationToken token)
    {
        var session = string.IsNullOrWhiteSpace(customerSessionToken) ? null : await customers.GetSessionAsync(storeKey, customerSessionToken, token);
        if (session is null) return null;
        var tenantId = await ResolveTenant(storeKey, token);
        if (tenantId is null) return null;
        string number, name, mobile, method, status; decimal balance;
        await using (var connection = await Open(tenantId.Value, token))
        await using (var command = new SqlCommand(@"SELECT i.InvoiceNumber,cu.CustomerName,cu.Mobile,i.BalanceAmount,i.Status,
COALESCE((SELECT TOP(1) p.PaymentMethod FROM commerce.CommercePayments p WHERE p.TenantId=i.TenantId AND p.InvoiceId=i.InvoiceId ORDER BY p.AttemptNumber DESC),N'UPI')
FROM sales.SalesInvoices i JOIN sales.Customers cu ON cu.CustomerId=i.CustomerId AND cu.TenantId=i.TenantId
WHERE i.TenantId=@tenant AND i.InvoiceId=@order AND i.CustomerId=@customer;", connection))
        {
            command.Parameters.AddWithValue("@tenant", tenantId.Value); command.Parameters.AddWithValue("@order", orderId);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return null;
            number = reader.GetString(0); name = reader.GetString(1); mobile = reader.GetString(2); balance = reader.GetDecimal(3); status = reader.GetString(4);
            method = reader.GetString(5);
        }
        if (status is "CANCELLED" or "VOID" or "RETURNED" || balance <= 0) throw new BusinessRuleException("This order cannot receive another payment.");
        method = method is "UPI" or "NET_BANKING" ? method : "UPI";
        var existing = await ExistingPayment(tenantId.Value, orderId, PaymentProviders.Razorpay, method, token);
        if (existing is not null)
            return new(orderId, number, existing.Value.Amount, "INR", existing.Value.PaymentId, existing.Value.Url, PaymentProviders.Razorpay, existing.Value.Status, CustomerMessage(method), null, method);
        var attempt = await payments.CreateAttemptForTenantAsync(tenantId.Value,
            new(orderId, PaymentProviders.Razorpay, method, PaymentReturnUrl(storeKey, orderId)), "STOREFRONT", token);
        return new(orderId, number, attempt.Payment.Amount, attempt.Payment.Currency, attempt.Payment.PaymentId,
            attempt.PaymentAction ?? attempt.Payment.PaymentLink, PaymentProviders.Razorpay, attempt.Payment.Status, CustomerMessage(method), null, method);
    }

    private async Task QueueOrderNotificationSafely(Guid tenantId, Guid orderId, CancellationToken token)
    {
        try { await automatedNotifications.QueueOrderEmailAsync(tenantId, orderId, token); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { StorefrontCheckoutLogs.NotificationQueueFailed(logger, tenantId, orderId, ex); }
    }

    private async Task<Guid?> ResolveTenant(string storeKey, CancellationToken token)
    {
        var normalized = storeKey?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !StoreKeyPattern().IsMatch(normalized)) return null;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1;", connection);
        command.Parameters.AddWithValue("@key", normalized);
        return await command.ExecuteScalarAsync(token) is Guid tenant ? tenant : null;
    }

    private async Task<(Guid? WarehouseId, IReadOnlyCollection<PricedLine> Lines, IReadOnlyCollection<StorefrontCartFulfillmentWarning> Warnings)> PriceCart(
        Guid tenantId, string cartJson, int expectedItems, bool requireFulfillment, CancellationToken token, bool checkCommonWarehouse = true)
    {
        await using var connection = await Open(tenantId, token);
        Guid? warehouseId = null;
        if (requireFulfillment)
        {
        await using var warehouse = new SqlCommand("""
            SELECT TOP(1) w.WarehouseId
            FROM inventory.Warehouses w
            WHERE w.TenantId=@tenant AND w.IsActive=1 AND w.IsDeleted=0
              AND NOT EXISTS (
                SELECT 1 FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,Quantity decimal(18,4)) j
                WHERE NOT EXISTS (
                  SELECT 1 FROM inventory.InventoryBalances b
                  WHERE b.TenantId=@tenant AND b.WarehouseId=w.WarehouseId AND b.ProductId=j.ProductId
                  GROUP BY b.ProductId HAVING SUM(b.QuantityOnHand-b.QuantityReserved)>=j.Quantity))
            ORDER BY w.IsDefault DESC,w.WarehouseName;
            """, connection);
        warehouse.Parameters.AddWithValue("@tenant", tenantId);
        warehouse.Parameters.AddWithValue("@items", cartJson);
        if (await warehouse.ExecuteScalarAsync(token) is not Guid selectedWarehouse)
            throw new BusinessRuleException("The cart items are not available together at this store.");
        warehouseId = selectedWarehouse;
        }

        await using var products = new SqlCommand("""
            SELECT p.ProductId,j.Quantity,p.SellingPrice,p.GSTPercentage,p.MRP,p.GstPricingType,p.ProductCode,p.ProductName,u.UnitName,u.ShortName
            FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,Quantity decimal(18,4)) j
            JOIN master.Products p ON p.ProductId=j.ProductId AND p.TenantId=@tenant
            JOIN master.ProductCategories c ON c.ProductCategoryId=p.CategoryId AND c.IsActive=1 AND c.IsDeleted=0
            JOIN master.UnitsOfMeasure u ON u.UnitId=p.UnitId AND u.IsActive=1 AND u.IsDeleted=0
            WHERE p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1
              AND commerce.IsProductPubliclyApproved(@tenant,p.ProductId)=1
              AND EXISTS(SELECT 1 FROM master.TenantProductCategories x WHERE x.TenantId=@tenant AND x.ProductCategoryId=p.CategoryId)
              AND EXISTS(SELECT 1 FROM master.TenantUnitsOfMeasure x WHERE x.TenantId=@tenant AND x.UnitId=p.UnitId);
            """, connection);
        products.Parameters.AddWithValue("@tenant", tenantId);
        products.Parameters.AddWithValue("@items", cartJson);
        var lines = new List<PricedLine>();
        await using var reader = await products.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            lines.Add(new(reader.GetGuid(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.IsDBNull(5) ? "EXCLUSIVE" : reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        await reader.DisposeAsync();
        if (lines.Count != expectedItems)
            throw new BusinessRuleException("One or more cart items are no longer available.");
        var warnings = new List<StorefrontCartFulfillmentWarning>();
        if (!requireFulfillment && checkCommonWarehouse)
        {
            await using var warehouse = new SqlCommand("""
                SELECT TOP(1) w.WarehouseId
                FROM inventory.Warehouses w
                WHERE w.TenantId=@tenant AND w.IsActive=1 AND w.IsDeleted=0
                  AND NOT EXISTS (
                    SELECT 1 FROM OPENJSON(@items) WITH(ProductId uniqueidentifier,Quantity decimal(18,4)) j
                    WHERE NOT EXISTS (
                      SELECT 1 FROM inventory.InventoryBalances b
                      WHERE b.TenantId=@tenant AND b.WarehouseId=w.WarehouseId AND b.ProductId=j.ProductId
                      GROUP BY b.ProductId HAVING SUM(b.QuantityOnHand-b.QuantityReserved)>=j.Quantity))
                ORDER BY w.IsDefault DESC,w.WarehouseName;
                """, connection);
            warehouse.Parameters.AddWithValue("@tenant", tenantId);
            warehouse.Parameters.AddWithValue("@items", cartJson);
            if (await warehouse.ExecuteScalarAsync(token) is Guid selectedWarehouse)
                warehouseId = selectedWarehouse;
            else
                warnings.Add(new("NO_COMMON_FULFILLMENT_WAREHOUSE", "These items can be priced, but they are not currently available together from one fulfillment warehouse."));
        }
        return (warehouseId, lines, warnings);
    }

    private async Task<Guid> FindOrCreateCustomer(Guid tenantId, string name, string mobile, string? email,
        CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        await using var find = new SqlCommand("""
            SELECT TOP(1) CustomerId FROM sales.Customers WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0
              AND REPLACE(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),N'(',N'') LIKE N'%'+@mobile
            ORDER BY CreatedOn;
            """, connection, transaction);
        find.Parameters.AddWithValue("@tenant", tenantId);
        find.Parameters.AddWithValue("@mobile", mobile);
        var existing = await find.ExecuteScalarAsync(token);
        if (existing is Guid customerId)
        {
            await transaction.CommitAsync(token);
            return customerId;
        }

        var id = Guid.NewGuid();
        await using var insert = new SqlCommand("""
            INSERT sales.Customers(CustomerId,TenantId,CustomerCode,CustomerName,CustomerType,Email,Mobile,
              Currency,CreditLimit,OpeningBalance,IsGSTRegistered,IsActive,IsDeleted,Remarks,CreatedBy)
            VALUES(@id,@tenant,@code,@name,N'RETAIL',@email,@mobile,N'INR',0,0,0,1,0,N'Created by customer storefront',N'STOREFRONT');
            """, connection, transaction);
        insert.Parameters.AddWithValue("@id", id);
        insert.Parameters.AddWithValue("@tenant", tenantId);
        insert.Parameters.AddWithValue("@code", $"WEB-{id:N}"[..16]);
        insert.Parameters.AddWithValue("@name", name);
        insert.Parameters.AddWithValue("@email", email ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("@mobile", mobile);
        await insert.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return id;
    }

    private async Task UpdateOrderMetadata(Guid tenantId, Guid orderId, string address, string method, string recipientName,
        string mobile, string? line1, string? line2, string? landmark, string? city, string? district, string? state,
        string country, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("""
            UPDATE integration.WhatsAppCommerceOrders
            SET DeliveryAddress=@address,DeliveryRecipientName=@recipientName,DeliveryMobile=@mobile,
                DeliveryAddressLine1=@line1,DeliveryAddressLine2=@line2,DeliveryLandmark=@landmark,
                DeliveryCity=@city,DeliveryDistrict=@district,DeliveryState=@state,DeliveryCountry=@country,
                FulfillmentMethod=N'RETAILER_DELIVERY',PaymentType=@paymentType
            WHERE TenantId=@tenant AND InvoiceId=@order;
            """, connection);
        command.Parameters.AddWithValue("@address", address);
        command.Parameters.AddWithValue("@recipientName", recipientName);
        command.Parameters.AddWithValue("@mobile", mobile);
        command.Parameters.AddWithValue("@line1", (object?)line1 ?? DBNull.Value);
        command.Parameters.AddWithValue("@line2", (object?)line2 ?? DBNull.Value);
        command.Parameters.AddWithValue("@landmark", (object?)landmark ?? DBNull.Value);
        command.Parameters.AddWithValue("@city", (object?)city ?? DBNull.Value);
        command.Parameters.AddWithValue("@district", (object?)district ?? DBNull.Value);
        command.Parameters.AddWithValue("@state", (object?)state ?? DBNull.Value);
        command.Parameters.AddWithValue("@country", (object?)country ?? DBNull.Value);
        command.Parameters.AddWithValue("@paymentType", method);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@order", orderId);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<(Guid PaymentId, decimal Amount, string Status, string? Url)?> ExistingPayment(Guid tenantId, Guid orderId, string provider, string method, CancellationToken token)
    {
        await using var connection = await Open(tenantId, token);
        await using var command = new SqlCommand("""
            SELECT TOP(1) PaymentId,Amount,Status,COALESCE(PaymentLink,ProviderReference) FROM commerce.CommercePayments
            WHERE TenantId=@tenant AND InvoiceId=@order AND Provider=@provider AND PaymentMethod=@method
              AND Status IN(N'PENDING',N'PENDING_VERIFICATION',N'COD_PENDING')
            ORDER BY AttemptNumber DESC;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@order", orderId);
        command.Parameters.AddWithValue("@provider", provider); command.Parameters.AddWithValue("@method", method);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetGuid(0), reader.GetDecimal(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)) : null;
    }    private async Task<SqlConnection> Open(Guid tenantId, CancellationToken token)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", connection);
        context.Parameters.AddWithValue("@tenant", tenantId);
        await context.ExecuteNonQueryAsync(token);
        return connection;
    }

    private sealed record PricedLine(Guid ProductId, decimal Quantity, decimal UnitPrice, decimal TaxPercentage,
        decimal CompareAtPrice, string GstPricingType, string ProductCode, string ProductName, string UnitName, string? UnitShortName);
    private static string NormalizePaymentMethod(string? method)
    {
        var value = method?.Trim().ToUpperInvariant();
        if (value is "COD" or "UPI" or "NET_BANKING") return value;
        throw new BusinessRuleException("Select a supported payment method.");
    }
    private static string CustomerMessage(string method, bool mayArriveSeparately = false)
    {
        var message = method == "COD"
            ? "Your order is confirmed for cash payment on delivery."
            : "Payment is processing. The order will update when Razorpay confirms it.";
        return mayArriveSeparately ? $"{message} Your order contains items that may arrive in separate deliveries." : message;
    }
    [GeneratedRegex("^[A-Z0-9_-]{1,100}$", RegexOptions.CultureInvariant)] private static partial Regex StoreKeyPattern();
    [GeneratedRegex("[^0-9]+", RegexOptions.CultureInvariant)] private static partial Regex Digits();
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    [GeneratedRegex("^[0-9]{6}$", RegexOptions.CultureInvariant)] private static partial Regex PincodePattern();
}

internal static partial class StorefrontCheckoutLogs
{
    [LoggerMessage(3401, LogLevel.Warning, "Storefront payment provider {Provider} returned an invalid action for tenant {TenantId}, order {OrderId}.")]
    public static partial void InvalidPaymentAction(ILogger logger, Guid tenantId, Guid orderId, string provider);
    [LoggerMessage(3402, LogLevel.Error, "Storefront order {OrderId} for tenant {TenantId} was committed but notification queueing failed.")]
    public static partial void NotificationQueueFailed(ILogger logger, Guid tenantId, Guid orderId, Exception exception);
}
