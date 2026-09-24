export interface Store {
  storeKey: string;
  name: string;
  tagline: string;
  logoUrl?: string;
  accentColor: string;
  deliveryMessage: string;
}

export interface Category {
  id: string;
  name: string;
  emoji?: string;
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
  unitLabel: string;
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
