export type ShippingLabelFormat = 'COURIER_4X6' | 'A5' | 'A4_SINGLE' | 'A4_4UP' | 'A4_DOUBLE' | 'THERMAL_80' | 'THERMAL_58';
export interface StorefrontShippingLabelItem { productName: string; sku?: string; variant?: string; quantity: number; }

export interface StorefrontShippingLabel {
  id: string;
  orderNumber: string;
  orderDate: string;
  customerName: string;
  customerMobile?: string;
  deliveryAddress: string;
  deliveryPincode?: string;
  paymentMethod: string;
  paymentStatus: string;
  paymentDue: boolean;
  dueAmount: number;
  storeName: string;
  senderAddress: string;
  senderPhone?: string;
  senderEmail?: string;
  fulfillmentMethod?: string;
  courierName?: string;
  trackingNumber?: string;
  deliveryAgentName?: string;
  senderLogoUrl?: string;
  retailerDisplayName?: string;
  deliveryLocality?: string;
  deliveryCity?: string;
  deliveryDistrict?: string;
  deliveryState?: string;
  qrCodeUrl?: string;
  barcodeUrl?: string;
  items?: StorefrontShippingLabelItem[];
  showProductDetails?: boolean;
}
