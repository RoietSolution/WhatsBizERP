import { InjectionToken } from '@angular/core';
import { CartLine, Category, CheckoutCustomer, CheckoutResult, CustomerOrder, Product, Store } from '../models/storefront.models';

export interface StorefrontDataProvider {
  getStore(storeKey: string): Promise<Store | null>;
  getCategories(storeKey: string): Promise<Category[]>;
  getProducts(storeKey: string): Promise<Product[]>;
  getProduct(storeKey: string, productId: string): Promise<Product | null>;
  getOrders(storeKey: string): Promise<CustomerOrder[]>;
  checkout(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string, paymentProvider: string): Promise<CheckoutResult>;
}

export const STOREFRONT_DATA_PROVIDER = new InjectionToken<StorefrontDataProvider>('STOREFRONT_DATA_PROVIDER');
