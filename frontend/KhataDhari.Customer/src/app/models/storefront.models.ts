export interface Store {
  storeKey: string;
  name: string;
  tagline: string;
  logoUrl?: string;
  allCategoryImageUrl?: string;
  accentColor: string;
  deliveryMessage: string;
  freeDeliveryThreshold?: number;
  showProductRatings: boolean;
  showProductReviews: boolean;
  banners: StoreBanner[];
  paymentMethods: StorePaymentMethod[];
}

export interface StoreBanner { slot: 'PRIMARY' | 'SECONDARY'; imageUrl: string; title?: string; subtitle?: string; targetUrl?: string; displayOrder: number; promotionId?: string | null; }
export interface StorePaymentMethod { code: 'UPI' | 'NET_BANKING' | 'COD'; provider: 'RAZORPAY' | 'COD'; label: string; description: string; isDefault: boolean; usesHostedPaymentPage: boolean; }

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
  packSize?: string;
  averageRating?: number;
  ratingCount?: number;
  badge?: string; returnPolicyMode?: "INHERIT_DEFAULT" | "CUSTOM" | "NON_RETURNABLE"; returnWindowDays?: number; isReturnable?: boolean;
}

export interface ProductPage { items: Product[]; pageNumber: number; pageSize: number; totalCount: number; hasMore: boolean; }
export function discountPercent(product: Product): number | null { const original = product.compareAtPrice; if (!original || original <= 0 || product.sellingPrice >= original) return null; return Math.round(((original - product.sellingPrice) / original) * 100); }

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
  status: string;
  total: number;
  deliveryStatus?: string;
  trackingNumber?: string;
  paymentMethod: string;
  paymentStatus: string;
  itemCount: number;
  lines?: CustomerOrderLine[];
  timeline?: OrderMilestone[];
  deliveryCharge?: number;
  promotionDiscount?: number;
  promotionName?: string;
  merchandiseAmount?: number;
  merchandiseTaxAmount?: number;
  merchandiseSubtotal?: number;
  cancellation?: CustomerOrderCancellation;
}

export interface CustomerOrderCancellation { canRequest:boolean;unavailableReason?:string;requestStatus?:string;reason?:string;refundableAmount:number;refundStatus:string;refundAmount:number;refundedAt?:string;refundNeedsReconciliation?:boolean;decisionNote?:string; }
export interface CustomerOrderLine { productId:string;productName:string;packSize?:string;quantity:number;lineTotal:number; }
export interface OrderMilestone { code:string;label:string;state:string;occurredAt?:string; }
export interface CartQuote { eligibleAmount:number;freeDeliveryThreshold?:number;remainingAmount:number;progressPercent:number;isFreeDeliveryUnlocked:boolean;isDeliveryEnabled:boolean;isPincodeServiceable:boolean;merchandiseAmount:number;merchandiseTaxAmount:number;merchandiseSubtotal:number;standardDeliveryCharge:number;freeDeliveryEnabled:boolean;deliveryCharge:number;promotionDiscount:number;promotionName?:string;promotionId?:string;finalPayableAmount:number; promotionCode?: string; }
export interface CheckoutCustomer {
  customerName: string;
  mobile: string;
  email?: string;
  deliveryAddress: string;
  pincode: string;
  promoCode?: string;
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
  customerSessionToken?: string;
  paymentMethod: string;
}

export interface ProductReview { reviewId:string; reviewerName:string; rating:number; reviewText:string; createdAt:string; updatedAt:string; isOwn:boolean; }
export interface ProductReviewSummary { averageRating?:number; ratingCount:number; reviews:ProductReview[]; }
export interface CustomerAddress { addressId: string; recipientName: string; mobile: string; addressLine1: string; addressLine2?: string; landmark?: string; city: string; state: string; pincode: string; addressType: 'Home'|'Work'|'Other'; isDefault: boolean; }
export type CustomerAddressInput = Omit<CustomerAddress, 'addressId'>;

export interface StorefrontOffer { offerId: string; title: string; bannerImageUrl?: string; shortDescription?: string; detailedDescription?: string; promoCode?: string; benefitDescription: string; validFrom?: string; validUntil?: string; minimumOrderAmount: number; eligibleItemsDescription?: string; maximumDiscount?: number; usageLimitPerCustomer?: number; termsAndConditions?: string; ctaLabel?: string; status: 'ACTIVE' | 'UPCOMING' | 'EXPIRED'; }
