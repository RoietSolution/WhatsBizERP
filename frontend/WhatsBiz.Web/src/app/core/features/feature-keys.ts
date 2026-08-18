export const FeatureKeys = {
  WhatsAppCommerce: 'WHATSAPP_COMMERCE',
  AdvancedWarehouse: 'ADVANCED_WAREHOUSE',
  AiAssistant: 'AI_ASSISTANT',
  Integrations: 'INTEGRATIONS',
} as const;
export type FeatureKey = (typeof FeatureKeys)[keyof typeof FeatureKeys];
