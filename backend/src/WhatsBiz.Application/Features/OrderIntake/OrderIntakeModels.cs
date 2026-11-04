namespace WhatsBiz.Application.Features.OrderIntake;

public static class OrderIntakeChannels
{
    public const string WhatsApp = "WHATSAPP";
    public const string PhoneCall = "PHONE_CALL";
    public const string ManualList = "MANUAL_LIST";
    public static readonly IReadOnlySet<string> V1 = new HashSet<string>([WhatsApp, PhoneCall, ManualList], StringComparer.Ordinal);
}

public static class OrderIntakeStatuses
{
    public const string New = "NEW";
    public const string UnderReview = "UNDER_REVIEW";
    public const string AwaitingCustomer = "AWAITING_CUSTOMER";
    public const string Confirmed = "CONFIRMED";
    public const string Converted = "CONVERTED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [New, UnderReview, AwaitingCustomer, Confirmed, Converted, Rejected, Cancelled], StringComparer.Ordinal);
}

public sealed record OrderIntakeCreateInput(string SourceChannel, string? CustomerName, string? CustomerMobile,
    string OriginalRequest, string? Notes = null, string? FulfilmentPreference = null, DateTimeOffset? RequestedAtUtc = null,
    OrderIntakeDeliveryAddressInput? DeliveryAddress = null);
public sealed record OrderIntakePage(IReadOnlyCollection<OrderIntakeSummary> Items, int TotalCount, int Page, int PageSize);
public sealed record OrderIntakeSummary(Guid Id, string Reference, string SourceChannel, string Status,
    string? CustomerName, string? CustomerMobile, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastInteractionAtUtc,
    Guid? AssignedStaffId, string? AssignedStaffName, int ItemCount, int UnmatchedItemCount);
public sealed record OrderIntakeDetail(Guid Id, string Reference, string SourceChannel, string Status,
    string? CustomerName, string? CustomerMobile, string OriginalRequest, string? Notes, string? FulfilmentPreference,
    DateTimeOffset? RequestedAtUtc, string? ProviderMessageId, string? ConversationReference,
    DateTimeOffset? LastInteractionAtUtc, Guid? AssignedStaffId, string? AssignedStaffName,
    string? ConfirmationChannel, DateTimeOffset? CustomerConfirmedAtUtc, DateTimeOffset CreatedAtUtc,
    byte[] Version, IReadOnlyCollection<OrderIntakeItem> Items, IReadOnlyCollection<OrderIntakeAudit> History,
    OrderIntakeDeliveryAddressSnapshot? DeliveryAddress = null);
public sealed record OrderIntakeItem(Guid Id, string RequestedText, decimal? RequestedQuantity, string? RequestedUnit,
    Guid? MatchedProductId, string MatchStatus, string? ProductCode, string? ProductName, decimal? CurrentSellingPrice,
    decimal? CurrentStock, string? Notes, bool CustomerApprovedSubstitution, bool ProposedSubstitution = false);
public sealed record OrderIntakeAudit(string EventType, string? PreviousStatus, string? NewStatus, string? Actor,
    string? Note, DateTimeOffset CreatedAtUtc);
public sealed record OrderIntakeProductMatch(Guid ProductId, string ProductCode, string ProductName,
    decimal SellingPrice, decimal CurrentStock);
public sealed record OrderIntakeItemUpdateInput(Guid? MatchedProductId, decimal? Quantity, string? Unit,
    string? Notes, bool CustomerApprovedSubstitution = false, byte[]? Version = null, string? MatchStatus = null,
    bool RetailerConfirmedMatch = false, bool ProposedSubstitution = false);
public sealed record OrderIntakeStatusInput(string Status, string? Note = null, string? ConfirmationChannel = null, byte[]? Version = null);
public sealed record OrderIntakeQuoteInput(string? PromotionCode = null, int ValidityMinutes = 30, byte[]? Version = null);
public sealed record OrderIntakeDeliveryAddressInput(string FulfilmentPreference, string? RecipientName = null,
    string? Mobile = null, string? HouseStreet = null, string? Locality = null, string? City = null,
    string? State = null, string? Pincode = null, string? Landmark = null, string? DeliveryInstructions = null,
    Guid? SavedAddressId = null);
public sealed record OrderIntakeDeliveryAddressSnapshot(string RecipientName, string Mobile, string HouseStreet,
    string Locality, string City, string State, string Pincode, string? Landmark, string? DeliveryInstructions,
    Guid? SavedAddressId = null);
public sealed record OrderIntakeDeliveryDetailsInput(OrderIntakeDeliveryAddressInput Details, byte[]? Version = null);
public sealed record OrderIntakeCustomerLinkInput(Guid CustomerId, string VerificationMethod, bool StaffAcknowledged, byte[]? Version = null);
public sealed record OrderIntakeCustomerOption(Guid CustomerId, string CustomerCode, string CustomerName);
public sealed record OrderIntakeSavedAddress(Guid AddressId, string RecipientName, string Mobile, string AddressLine1,
    string? AddressLine2, string? Landmark, string City, string State, string Pincode, string AddressType, bool IsDefault);

public static class OrderIntakeDeliveryAddressPolicy
{
    public static string NormalizeFulfillment(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "PICKUP" or "STORE_PICKUP" => "PICKUP",
        "DELIVERY" or "HOME_DELIVERY" => "DELIVERY",
        _ => throw new ArgumentException("Choose Home Delivery or Store Pickup.")
    };

    public static OrderIntakeDeliveryAddressSnapshot Normalize(OrderIntakeDeliveryAddressInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        static string Required(string? value, int max, string label)
        {
            var result = value?.Trim();
            if (string.IsNullOrWhiteSpace(result)) throw new ArgumentException($"{label} is required for home delivery.");
            if (result.Length > max) throw new ArgumentException($"{label} must be {max} characters or fewer.");
            return result;
        }
        static string? Optional(string? value, int max, string label)
        {
            var result = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (result?.Length > max) throw new ArgumentException($"{label} must be {max} characters or fewer.");
            return result;
        }
        var mobile = Required(input.Mobile, 30, "Mobile number");
        var mobileDigits = mobile.Count(c => c >= '0' && c <= '9');
        var mobileCharactersValid = mobile.All(c => (c >= '0' && c <= '9') || c == '+' || c == '(' || c == ')' || c == '-' || c == ' ');
        if (mobileDigits < 7 || !mobileCharactersValid)
            throw new ArgumentException("Enter a valid customer mobile number.");
        var pincode = input.Pincode?.Trim();
        if (pincode is null || pincode.Length != 6 || pincode.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("Enter a valid six-digit delivery PIN code.");
        return new(Required(input.RecipientName, 200, "Recipient name"), mobile,
            Required(input.HouseStreet, 300, "House / street"), Required(input.Locality, 160, "Locality / village"),
            Required(input.City, 120, "City"), Required(input.State, 120, "State"), pincode,
            Optional(input.Landmark, 200, "Landmark"), Optional(input.DeliveryInstructions, 500, "Delivery instructions"), input.SavedAddressId);
    }
}
public sealed record OrderIntakeConfirmationRequestInput(int QuoteRevision, string Channel, byte[]? Version = null);
public sealed record OrderIntakeQuoteDto(Guid Id, int Revision, string Status, string FulfilmentPreference,
    string? Pincode, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc,
    WhatsBiz.Application.Features.Storefront.StorefrontCartQuoteDto Pricing,
    IReadOnlyCollection<OrderIntakeQuotedItem> Items, OrderIntakeDeliveryAddressSnapshot? DeliveryAddress = null);
public sealed record OrderIntakeQuotedItem(Guid ProductId, string ProductCode, string ProductName, string RequestedText,
    decimal Quantity, string? RequestedUnit, string UnitName, string? UnitShortName, decimal UnitPrice,
    decimal TaxPercentage, decimal CompareAtPrice, string GstPricingType, bool IsSubstitution);
public sealed record OrderIntakeConfirmationInput(int QuoteRevision, string Channel, string VerificationMethod,
    string? EvidenceReference, bool StaffAcknowledged, byte[]? Version = null);
public sealed record OrderIntakeConversionInput(byte[]? Version = null);
public sealed record OrderIntakeSalesOrderStatusInput(string Status, byte[]? Version = null);
public sealed record IntakeSalesOrderDto(Guid Id, string Reference, Guid RequestId, Guid QuoteId, int QuoteRevision,
    string Status, string FulfilmentPreference, decimal PayableAmount, DateTimeOffset CreatedAtUtc, byte[] Version,
    IReadOnlyCollection<OrderIntakeAudit> History, OrderIntakeDeliveryAddressSnapshot? DeliveryAddress = null);

public interface IOrderIntakeService
{
    Task<OrderIntakePage> ListAsync(Guid tenantId, string? search, string? channel, string? status,
        DateTimeOffset? from, DateTimeOffset? through, int page, int pageSize, bool revealMobile, CancellationToken token);
    Task<OrderIntakeDetail> GetAsync(Guid tenantId, Guid requestId, bool revealMobile, CancellationToken token);
    Task<OrderIntakeDetail> CreateAsync(Guid tenantId, OrderIntakeCreateInput input, string? actor, CancellationToken token);
    Task<OrderIntakeDetail> AssignToSelfAsync(Guid tenantId, Guid requestId, Guid staffId, byte[]? version, string? actor, CancellationToken token);
    Task<IReadOnlyCollection<OrderIntakeProductMatch>> FindMatchesAsync(Guid tenantId, string query, CancellationToken token);
    Task<OrderIntakeDetail> UpdateItemAsync(Guid tenantId, Guid requestId, Guid itemId, OrderIntakeItemUpdateInput input, string? actor, bool revealMobile, CancellationToken token);
    Task<OrderIntakeDetail> UpdateStatusAsync(Guid tenantId, Guid requestId, OrderIntakeStatusInput input, string? actor, bool revealMobile, CancellationToken token);
    Task<OrderIntakeDetail> SaveDeliveryDetailsAsync(Guid tenantId, Guid requestId, OrderIntakeDeliveryDetailsInput input, string? actor, bool revealCustomerData, CancellationToken token);
    Task<OrderIntakeDetail> LinkVerifiedCustomerAsync(Guid tenantId, Guid requestId, OrderIntakeCustomerLinkInput input, string? actor, bool revealCustomerData, CancellationToken token);
    Task<IReadOnlyCollection<OrderIntakeCustomerOption>> SearchCustomersAsync(Guid tenantId, string query, CancellationToken token);
    Task<IReadOnlyCollection<OrderIntakeSavedAddress>> GetLinkedCustomerAddressesAsync(Guid tenantId, Guid requestId, CancellationToken token);
    Task<OrderIntakeQuoteDto> CreateQuoteAsync(Guid tenantId, Guid requestId, OrderIntakeQuoteInput input, string? actor, bool revealCustomerContext, CancellationToken token);
    Task<OrderIntakeQuoteDto?> GetCurrentQuoteAsync(Guid tenantId, Guid requestId, bool revealCustomerContext, CancellationToken token);
    Task<OrderIntakeDetail> MarkConfirmationRequestedAsync(Guid tenantId, Guid requestId, OrderIntakeConfirmationRequestInput input, string? actor, bool revealMobile, CancellationToken token);
    Task<OrderIntakeDetail> RecordConfirmationAsync(Guid tenantId, Guid requestId, OrderIntakeConfirmationInput input, string? actor, bool revealMobile, CancellationToken token);
    Task<IntakeSalesOrderDto> ConvertToSalesOrderAsync(Guid tenantId, Guid requestId, OrderIntakeConversionInput input, string? actor, CancellationToken token);
    Task<IntakeSalesOrderDto?> GetSalesOrderAsync(Guid tenantId, Guid requestId, bool revealCustomerData, CancellationToken token);
    Task<IntakeSalesOrderDto> TransitionSalesOrderAsync(Guid tenantId, Guid requestId, string status, byte[]? version, string? actor, CancellationToken token);
    Task<bool> CreateWhatsAppAsync(Guid tenantId, string phoneNumberId, string sender, string messageId,
        string? profileName, string text, CancellationToken token);
}

public interface IWhatsAppOrderIntakeHandler
{
    Task<bool> TryCaptureAsync(Guid tenantId, string phoneNumberId, string sender, string messageId,
        string? profileName, string? text, CancellationToken token);
}
