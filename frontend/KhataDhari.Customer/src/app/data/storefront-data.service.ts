import { Inject, Injectable } from '@angular/core';
import { AvailabilityAnnouncement, CartLine, CartQuote, Category, CheckoutCustomer, CheckoutResult, CustomerAddress, CustomerAddressInput, CustomerOrder, Product, ProductCatalogFilters, ProductPage, ProductReview, ProductReviewSummary, Store, StorefrontOffer, StorefrontReturnRequest } from '../models/storefront.models';
import { STOREFRONT_DATA_PROVIDER, StorefrontDataProvider } from './storefront-data.provider';

@Injectable({ providedIn: 'root' })
export class StorefrontDataService {
  private readonly storeRequests = new Map<string, Promise<Store | null>>();

  constructor(@Inject(STOREFRONT_DATA_PROVIDER) private readonly provider: StorefrontDataProvider) {}

  getStore(storeKey: string): Promise<Store | null> {
    const key = storeKey.trim().toLowerCase();
    const existing = this.storeRequests.get(key);
    if (existing) return existing;
    const request = this.provider.getStore(storeKey);
    this.storeRequests.set(key, request);
    void request.catch(() => {
      if (this.storeRequests.get(key) === request) this.storeRequests.delete(key);
    });
    return request;
  }
  getCategories(storeKey: string): Promise<Category[]> { return this.provider.getCategories(storeKey); }
  getProducts(storeKey: string, page = 1, pageSize = 24, categoryId?: string, filters?: ProductCatalogFilters): Promise<ProductPage> { return this.provider.getProducts(storeKey, page, pageSize, categoryId, filters); }
  getAvailabilityAnnouncements(storeKey:string): Promise<AvailabilityAnnouncement[]> { return this.provider.getAvailabilityAnnouncements(storeKey); }
  getProduct(storeKey: string, id: string): Promise<Product | null> { return this.provider.getProduct(storeKey, id); }
  getOffer(storeKey: string, id: string): Promise<StorefrontOffer | null> { return this.provider.getOffer(storeKey, id); }
  getOrders(storeKey: string): Promise<CustomerOrder[]> { return this.provider.getOrders(storeKey); }
  getOrder(storeKey: string, id: string): Promise<CustomerOrder | null> { return this.provider.getOrder(storeKey, id); }
  requestCancellation(storeKey: string, orderId: string, reason: string): Promise<void> { return this.provider.requestCancellation(storeKey, orderId, reason); }
  requestReturn(storeKey:string,orderId:string,invoiceItemId:string,quantity:number,reason:string,explanation?:string):Promise<StorefrontReturnRequest>{return this.provider.requestReturn(storeKey,orderId,invoiceItemId,quantity,reason,explanation);}
  quote(storeKey: string, lines: readonly CartLine[], pincode: string, promoCode?: string): Promise<CartQuote | null> { return this.provider.quote(storeKey, lines, pincode, promoCode); }
  previewQuote(storeKey: string, lines: readonly CartLine[], promoCode?: string): Promise<CartQuote | null> { return this.provider.previewQuote(storeKey, lines, promoCode); }
  checkDelivery(storeKey: string, pincode: string): Promise<{deliveryEnabled: boolean; isPincodeServiceable: boolean}> { return this.provider.checkDelivery(storeKey, pincode); }
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
  getPaymentReturn(storeKey:string,orderId:string):Promise<import('../models/storefront.models').StorefrontPaymentReturn|null>{return this.provider.getPaymentReturn(storeKey,orderId);}
  retryPayment(storeKey:string,orderId:string):Promise<CheckoutResult|null>{return this.provider.retryPayment(storeKey,orderId);}
  requestAvailability(storeKey:string,productId:string,mobile?:string,consentGiven=false){return this.provider.requestAvailability(storeKey,productId,mobile,consentGiven);}
  getAvailability(storeKey:string,productId:string){return this.provider.getAvailability(storeKey,productId);}
  cancelAvailability(storeKey:string,productId:string){return this.provider.cancelAvailability(storeKey,productId);}
  requestMissingProduct(storeKey:string,keyword:string,mobile?:string,remarks?:string){return this.provider.requestMissingProduct(storeKey,keyword,mobile,remarks);}
}
