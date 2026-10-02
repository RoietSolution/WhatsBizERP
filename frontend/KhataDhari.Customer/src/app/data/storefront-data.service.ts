import { Inject, Injectable } from '@angular/core';
import { CartLine, CartQuote, Category, CheckoutCustomer, CheckoutResult, CustomerAddress, CustomerAddressInput, CustomerOrder, Product, ProductPage, ProductReview, ProductReviewSummary, Store, StorefrontOffer } from '../models/storefront.models';
import { STOREFRONT_DATA_PROVIDER, StorefrontDataProvider } from './storefront-data.provider';

@Injectable({ providedIn: 'root' })
export class StorefrontDataService {
  constructor(@Inject(STOREFRONT_DATA_PROVIDER) private readonly provider: StorefrontDataProvider) {}

  getStore(storeKey: string): Promise<Store | null> { return this.provider.getStore(storeKey); }
  getCategories(storeKey: string): Promise<Category[]> { return this.provider.getCategories(storeKey); }
  getProducts(storeKey: string, page = 1, pageSize = 24): Promise<ProductPage> { return this.provider.getProducts(storeKey, page, pageSize); }
  getProduct(storeKey: string, id: string): Promise<Product | null> { return this.provider.getProduct(storeKey, id); }
  getOffer(storeKey: string, id: string): Promise<StorefrontOffer | null> { return this.provider.getOffer(storeKey, id); }
  getOrders(storeKey: string): Promise<CustomerOrder[]> { return this.provider.getOrders(storeKey); }
  getOrder(storeKey: string, id: string): Promise<CustomerOrder | null> { return this.provider.getOrder(storeKey, id); }
  requestCancellation(storeKey: string, orderId: string, reason: string): Promise<void> { return this.provider.requestCancellation(storeKey, orderId, reason); }
  quote(storeKey: string, lines: readonly CartLine[], pincode: string, promoCode?: string): Promise<CartQuote | null> { return this.provider.quote(storeKey, lines, pincode, promoCode); }
  getReviews(storeKey: string, productId: string): Promise<ProductReviewSummary> { return this.provider.getReviews(storeKey, productId); }
  saveReview(storeKey: string, productId: string, rating: number, reviewText: string): Promise<ProductReview> { return this.provider.saveReview(storeKey, productId, rating, reviewText); }
  getAddresses(storeKey: string): Promise<CustomerAddress[]> { return this.provider.getAddresses(storeKey); }
  saveAddress(storeKey: string, address: CustomerAddressInput, addressId?: string): Promise<CustomerAddress> { return this.provider.saveAddress(storeKey, address, addressId); }
  deleteAddress(storeKey: string, addressId: string): Promise<void> { return this.provider.deleteAddress(storeKey, addressId); }
  setDefaultAddress(storeKey: string, addressId: string): Promise<void> { return this.provider.setDefaultAddress(storeKey, addressId); }
  uploadProfileImage(storeKey: string, file: File): Promise<void> { return this.provider.uploadProfileImage(storeKey, file); }
  removeProfileImage(storeKey: string): Promise<void> { return this.provider.removeProfileImage(storeKey); }
  checkout(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string, paymentMethod: string): Promise<CheckoutResult> {
    return this.provider.checkout(storeKey, customer, lines, idempotencyKey, paymentMethod);
  }
}
