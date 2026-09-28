namespace WhatsBiz.Application.Features.Storefront;

public sealed record StorefrontBannerDto(string Slot, string ImageUrl, string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder);
public sealed record StorefrontPaymentMethodDto(string Code, string Label, string Description, bool IsDefault, bool UsesHostedPaymentPage);
public sealed record StorefrontStoreDto(string StoreKey, string Name, string Tagline, string? LogoUrl, string AccentColor,
    string DeliveryMessage, decimal? FreeDeliveryThreshold, bool ShowProductRatings, bool ShowProductReviews, IReadOnlyCollection<StorefrontBannerDto> Banners, IReadOnlyCollection<StorefrontPaymentMethodDto> PaymentMethods, string? AllCategoryImageUrl = null);
public sealed record StorefrontCategoryDto(Guid Id, string Name, string? ImageUrl);
public sealed record StorefrontProductDto(Guid Id, Guid CategoryId, string Name, string Description, string? ImageUrl, decimal SellingPrice, decimal? CompareAtPrice, string Availability, string? PackSize, decimal? AverageRating, int RatingCount);
public sealed record StorefrontImage(string ContentType, byte[] Content);
public sealed record StorefrontCheckoutItem(Guid ProductId, decimal Quantity);
public sealed record StorefrontCheckoutInput(string CustomerName, string Mobile, string? Email, string Pincode,
    string DeliveryAddress, IReadOnlyCollection<StorefrontCheckoutItem> Items, string PaymentProvider = "RAZORPAY");
public sealed record StorefrontCheckoutResult(Guid OrderId, string OrderNumber, decimal Amount,
    string Currency, Guid PaymentId, string? CheckoutUrl, string PaymentProvider, string PaymentStatus, string? CustomerMessage,
    string? CustomerSessionToken);

public interface IStorefrontService
{
    Task<StorefrontStoreDto?> GetStoreAsync(string storeKey, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontCategoryDto>?> GetCategoriesAsync(string storeKey, CancellationToken token);
    Task<IReadOnlyCollection<StorefrontProductDto>?> GetProductsAsync(string storeKey, CancellationToken token);
    Task<StorefrontProductDto?> GetProductAsync(string storeKey, Guid productId, CancellationToken token);
    Task<StorefrontImage?> GetProductImageAsync(string storeKey, Guid productId, CancellationToken token);
    Task<StorefrontImage?> GetPresentationImageAsync(string storeKey, string resource, Guid? categoryId, CancellationToken token);
}

public sealed record StorefrontBannerAdminDto(string Slot, bool IsEnabled, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder, string? ImageUrl);
public sealed record StorefrontAdminDto(string StoreName, string? LogoUrl, bool DeliveryEnabled, decimal StandardDeliveryCharge, bool FreeDeliveryEnabled, decimal? FreeDeliveryThreshold, bool ShowProductRatings, bool ShowProductReviews, IReadOnlyCollection<StorefrontBannerAdminDto> Banners, IReadOnlyCollection<StorefrontPincodeDto> ServiceablePincodes, IReadOnlyCollection<StorefrontPromotionDto> Promotions, string? AllCategoryImageUrl = null);
public sealed record UpdateStorefrontReviewSettingsInput(bool ShowProductRatings, bool ShowProductReviews);
public sealed record UpdateStorefrontDeliverySettingsInput(bool DeliveryEnabled, decimal StandardDeliveryCharge, bool FreeDeliveryEnabled, decimal? FreeDeliveryThreshold);
public sealed record StorefrontPincodeDto(string Pincode, bool IsActive);
public sealed record StorefrontPromotionDto(Guid PromotionId, string OfferName, string OfferType, decimal MinimumPurchaseAmount, string DiscountType, decimal DiscountValue, decimal? MaximumDiscount, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool IsActive, int? UsageLimitPerCustomer);
public sealed record SaveStorefrontPromotionInput(string OfferName, string OfferType, decimal MinimumPurchaseAmount, string DiscountType, decimal DiscountValue, decimal? MaximumDiscount, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool IsActive, int? UsageLimitPerCustomer);
public sealed record UpdateStorefrontBannerInput(bool IsEnabled, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? Title, string? Subtitle, string? TargetUrl, int DisplayOrder);

public interface IStorefrontAdministrationService
{
    Task<StorefrontAdminDto> GetAsync(CancellationToken token);
    Task UpdateBannerAsync(string slot, UpdateStorefrontBannerInput input, CancellationToken token);
    Task UpdateReviewSettingsAsync(UpdateStorefrontReviewSettingsInput input, CancellationToken token);
    Task UpdateDeliverySettingsAsync(UpdateStorefrontDeliverySettingsInput input, CancellationToken token);
    Task SavePincodeAsync(StorefrontPincodeDto input, CancellationToken token);
    Task RemovePincodeAsync(string pincode, CancellationToken token);
    Task<StorefrontPromotionDto> SavePromotionAsync(Guid? id, SaveStorefrontPromotionInput input, CancellationToken token);
    Task RemovePromotionAsync(Guid id, CancellationToken token);
    Task<StorefrontImage?> GetImageAsync(string resource, Guid? categoryId, CancellationToken token);
    Task UploadImageAsync(string resource, Guid? categoryId, string fileName, Stream content, CancellationToken token);
    Task RemoveImageAsync(string resource, Guid? categoryId, CancellationToken token);
}

public sealed record StorefrontCustomerDto(Guid Id, string Name, string? Email, string? Mobile);
public sealed record StorefrontCustomerOrderLineDto(Guid ProductId, string ProductName, string? PackSize, decimal Quantity, decimal LineTotal);
public sealed record StorefrontOrderMilestoneDto(string Code, string Label, string State, DateTimeOffset? OccurredAt);
public sealed record StorefrontCustomerOrderDto(Guid Id, string OrderNumber, DateTimeOffset PlacedAt,
    string Status, decimal Total, string? DeliveryStatus, string? TrackingNumber, string PaymentMethod, string PaymentStatus,
    int ItemCount, IReadOnlyCollection<StorefrontCustomerOrderLineDto>? Lines = null, IReadOnlyCollection<StorefrontOrderMilestoneDto>? Timeline = null, decimal DeliveryCharge = 0, decimal PromotionDiscount = 0, string? PromotionName = null, decimal MerchandiseAmount = 0, StorefrontCancellationDto? Cancellation = null);
public sealed record StorefrontCartQuoteInput(IReadOnlyCollection<StorefrontCheckoutItem> Items, string Pincode);
public sealed record StorefrontCartQuoteDto(decimal EligibleAmount, decimal? FreeDeliveryThreshold, decimal RemainingAmount, int ProgressPercent, bool IsFreeDeliveryUnlocked, bool IsDeliveryEnabled = false, bool IsPincodeServiceable = false, decimal MerchandiseAmount = 0, decimal MerchandiseTaxAmount = 0, decimal StandardDeliveryCharge = 0, bool FreeDeliveryEnabled = false, decimal DeliveryCharge = 0, decimal PromotionDiscount = 0, string? PromotionName = null, Guid? PromotionId = null, decimal FinalPayableAmount = 0);
public sealed record StorefrontWishlistInput(IReadOnlyCollection<Guid> ProductIds);
public sealed record StorefrontOtpRequest(string MobileNumber);
public sealed record StorefrontOtpChallengeDto(Guid ChallengeId, DateTimeOffset ExpiresAt, int ResendAfterSeconds);
public sealed record StorefrontOtpVerifyInput(Guid ChallengeId, string MobileNumber, string Otp, string? Name, string? Email);
public sealed record StorefrontAuthenticationDto(string CustomerSessionToken, StorefrontCustomerDto Customer);
public sealed record UpdateStorefrontCustomerInput(string? Name, string? Email);

public interface ICustomerOtpSender
{
    Task SendAsync(string normalizedMobile, string otp, Guid challengeId, CancellationToken token);
}

public interface IStorefrontCustomerAuthenticationService
{
    Task<StorefrontOtpChallengeDto?> RequestOtpAsync(string storeKey, StorefrontOtpRequest input, CancellationToken token);
    Task<StorefrontAuthenticationDto?> VerifyOtpAsync(string storeKey, StorefrontOtpVerifyInput input, CancellationToken token);
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
}

public interface IStorefrontCheckoutService
{
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
public interface IStorefrontCancellationService
{
    Task<StorefrontCancellationDto?> GetCustomerStatusAsync(string storeKey, string sessionToken, Guid orderId, CancellationToken token);
    Task<StorefrontCancellationDto?> RequestAsync(string storeKey, string sessionToken, Guid orderId, string reason, CancellationToken token);
    Task<StorefrontCancellationDto> DecideAsync(Guid tenantId, Guid orderId, Guid actorId, bool approve, string? note, CancellationToken token);
}
