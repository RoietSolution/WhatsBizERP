using WhatsBiz.Application.Features.Loyalty;
using WhatsBiz.Application.Features.Referrals;

namespace WhatsBiz.Application.Features.Storefront;

public sealed record StorefrontBannerDto(string Slot, string ImageUrl, string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder, Guid? PromotionId = null);
public sealed record StorefrontPaymentMethodDto(string Code, string Label, string Description, bool IsDefault, bool UsesHostedPaymentPage, string Provider);
public sealed record StorefrontStoreDto(string StoreKey, string Name, string Tagline, string? LogoUrl, string AccentColor,
    string DeliveryMessage, decimal? FreeDeliveryThreshold, bool ShowProductRatings, bool ShowProductReviews, IReadOnlyCollection<StorefrontBannerDto> Banners, IReadOnlyCollection<StorefrontPaymentMethodDto> PaymentMethods, string? AllCategoryImageUrl = null, bool CustomerRewardsEnabled = false, string? DisplayName = null);
public sealed record StorefrontCategoryDto(Guid Id, string Name, string? ImageUrl);
public sealed record StorefrontProductDto(Guid Id, Guid CategoryId, string Name, string Description, string? LongDescription, IReadOnlyCollection<StorefrontProductSpecificationDto> Specifications, string? ImageUrl, decimal SellingPrice, decimal? CompareAtPrice, string Availability, string? PackSize, decimal? AverageRating, int RatingCount, string ReturnPolicyMode = "INHERIT_DEFAULT", int? ReturnWindowDays = null, bool IsReturnable = true, IReadOnlyCollection<StorefrontProductImageDto>? Images = null, string? ReturnPolicyDescription = null);
public sealed record StorefrontProductImageDto(Guid Id, string Url, string ThumbnailUrl, bool IsPrimary);
public sealed record StorefrontProductSpecificationDto(string Name, string Value);
public sealed record StorefrontProductPageDto(IReadOnlyCollection<StorefrontProductDto> Items, int PageNumber, int PageSize, int TotalCount, bool HasMore);
public sealed record StorefrontOfferDto(Guid OfferId, string Title, string? BannerImageUrl, string? ShortDescription, string? DetailedDescription, string? PromoCode, string BenefitDescription, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, decimal MinimumOrderAmount, string? EligibleItemsDescription, decimal? MaximumDiscount, int? UsageLimitPerCustomer, string? TermsAndConditions, string? CtaLabel, string Status);
public sealed record StorefrontImage(string ContentType, byte[] Content);
public sealed record StorefrontCheckoutItem(Guid ProductId, decimal Quantity);
public sealed record StorefrontCheckoutInput(string CustomerName, string Mobile, string? Email, string Pincode,
    string DeliveryAddress, IReadOnlyCollection<StorefrontCheckoutItem> Items, string PaymentMethod = "", string? PromoCode = null);
public sealed record StorefrontCheckoutResult(Guid OrderId, string OrderNumber, decimal Amount,
    string Currency, Guid PaymentId, string? CheckoutUrl, string PaymentProvider, string PaymentStatus, string? CustomerMessage,
    string? CustomerSessionToken, string PaymentMethod = "COD");

public interface IStorefrontService
{
    Task<StorefrontStoreDto?> GetStoreAsync(string storeKey, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontCategoryDto>?> GetCategoriesAsync(string storeKey, CancellationToken token);
    Task<StorefrontProductPageDto?> GetProductsAsync(string storeKey, int page, int pageSize, CancellationToken token);
    Task<StorefrontProductDto?> GetProductAsync(string storeKey, Guid productId, CancellationToken token);
    Task<StorefrontOfferDto?> GetOfferAsync(string storeKey, Guid offerId, CancellationToken token);
    Task<StorefrontImage?> GetProductImageAsync(string storeKey, Guid productId, Guid? imageId, bool thumbnail, CancellationToken token);
    Task<StorefrontImage?> GetPresentationImageAsync(string storeKey, string resource, Guid? categoryId, CancellationToken token);
    Task<StorefrontImage?> GetPwaIconAsync(string storeKey, int size, CancellationToken token);
}

public sealed record StorefrontBannerAdminDto(string Slot, bool IsEnabled, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder, string? ImageUrl, Guid? PromotionId = null);
public sealed record StorefrontAdminDto(string StoreName, string? LogoUrl, bool DeliveryEnabled, decimal StandardDeliveryCharge, bool FreeDeliveryEnabled, decimal? FreeDeliveryThreshold, bool ShowProductRatings, bool ShowProductReviews, int DefaultReturnWindowDays, IReadOnlyCollection<StorefrontBannerAdminDto> Banners, IReadOnlyCollection<StorefrontPincodeDto> ServiceablePincodes, IReadOnlyCollection<StorefrontPromotionDto> Promotions, string? AllCategoryImageUrl = null, bool ShowProductDetailsOnShippingLabel = true);
public sealed record UpdateStorefrontReviewSettingsInput(bool ShowProductRatings, bool ShowProductReviews);
public sealed record UpdateStorefrontShippingLabelSettingsInput(bool ShowProductDetailsOnShippingLabel);
public sealed record UpdateStorefrontDeliverySettingsInput(bool DeliveryEnabled, decimal StandardDeliveryCharge, bool FreeDeliveryEnabled, decimal? FreeDeliveryThreshold, int DefaultReturnWindowDays = 7);
public sealed record StorefrontPincodeDto(string Pincode, bool IsActive);
public sealed record StorefrontPromotionDto(Guid PromotionId, string OfferName, string OfferType, decimal MinimumPurchaseAmount, string DiscountType, decimal DiscountValue, decimal? MaximumDiscount, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool IsActive, int? UsageLimitPerCustomer, string? ShortDescription = null, string? DetailedDescription = null, string? TermsAndConditions = null, string? PromoCode = null, string? CtaLabel = null, string? EligibleItemsDescription = null);
public sealed record SaveStorefrontPromotionInput(string OfferName, string OfferType, decimal MinimumPurchaseAmount, string DiscountType, decimal DiscountValue, decimal? MaximumDiscount, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool IsActive, int? UsageLimitPerCustomer, string? ShortDescription = null, string? DetailedDescription = null, string? TermsAndConditions = null, string? PromoCode = null, string? CtaLabel = null, string? EligibleItemsDescription = null);
public sealed record UpdateStorefrontBannerInput(bool IsEnabled, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder, Guid? PromotionId = null);

public interface IStorefrontAdministrationService
{
    Task<StorefrontAdminDto> GetAsync(CancellationToken token);
    Task UpdateBannerAsync(string slot, UpdateStorefrontBannerInput input, CancellationToken token);
    Task UpdateReviewSettingsAsync(UpdateStorefrontReviewSettingsInput input, CancellationToken token);
    Task UpdateShippingLabelSettingsAsync(UpdateStorefrontShippingLabelSettingsInput input, CancellationToken token);
    Task UpdateDeliverySettingsAsync(UpdateStorefrontDeliverySettingsInput input, CancellationToken token);
    Task SavePincodeAsync(StorefrontPincodeDto input, CancellationToken token);
    Task RemovePincodeAsync(string pincode, CancellationToken token);
    Task<StorefrontPromotionDto> SavePromotionAsync(Guid? id, SaveStorefrontPromotionInput input, CancellationToken token);
    Task RemovePromotionAsync(Guid id, CancellationToken token);
    Task<StorefrontImage?> GetImageAsync(string resource, Guid? categoryId, CancellationToken token);
    Task UploadImageAsync(string resource, Guid? categoryId, string fileName, Stream content, CancellationToken token);
    Task RemoveImageAsync(string resource, Guid? categoryId, CancellationToken token);
}

public sealed record StorefrontCustomerDto(Guid Id, string Name, string? Email, string? Mobile, string? ProfileImageUrl = null);
public sealed record StorefrontCustomerReferralDto(Guid ReferralId, string Status, string QualificationType, DateTimeOffset? QualifiedAt, DateTimeOffset? RewardedAt, DateTimeOffset? ReversedAt, DateTimeOffset CreatedAt, string ReferredCustomerDisplay);
public sealed record StorefrontCustomerRewardsDto(bool Enabled, CoinWalletDto? Wallet, ReferralCodeDto? ReferralCode,
    IReadOnlyCollection<StorefrontCustomerReferralDto> Referrals, string Terms, int CoinValidityDays);
public sealed record StorefrontReferralCaptureInput(string Code, string Source = "STOREFRONT");
public sealed record StorefrontCustomerAddressDto(Guid AddressId, string RecipientName, string Mobile, string AddressLine1, string? AddressLine2, string? Landmark, string City, string State, string Pincode, string AddressType, bool IsDefault);
public sealed record StorefrontCustomerAddressInput(string RecipientName, string Mobile, string AddressLine1, string? AddressLine2, string? Landmark, string City, string State, string Pincode, string AddressType, bool IsDefault = false);
public sealed record StorefrontCustomerOrderLineDto(Guid ProductId, string ProductName, string? PackSize, decimal Quantity, decimal LineTotal, bool IsReturnable = false, int? ReturnWindowDays = null, DateTimeOffset? DeliveredAt = null, DateTimeOffset? ReturnDeadline = null, string ReturnStatus = "NOT_ELIGIBLE", string? ReturnPolicyDescription = null, Guid? InvoiceItemId = null, decimal ReturnedQuantity = 0, decimal ReturnableQuantity = 0, decimal ReturnRequestedQuantity = 0, string? ReturnRequestStatus = null, Guid? ReturnRequestId = null);
public sealed record StorefrontOrderMilestoneDto(string Code, string Label, string State, DateTimeOffset? OccurredAt);
public sealed record StorefrontCustomerOrderDto(Guid Id, string OrderNumber, DateTimeOffset PlacedAt,
    string Status, decimal Total, string? DeliveryStatus, string? TrackingNumber, string PaymentMethod, string PaymentStatus,
    int ItemCount, IReadOnlyCollection<StorefrontCustomerOrderLineDto>? Lines = null, IReadOnlyCollection<StorefrontOrderMilestoneDto>? Timeline = null, decimal DeliveryCharge = 0, decimal PromotionDiscount = 0, string? PromotionName = null, decimal MerchandiseAmount = 0, StorefrontCancellationDto? Cancellation = null, decimal MerchandiseTaxAmount = 0, decimal MerchandiseSubtotal = 0);
public sealed record StorefrontCartQuoteInput(IReadOnlyCollection<StorefrontCheckoutItem> Items, string Pincode, string? PromoCode = null);
public sealed record StorefrontCartQuoteDto(decimal EligibleAmount, decimal? FreeDeliveryThreshold, decimal RemainingAmount, int ProgressPercent, bool IsFreeDeliveryUnlocked, bool IsDeliveryEnabled = false, bool IsPincodeServiceable = false, decimal MerchandiseAmount = 0, decimal MerchandiseTaxAmount = 0, decimal StandardDeliveryCharge = 0, bool FreeDeliveryEnabled = false, decimal DeliveryCharge = 0, decimal PromotionDiscount = 0, string? PromotionName = null, Guid? PromotionId = null, decimal FinalPayableAmount = 0, decimal MerchandiseSubtotal = 0, string? PromotionCode = null);
public sealed record StorefrontWishlistInput(IReadOnlyCollection<Guid> ProductIds);
public sealed record StorefrontOtpRequest(string MobileNumber);
public sealed record StorefrontOtpChallengeDto(Guid ChallengeId, DateTimeOffset ExpiresAt, int ResendAfterSeconds);
public sealed record StorefrontOtpVerifyInput(Guid ChallengeId, string MobileNumber, string Otp, string? Name, string? Email, bool IsRegistration = true, string? Password = null, string? ConfirmPassword = null, bool ReplacePassword = false);
public sealed record StorefrontPasswordSignInInput(string MobileNumber, string Password);
public sealed record StorefrontPasswordResetInput(Guid ChallengeId, string MobileNumber, string Otp, string NewPassword, string ConfirmPassword);
public sealed record StorefrontChangePasswordInput(string CurrentPassword, string NewPassword, string ConfirmPassword);
public sealed record StorefrontAuthenticationDto(string CustomerSessionToken, StorefrontCustomerDto Customer);
public sealed record StorefrontDeliveryAvailabilityDto(bool DeliveryEnabled, bool IsPincodeServiceable);
public sealed record UpdateStorefrontCustomerInput(string? Name, string? Email);

public interface ICustomerOtpSender
{
    Task SendAsync(string normalizedMobile, string otp, Guid challengeId, CancellationToken token);
}

public interface IStorefrontCustomerAuthenticationService
{
    Task<StorefrontOtpChallengeDto?> RequestOtpAsync(string storeKey, StorefrontOtpRequest input, CancellationToken token);
    Task<StorefrontAuthenticationDto?> VerifyOtpAsync(string storeKey, StorefrontOtpVerifyInput input, CancellationToken token);
    Task<StorefrontAuthenticationDto?> SignInPasswordAsync(string storeKey, StorefrontPasswordSignInInput input, CancellationToken token);
    Task<bool> ResetPasswordAsync(string storeKey, StorefrontPasswordResetInput input, CancellationToken token);
    Task<bool> ChangePasswordAsync(string storeKey, string sessionToken, StorefrontChangePasswordInput input, CancellationToken token);
    Task<StorefrontCustomerDto?> UpdateProfileAsync(string storeKey, string sessionToken, UpdateStorefrontCustomerInput input, CancellationToken token);
}

public interface IStorefrontCustomerService
{
    Task<string> IssueSessionAsync(string storeKey, Guid tenantId, Guid customerId, CancellationToken token);
    Task<StorefrontCustomerDto?> GetSessionAsync(string storeKey, string sessionToken, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontCustomerOrderDto>?> GetOrdersAsync(string storeKey, string sessionToken, CancellationToken token);
    Task<StorefrontCustomerOrderDto?> GetOrderAsync(string storeKey, string sessionToken, Guid orderId, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontProductDto>?> GetWishlistAsync(string storeKey, string sessionToken, CancellationToken token);
    Task<bool> AddWishlistAsync(string storeKey, string sessionToken, Guid productId, CancellationToken token);
    Task<bool> RemoveWishlistAsync(string storeKey, string sessionToken, Guid productId, CancellationToken token);
    Task<bool> MergeWishlistAsync(string storeKey, string sessionToken, IReadOnlyCollection<Guid> productIds, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontCustomerAddressDto>?> GetAddressesAsync(string storeKey, string sessionToken, CancellationToken token);
    Task<StorefrontCustomerAddressDto?> SaveAddressAsync(string storeKey, string sessionToken, Guid? addressId, StorefrontCustomerAddressInput input, CancellationToken token);
    Task<bool> DeleteAddressAsync(string storeKey, string sessionToken, Guid addressId, CancellationToken token);
    Task<bool> SetDefaultAddressAsync(string storeKey, string sessionToken, Guid addressId, CancellationToken token);
    Task<StorefrontCustomerDto?> UploadProfileImageAsync(string storeKey, string sessionToken, string fileName, Stream content, CancellationToken token);
    Task<StorefrontCustomerDto?> RemoveProfileImageAsync(string storeKey, string sessionToken, CancellationToken token);
    Task<StorefrontImage?> GetProfileImageAsync(string storeKey, string sessionToken, CancellationToken token);
}

public sealed record StorefrontActivityInput(string VisitorSessionId, string EventType, Guid? ProductId = null, string? SearchTerm = null);
public sealed record StorefrontSearchEventInput(string Keyword, string? AnonymousSessionId = null);
public sealed record StorefrontAvailabilityRequestInput(string? Mobile, string? PreferredChannel = "WHATSAPP", bool ConsentGiven = false);
public sealed record StorefrontMissingProductRequestInput(string Keyword, string? Mobile, string? Remarks = null, string? AnonymousSessionId = null, Guid? SearchEventId = null);
public sealed record StorefrontFeedbackInput(string FeedbackType, string Category, string Subject, string Description, Guid? OrderId = null, string? GuestName = null, string? GuestMobile = null);
public sealed record StorefrontFeedbackTicketDto(Guid TicketId, string TicketReference, string FeedbackType, string Category, string Subject, string Description, Guid? OrderId, string Priority, string Status, string? CustomerVisibleReply, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string? CustomerName = null, string? CustomerMobile = null);
public sealed record StorefrontFeedbackActivityDto(string ActivityType, string? FromStatus, string? ToStatus, string? CustomerReply, DateTimeOffset CreatedAtUtc);
public sealed record StorefrontFeedbackTicketDetailDto(StorefrontFeedbackTicketDto Ticket, IReadOnlyCollection<StorefrontFeedbackActivityDto> Activity);
public sealed record UpdateStorefrontFeedbackInput(string? Status = null, string? Priority = null, Guid? AssignedTo = null, string? CustomerReply = null, string? InternalNote = null);
public sealed record StorefrontFeedbackPageDto(IReadOnlyCollection<StorefrontFeedbackTicketDto> Items, int TotalCount, int PageNumber, int PageSize, int OpenCount, int PendingCount);
public sealed record StorefrontBlacklistDto(Guid BlacklistId, string NormalizedPhone, string Reason, bool IsActive, string? BlockedBy, DateTimeOffset BlockedAtUtc, string? UnblockedBy, DateTimeOffset? UnblockedAtUtc);
public sealed record StorefrontBlacklistInput(string Phone, string Reason);
public sealed record StorefrontBlacklistPageDto(IReadOnlyCollection<StorefrontBlacklistDto> Items, int TotalCount, int PageNumber, int PageSize);
public interface IStorefrontFeedbackService
{
    Task<StorefrontFeedbackTicketDto> SubmitAsync(string storeKey, string? sessionToken, StorefrontFeedbackInput input, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontFeedbackTicketDto>?> CustomerTicketsAsync(string storeKey, string sessionToken, CancellationToken token);
    Task<StorefrontFeedbackTicketDetailDto?> CustomerTicketAsync(string storeKey, string sessionToken, Guid ticketId, CancellationToken token);
    Task<StorefrontFeedbackPageDto> SearchAsync(Guid tenantId, string? search, string? status, string? priority, int pageNumber, int pageSize, CancellationToken token);
    Task<StorefrontFeedbackTicketDetailDto?> GetAsync(Guid tenantId, Guid ticketId, bool includeInternal, CancellationToken token);
    Task<StorefrontFeedbackTicketDetailDto> UpdateAsync(Guid tenantId, Guid actorUserId, Guid ticketId, UpdateStorefrontFeedbackInput input, CancellationToken token);
}
public interface IStorefrontCustomerBlacklistService
{
    Task<bool> IsBlockedAsync(Guid tenantId, string phone, CancellationToken token);
    Task<StorefrontBlacklistPageDto> SearchAsync(Guid tenantId, bool activeOnly, string? search, int pageNumber, int pageSize, CancellationToken token);
    Task<StorefrontBlacklistDto> BlockAsync(Guid tenantId, Guid actorUserId, StorefrontBlacklistInput input, CancellationToken token);
    Task<bool> UnblockAsync(Guid tenantId, Guid actorUserId, Guid blacklistId, CancellationToken token);
}
public sealed record StorefrontAvailabilityRequestDto(Guid Id, Guid ProductId, string Status, DateTimeOffset RequestedAtUtc);
public sealed record CustomerDemandSummaryDto(int TotalSearches, int UniqueKeywords, int AvailableSearches, int OutOfStockSearches, int NoResultSearches, int MixedSearches, IReadOnlyCollection<CustomerDemandKeywordDto> Keywords, IReadOnlyCollection<CustomerDemandRequestDto> AvailabilityRequests, IReadOnlyCollection<CustomerDemandMissingDto> MissingRequests, IReadOnlyCollection<CustomerDemandTrendDto>? Trends = null);
public sealed record CustomerDemandKeywordDto(string Keyword, int Searches, string Outcome, int InterestedCustomers, DateTime LastSearchedAtUtc);
public sealed record CustomerDemandRequestDto(Guid Id, Guid ProductId, string ProductName, string? ProductCode, decimal AvailableStock, int InterestedCustomers, string Status, DateTime RequestedAtUtc, int ActiveRequestCount = 0, int CancelledRequestCount = 0);
public sealed record CustomerDemandMissingDto(Guid Id, string Keyword, int Searches, int InterestedCustomers, string Status, DateTime LastRequestedAtUtc);
public sealed record CustomerDemandTrendDto(DateTime Date, int Searches, int AvailableSearches, int OutOfStockSearches, int NoResultSearches, int MixedSearches);
public sealed record StorefrontActivitySummaryDto(int VisitorsToday, int UniqueVisitors, int SignedInCustomers, int NewCustomers, int ReturningCustomers, int ProductViews, int AddToCarts, int Checkouts, int Orders);
public sealed record StorefrontActivityRowDto(string VisitorLabel, string? Mobile, DateTimeOffset LastVisit, DateTimeOffset? LastSignIn, int Sessions, int Orders, string LastActivity);
public sealed record StorefrontActivityReportDto(StorefrontActivitySummaryDto Summary, IReadOnlyCollection<StorefrontActivityRowDto> RecentActivity);
public sealed record StorefrontBehaviorEventDto(string EventType, string VisitorLabel, Guid? CustomerId, Guid? ProductId, string? ProductName, DateTimeOffset OccurredOn);
public sealed record StorefrontWishlistActivityDto(Guid CustomerId, string CustomerName, Guid ProductId, string ProductName, decimal SellingPrice, string Availability, DateTimeOffset SavedAt);
public sealed record StorefrontBehaviorReportDto(IReadOnlyCollection<StorefrontBehaviorEventDto> Events, IReadOnlyCollection<StorefrontWishlistActivityDto> Wishlist);
public sealed record StorefrontCustomerManagementRowDto(Guid CustomerId, string Name, string? Mobile, DateTimeOffset RegisteredOn, DateTimeOffset? LastSuccessfulLogin, DateTimeOffset? LastVisit, int TotalOrders, decimal TotalSpent, bool IsActive);
public sealed record StorefrontCustomerManagementPageDto(IReadOnlyCollection<StorefrontCustomerManagementRowDto> Items, int TotalCount, int PageNumber, int PageSize);
public sealed record StorefrontCustomerManagementOrderDto(string OrderNumber, DateTimeOffset PlacedOn, string Status, decimal Total);
public sealed record StorefrontCustomerManagementActivityDto(string EventType, DateTimeOffset OccurredOn, Guid? ProductId, string? ProductName = null);
public sealed record StorefrontCustomerManagementSearchDto(string Keyword, string Outcome, DateTime CreatedAtUtc);
public sealed record StorefrontCustomerManagementWishlistDto(Guid ProductId, string ProductName, decimal SellingPrice, DateTimeOffset SavedAt);
public sealed record StorefrontCustomerManagementDetailDto(StorefrontCustomerManagementRowDto Customer, IReadOnlyCollection<StorefrontCustomerManagementOrderDto> Orders, IReadOnlyCollection<StorefrontCustomerManagementActivityDto> RecentActivity, IReadOnlyCollection<StorefrontReturnRequestDto> ReturnRequests, IReadOnlyCollection<StorefrontCustomerManagementSearchDto>? SearchHistory = null, IReadOnlyCollection<StorefrontCustomerManagementWishlistDto>? Wishlist = null);
public interface IStorefrontCustomerManagementService
{
    Task<StorefrontCustomerManagementPageDto> SearchAsync(Guid tenantId, string? search, string sortBy, bool descending, int pageNumber, int pageSize, CancellationToken token);
    Task<StorefrontCustomerManagementDetailDto?> GetAsync(Guid tenantId, Guid customerId, CancellationToken token);
}
public interface IStorefrontActivityService
{
    Task RecordAsync(string storeKey, string? sessionToken, StorefrontActivityInput input, CancellationToken token);
    Task<StorefrontActivityReportDto> ReportAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset toDate, CancellationToken token);
    Task<StorefrontBehaviorReportDto> BehaviorAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset toDate, CancellationToken token);
}
public interface ICustomerDemandService
{
    Task<CustomerDemandSummaryDto> RecordSearchAsync(string storeKey, string? sessionToken, StorefrontSearchEventInput input, CancellationToken token);
    Task<StorefrontAvailabilityRequestDto> RequestAvailabilityAsync(string storeKey, Guid productId, string? sessionToken, StorefrontAvailabilityRequestInput input, CancellationToken token);
    Task<bool> CancelAvailabilityAsync(string storeKey, Guid productId, string sessionToken, CancellationToken token);
    Task<CustomerDemandMissingDto> RequestMissingProductAsync(string storeKey, string? sessionToken, StorefrontMissingProductRequestInput input, CancellationToken token);
    Task<CustomerDemandSummaryDto> SummaryAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset until, CancellationToken token);
}
public sealed record UpdateMissingProductRequestStatusInput(string Status);
public interface ICustomerDemandManagementService
{
    Task<bool> UpdateMissingProductStatusAsync(Guid tenantId, Guid requestId, string status, CancellationToken token);
}

public interface IStorefrontCustomerRewardsService
{
    Task<StorefrontCustomerRewardsDto?> GetRewardsAsync(string storeKey, string sessionToken, int take, CancellationToken token);
    Task<bool> CaptureReferralAsync(string storeKey, string sessionToken, StorefrontReferralCaptureInput input, CancellationToken token);
}

public interface IStorefrontCheckoutService
{
    Task<StorefrontDeliveryAvailabilityDto?> CheckDeliveryAsync(string storeKey, string pincode, CancellationToken token);
    Task<StorefrontCartQuoteDto?> QuoteAsync(string storeKey, StorefrontCartQuoteInput input, string? customerSessionToken, CancellationToken token);
    Task<StorefrontCheckoutResult> CheckoutAsync(string storeKey, StorefrontCheckoutInput input,
        string idempotencyKey, string? customerSessionToken, CancellationToken token);
}

public sealed record StorefrontReviewDto(Guid ReviewId, string ReviewerName, int Rating, string ReviewText, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsOwn);
public sealed record StorefrontReviewSummaryDto(decimal? AverageRating, int RatingCount, IReadOnlyCollection<StorefrontReviewDto> Reviews);
public sealed record StorefrontReviewInput(int Rating, string ReviewText);
public interface IStorefrontReviewService
{
    Task<StorefrontReviewSummaryDto?> GetAsync(string storeKey, Guid productId, string? sessionToken, CancellationToken token);
    Task<StorefrontReviewDto?> UpsertAsync(string storeKey, Guid productId, string sessionToken, StorefrontReviewInput input, CancellationToken token);
}
public sealed record StorefrontCancellationRequestInput(string Reason);
public sealed record StorefrontCancellationDecisionInput(string? Note);
public sealed record StorefrontCancellationDto(bool CanRequest, string? UnavailableReason,
    string? RequestStatus, string? Reason, decimal RefundableAmount, string RefundStatus,
    decimal RefundAmount, DateTimeOffset? RefundedAt, bool RefundNeedsReconciliation = false,
    string? DecisionNote = null);

public sealed record StorefrontReturnRequestInput(decimal Quantity, string Reason, string? Explanation = null);
public sealed record StorefrontReturnRequestDto(Guid RequestId, Guid OrderId, Guid InvoiceItemId, string OrderNumber,
    string ProductName, string? ProductCode, decimal Quantity, string Reason, string? Explanation, string Status,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? DecidedAtUtc = null, string? DecisionNote = null);
public sealed record StorefrontReturnDecisionInput(string Status, string? Note = null);
public interface IStorefrontReturnService
{
    Task<StorefrontReturnRequestDto> RequestAsync(string storeKey, string sessionToken, Guid orderId, Guid invoiceItemId, StorefrontReturnRequestInput input, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontReturnRequestDto>?> CustomerRequestsAsync(string storeKey, string sessionToken, Guid orderId, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontReturnRequestDto>> RetailerRequestsAsync(Guid tenantId, CancellationToken token);
    Task<StorefrontReturnRequestDto> DecideAsync(Guid tenantId, Guid requestId, Guid actorId, StorefrontReturnDecisionInput input, CancellationToken token);
}
public interface IStorefrontCancellationService
{
    Task<StorefrontCancellationDto?> GetCustomerStatusAsync(string storeKey, string sessionToken, Guid orderId, CancellationToken token);
    Task<StorefrontCancellationDto?> RequestAsync(string storeKey, string sessionToken, Guid orderId, string reason, CancellationToken token);
    Task<StorefrontCancellationDto> DecideAsync(Guid tenantId, Guid orderId, Guid actorId, bool approve, string? note, CancellationToken token);
}
