import { InjectionToken } from '@angular/core';
import { CartLine, CartQuote, Category, CheckoutCustomer, CheckoutResult, CustomerOrder, Product, ProductReview, ProductReviewSummary, Store, StorefrontOffer } from '../models/storefront.models';

export interface StorefrontDataProvider {
  getStore(storeKey: string): Promise<Store | null>;
  getCategories(storeKey: string): Promise<Category[]>;
  getProducts(storeKey: string): Promise<Product[]>;
  getProduct(storeKey: string, productId: string): Promise<Product | null>;
  getOffer(storeKey: string, offerId: string): Promise<StorefrontOffer | null>;
  getOrders(storeKey: string): Promise<CustomerOrder[]>;
  getOrder(storeKey: string, orderId: string): Promise<CustomerOrder | null>;
  requestCancellation(storeKey: string, orderId: string, reason: string): Promise<void>;
  quote(storeKey: string, lines: readonly CartLine[], pincode: string): Promise<CartQuote | null>;
  getReviews(storeKey: string, productId: string): Promise<ProductReviewSummary>;
  saveReview(storeKey: string, productId: string, rating: number, reviewText: string): Promise<ProductReview>;
  checkout(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string, paymentMethod: string): Promise<CheckoutResult>;
}

export const STOREFRONT_DATA_PROVIDER = new InjectionToken<StorefrontDataProvider>('STOREFRONT_DATA_PROVIDER');
