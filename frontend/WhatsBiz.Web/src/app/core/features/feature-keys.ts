export const FeatureKeys = {
  V1: 'V1', V2: 'V2', Dashboard: 'DASHBOARD', Pos: 'POS', Products: 'PRODUCTS', Customers: 'CUSTOMERS',
  Purchase: 'PURCHASE', Inventory: 'INVENTORY', Finance: 'FINANCE', Reports: 'REPORTS', UsersRoles: 'USERS_ROLES',
  Suppliers: 'SUPPLIERS', Warehouses: 'WAREHOUSES', Printing: 'PRINTING', Gst: 'GST', Analytics: 'ANALYTICS', Administration: 'ADMINISTRATION',
  WhatsAppCommerce: 'WHATSAPP_COMMERCE',
  WhatsAppConfiguration: 'WHATSAPP_CONFIGURATION', WhatsAppCommerceDemo: 'WHATSAPP_COMMERCE_DEMO',
  CommerceProductSearch: 'COMMERCE_PRODUCT_SEARCH', CommerceCollections: 'COMMERCE_COLLECTIONS', CommerceOrders: 'COMMERCE_ORDERS',
  CommerceAnalytics: 'COMMERCE_ANALYTICS', MetaWhatsAppIntegration: 'META_WHATSAPP_INTEGRATION', WebhookDiagnostics: 'WEBHOOK_DIAGNOSTICS',
  AdvancedWarehouse: 'ADVANCED_WAREHOUSE',
  AiAssistant: 'AI_ASSISTANT',
  Integrations: 'INTEGRATIONS',
  DeliveryManagement: 'DELIVERY_MANAGEMENT',
} as const;
export type FeatureKey = (typeof FeatureKeys)[keyof typeof FeatureKeys];
