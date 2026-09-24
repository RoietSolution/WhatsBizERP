import { Inject, Injectable } from '@angular/core';
import { CartLine, Category, CheckoutCustomer, CheckoutResult, CustomerOrder, Product, Store } from '../models/storefront.models';
import { STOREFRONT_DATA_PROVIDER, StorefrontDataProvider } from './storefront-data.provider';

@Injectable({ providedIn: 'root' })
export class StorefrontDataService {
  constructor(@Inject(STOREFRONT_DATA_PROVIDER) private readonly provider: StorefrontDataProvider) {}

  getStore(storeKey: string): Promise<Store | null> { return this.provider.getStore(storeKey); }
  getCategories(storeKey: string): Promise<Category[]> { return this.provider.getCategories(storeKey); }
  getProducts(storeKey: string): Promise<Product[]> { return this.provider.getProducts(storeKey); }
  getProduct(storeKey: string, id: string): Promise<Product | null> { return this.provider.getProduct(storeKey, id); }
  getOrders(storeKey: string): Promise<CustomerOrder[]> { return this.provider.getOrders(storeKey); }
  checkoutWithRazorpay(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string): Promise<CheckoutResult> {
    return this.provider.checkoutWithRazorpay(storeKey, customer, lines, idempotencyKey);
  }
}
