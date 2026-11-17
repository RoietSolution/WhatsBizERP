namespace WhatsBiz.Application.Common.Features;

public static class FeatureKeys
{
    public const string V1 = "V1";
    public const string V2 = "V2";
    public const string Dashboard = "DASHBOARD";
    public const string Pos = "POS";
    public const string Products = "PRODUCTS";
    public const string Customers = "CUSTOMERS";
    public const string Purchase = "PURCHASE";
    public const string Inventory = "INVENTORY";
    public const string Finance = "FINANCE";
    public const string Reports = "REPORTS";
    public const string UsersRoles = "USERS_ROLES";
    public const string Suppliers = "SUPPLIERS";
    public const string Warehouses = "WAREHOUSES";
    public const string Printing = "PRINTING";
    public const string Gst = "GST";
    public const string Analytics = "ANALYTICS";
    public const string Administration = "ADMINISTRATION";
    public const string WhatsAppCommerce = "WHATSAPP_COMMERCE";
    public const string WhatsAppConfiguration = "WHATSAPP_CONFIGURATION";
    public const string WhatsAppCommerceDemo = "WHATSAPP_COMMERCE_DEMO";
    public const string CommerceProductSearch = "COMMERCE_PRODUCT_SEARCH";
    public const string CommerceCollections = "COMMERCE_COLLECTIONS";
    public const string CommerceOrders = "COMMERCE_ORDERS";
    public const string CommerceAnalytics = "COMMERCE_ANALYTICS";
    public const string MetaWhatsAppIntegration = "META_WHATSAPP_INTEGRATION";
    public const string WebhookDiagnostics = "WEBHOOK_DIAGNOSTICS";
    public const string AdvancedWarehouse = "ADVANCED_WAREHOUSE";
    public const string AiAssistant = "AI_ASSISTANT";
    public const string Integrations = "INTEGRATIONS";
    public const string CustomerReferralRewards = "CUSTOMER_REFERRAL_REWARDS";
    public const string DeliveryManagement = "DELIVERY_MANAGEMENT";
    public const string CustomerDemand = "CUSTOMER_DEMAND";
    public const string StorefrontFeedback = "STOREFRONT_FEEDBACK";
    public const string StorefrontCustomerBlacklist = "STOREFRONT_CUSTOMER_BLACKLIST";
    public const string StorefrontScheduledSelling = "STOREFRONT_SCHEDULED_SELLING";
    public const string StorefrontPopupCampaigns = "STOREFRONT_POPUP_CAMPAIGNS";
    public const string StorefrontNotifications = "STOREFRONT_NOTIFICATIONS";
    public const string PartnerSellerManagement = "PARTNER_SELLER_MANAGEMENT";
    public const string PartnerCommissions = "PARTNER_COMMISSIONS";
    public const string PartnerSettlements = "PARTNER_SETTLEMENTS";
    public const string CityMarketplace = "CITY_MARKETPLACE";
    public const string MultiChannelOrderIntake = "MULTI_CHANNEL_ORDER_INTAKE";
    public const string GuestVisitorAnalytics = "GUEST_VISITOR_ANALYTICS";
    public const string MarketplaceCheckout = "MARKETPLACE_CHECKOUT";
    public static bool RequiresFreshEvaluation(string key) => new[] { StorefrontScheduledSelling, StorefrontPopupCampaigns, StorefrontNotifications }.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyCollection<string> All =
        [V1, Dashboard, Pos, Products, Customers, Purchase, Inventory, Finance, Reports,
         UsersRoles, Suppliers, Warehouses, Printing, Gst, Analytics, Administration,
         V2, WhatsAppCommerce, WhatsAppConfiguration, WhatsAppCommerceDemo,
         CommerceProductSearch, CommerceCollections, CommerceOrders, CommerceAnalytics,
         MetaWhatsAppIntegration, WebhookDiagnostics, AdvancedWarehouse, AiAssistant, Integrations, CustomerReferralRewards, DeliveryManagement, CustomerDemand, StorefrontFeedback, StorefrontCustomerBlacklist,
         StorefrontScheduledSelling, StorefrontPopupCampaigns, StorefrontNotifications,
         PartnerSellerManagement, PartnerCommissions, PartnerSettlements, CityMarketplace, MultiChannelOrderIntake, GuestVisitorAnalytics, MarketplaceCheckout];
}
