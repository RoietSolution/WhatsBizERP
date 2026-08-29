:r ..\SeedData\SupplierPaymentTerms.sql
:r ..\SeedData\CustomerPaymentTerms.sql
:r ..\SeedData\WarehouseTypes.sql
:r ..\SeedData\InventorySettings.sql
:r ..\SeedData\PaymentMethods.sql
:r ..\SeedData\InvoiceSeries.sql
:r ..\SeedData\PurchaseSeries.sql
:r .\RCDEV008-RuntimeObjects.sql
:r .\RCDEV009-PrintingPaperSize.sql
:r .\RCDEV010-CustomerNotifications.sql
GO
:r .\V2-FeatureEntitlements.sql
GO
-- V2 integration foundation. Keep this complete chain before V3+ scripts:
-- WC001 creates tenant WhatsApp configuration; the provider script creates commerce orders;
-- readiness/transport scripts extend those objects before V10/V11 consume them.
:r .\V2-WC001-WhatsAppConfiguration.sql
GO
:r .\V2-WhatsAppCommerceDemoProvider.sql
GO
:r .\V2-WC-DEMO-002-ReadinessLifecycle.sql
GO
:r .\V2-WC003-MetaTestTransport.sql
GO
:r .\V2-WC003A-MetaTestReadiness.sql
GO
:r .\V3-ProductImageOptimization.sql
GO
:r .\V4-DashboardNotificationDetails.sql
GO
:r .\V5-CommerceCollections.sql
GO
:r .\V6-CommerceProductChannelMappings.sql
GO
:r .\V7-CustomerTenantIsolation.sql
GO
:r .\V8-CommerceAnalyticsEvents.sql
GO
:r .\V9-CustomerGroups.sql
GO
:r .\V10-WhatsAppCommerceCheckout.sql
GO
:r .\V11-WhatsAppCommerceDeliveryTracking.sql
GO
:r .\V12-HierarchicalFeatureManagement.sql
GO
:r .\V13-LoyaltyCoins.sql
GO
:r .\V14-WhatsAppOption3TenantConnections.sql
GO
:r .\V15-CustomerReferralRewards.sql
GO
:r .\V16-PurchaseCoinExpiry.sql
:r .\V17-DeliveryManagement.sql
GO
:r .\V18-WhatsAppContacts.sql
GO
:r .\V19-ProductImageStorageProviders.sql
GO
:r .\V20-DemoRequests.sql
GO
:r .\V21-POSMobileBarcodeScanner.sql
