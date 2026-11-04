import { InjectionToken } from '@angular/core';
import { AvailabilityAnnouncement, AvailabilityRequest, CartLine, CartQuote, Category, CheckoutCustomer, CheckoutResult, CustomerAddress, CustomerAddressInput, CustomerOrder, Product, ProductPage, ProductReview, ProductReviewSummary, Store, StorefrontOffer, StorefrontPaymentReturn, StorefrontReturnRequest } from '../models/storefront.models';

export interface StorefrontDataProvider {
  getStore(storeKey: string): Promise<Store | null>;
  getCategories(storeKey: string): Promise<Category[]>;
  getProducts(storeKey: string, page?: number, pageSize?: number, categoryId?: string): Promise<ProductPage>;
  getAvailabilityAnnouncements(storeKey:string): Promise<AvailabilityAnnouncement[]>;
  getProduct(storeKey: string, productId: string): Promise<Product | null>;
  getOffer(storeKey: string, offerId: string): Promise<StorefrontOffer | null>;
  getOrders(storeKey: string): Promise<CustomerOrder[]>;
  getOrder(storeKey: string, orderId: string): Promise<CustomerOrder | null>;
  requestCancellation(storeKey: string, orderId: string, reason: string): Promise<void>;
  requestReturn(storeKey:string, orderId:string, invoiceItemId:string, quantity:number, reason:string, explanation?:string): Promise<StorefrontReturnRequest>;
  quote(storeKey: string, lines: readonly CartLine[], pincode: string, promoCode?: string): Promise<CartQuote | null>;
  previewQuote(storeKey: string, lines: readonly CartLine[], promoCode?: string): Promise<CartQuote | null>;
  checkDelivery(storeKey: string, pincode: string): Promise<{deliveryEnabled: boolean; isPincodeServiceable: boolean}>;
  getReviews(storeKey: string, productId: string): Promise<ProductReviewSummary>;
  saveReview(storeKey: string, productId: string, rating: number, reviewText: string): Promise<ProductReview>;
  getAddresses(storeKey: string): Promise<CustomerAddress[]>;
  saveAddress(storeKey: string, address: CustomerAddressInput, addressId?: string): Promise<CustomerAddress>;
  deleteAddress(storeKey: string, addressId: string): Promise<void>;
  setDefaultAddress(storeKey: string, addressId: string): Promise<void>;
  uploadProfileImage(storeKey: string, file: File): Promise<void>;
  removeProfileImage(storeKey: string): Promise<void>;
  checkout(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string, paymentMethod: string): Promise<CheckoutResult>;
  getPaymentReturn(storeKey: string, orderId: string): Promise<StorefrontPaymentReturn | null>;
  retryPayment(storeKey:string,orderId:string):Promise<CheckoutResult|null>;
  requestAvailability(storeKey:string, productId:string, mobile?:string, consentGiven?:boolean): Promise<AvailabilityRequest>;
  getAvailability(storeKey:string, productId:string): Promise<AvailabilityRequest|null>;
  cancelAvailability(storeKey:string, productId:string): Promise<void>;
  requestMissingProduct(storeKey:string, keyword:string, mobile?:string, remarks?:string): Promise<void>;
}

export const STOREFRONT_DATA_PROVIDER = new InjectionToken<StorefrontDataProvider>('STOREFRONT_DATA_PROVIDER');
