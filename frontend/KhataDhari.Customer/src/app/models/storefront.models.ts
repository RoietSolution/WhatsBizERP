export interface Store {
  storeKey: string;
  name: string;
  tagline: string;
  logoUrl?: string;
  accentColor: string;
  deliveryMessage: string;
  banners: StoreBanner[];
  paymentMethods: StorePaymentMethod[];
}

export interface StoreBanner { slot: 'PRIMARY' | 'SECONDARY'; imageUrl: string; title?: string; subtitle?: string; targetUrl?: string; displayOrder: number; }
export interface StorePaymentMethod { code: 'RAZORPAY' | 'DIRECT_UPI' | 'COD'; label: string; description: string; isDefault: boolean; usesHostedPaymentPage: boolean; }

export interface Category {
  id: string;
  name: string;
  emoji?: string;
  imageUrl?: string;
}

export interface Product {
  id: string;
  categoryId: string;
  name: string;
  description: string;
  imageUrl: string;
  sellingPrice: number;
  compareAtPrice?: number;
  available: boolean;
  unitLabel?: string;
  badge?: string;
}

export interface CartLine {
  product: Product;
  quantity: number;
}

export interface Cart {
  storeKey: string;
  lines: CartLine[];
  itemCount: number;
  total: number;
}

export interface CustomerOrder {
  id: string;
  orderNumber: string;
  placedAt: string;
  status: 'processing' | 'ready' | 'delivered' | 'cancelled';
  total: number;
  lines: CartLine[];
}

export interface CheckoutCustomer {
  customerName: string;
  mobile: string;
  email?: string;
  deliveryAddress: string;
}

export interface CheckoutResult {
  orderId: string;
  orderNumber: string;
  amount: number;
  currency: string;
  paymentId: string;
  checkoutUrl?: string;
  paymentProvider: string;
  paymentStatus: string;
  customerMessage?: string;
}
