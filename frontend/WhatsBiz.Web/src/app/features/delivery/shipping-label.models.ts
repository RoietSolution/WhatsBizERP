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
  paymentCollected?: boolean;
  collectedAmount?: number;
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

export function shippingLabelPaymentIndicator(order: StorefrontShippingLabel): string {
  const currency = (value: number) => new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value);
  if ((order.paymentMethod || '').toUpperCase() === 'COD') {
    if (order.paymentCollected && order.collectedAmount != null) return `COD PAID — ${currency(order.collectedAmount)} · Do not collect payment again`;
    return order.paymentDue ? `COD TO COLLECT — ${currency(order.dueAmount)}` : 'COD collection status unavailable';
  }
  return ['PAID', 'CAPTURED', 'SUCCESS', 'COMPLETED'].includes((order.paymentStatus || '').toUpperCase())
    ? 'PREPAID — Payment received'
    : 'Payment pending — do not collect';
}
