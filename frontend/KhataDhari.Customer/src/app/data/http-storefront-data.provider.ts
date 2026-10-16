import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AvailabilityRequest, CartLine, CartQuote, Category, CheckoutCustomer, CheckoutResult, CustomerAddress, CustomerAddressInput, CustomerOrder, Product, ProductPage, ProductReview, ProductReviewSummary, Store, StorefrontOffer, StorefrontPaymentReturn } from '../models/storefront.models';
import { StorefrontDataProvider } from './storefront-data.provider';
import { CustomerSessionService } from '../customer-session.service';

interface ApiStore { storeKey: string; name: string; tagline?: string; logoUrl?: string | null; allCategoryImageUrl?: string | null; accentColor?: string; deliveryMessage?: string; freeDeliveryThreshold?:number|null; banners?: Array<{slot:'PRIMARY'|'SECONDARY';imageUrl:string;title?:string;subtitle?:string;targetUrl?:string;displayOrder:number;promotionId?:string|null}>; showProductRatings?:boolean; showProductReviews?:boolean; paymentMethods?: Array<{code:'UPI'|'NET_BANKING'|'COD';provider:'RAZORPAY'|'COD';label:string;description:string;isDefault:boolean;usesHostedPaymentPage:boolean}>; customerRewardsEnabled?: boolean; }
interface ApiCategory { id: string; name: string; imageUrl?: string | null; }
interface ApiOffer extends StorefrontOffer {}
interface ApiProductPage { items: ApiProduct[]; pageNumber: number; pageSize: number; totalCount: number; hasMore: boolean; }
interface ApiProductImage { id: string; url: string; thumbnailUrl: string; isPrimary: boolean; }
interface ApiProduct { id: string; categoryId: string; name: string; description?: string | null; longDescription?: string | null; specifications?: Array<{name:string;value:string}>; imageUrl?: string | null; images?: ApiProductImage[] | null; sellingPrice: number; compareAtPrice?: number | null; availability: 'IN_STOCK' | 'OUT_OF_STOCK'; packSize?: string | null; averageRating?: number | null; ratingCount?: number; returnPolicyMode?: "INHERIT_DEFAULT" | "CUSTOM" | "NON_RETURNABLE"; returnWindowDays?: number | null; isReturnable?: boolean; returnPolicyDescription?: string | null; }

@Injectable({ providedIn: 'root' })
export class HttpStorefrontDataProvider implements StorefrontDataProvider {
  constructor(private readonly http: HttpClient, private readonly session: CustomerSessionService) {}

  async getStore(storeKey: string): Promise<Store | null> {
    let store: ApiStore;
    try { store = await firstValueFrom(this.http.get<ApiStore>(this.url(storeKey))); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 404) return null; throw error; }
    return { storeKey: store.storeKey, name: store.name, tagline: store.tagline ?? 'Good things, close to home.', logoUrl: this.asset(store.logoUrl), allCategoryImageUrl:this.asset(store.allCategoryImageUrl), accentColor: store.accentColor ?? '#145c43', freeDeliveryThreshold:store.freeDeliveryThreshold??undefined, deliveryMessage: store.deliveryMessage ?? 'Fresh picks, close to home.', showProductRatings:store.showProductRatings??true, showProductReviews:store.showProductReviews??true, banners:(store.banners??[]).map(b=>({...b,imageUrl:this.asset(b.imageUrl)!})),paymentMethods:store.paymentMethods??[],customerRewardsEnabled:store.customerRewardsEnabled===true };
  }

  async getCategories(storeKey: string): Promise<Category[]> {
    const categories = await firstValueFrom(this.http.get<ApiCategory[]>(`${this.url(storeKey)}/categories`));
    return categories.map((category) => ({ id: category.id, name: category.name, imageUrl:this.asset(category.imageUrl) }));
  }

  async getProducts(storeKey: string, page = 1, pageSize = 24): Promise<ProductPage> { const result = await firstValueFrom(this.http.get<ApiProductPage>(`${this.url(storeKey)}/products`, { params: { page, pageSize } })); return { ...result, items: result.items.map((product) => this.mapProduct(product)) }; }

  async getProduct(storeKey: string, productId: string): Promise<Product | null> {
    let product: ApiProduct;
    try { product = await firstValueFrom(this.http.get<ApiProduct>(`${this.url(storeKey)}/products/${encodeURIComponent(productId)}`)); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 404) return null; throw error; }
    return this.mapProduct(product);
  }

  async getOffer(storeKey:string,offerId:string):Promise<StorefrontOffer|null>{try{const offer=await firstValueFrom(this.http.get<ApiOffer>(this.url(storeKey)+'/offers/'+encodeURIComponent(offerId)));return {...offer,bannerImageUrl:this.asset(offer.bannerImageUrl)};}catch(error){if(error instanceof HttpErrorResponse&&error.status===404)return null;throw error;}}

  async getOrders(storeKey: string): Promise<CustomerOrder[]> {
    const token = this.session.token(storeKey);
    if (!token) return [];
    try { return await firstValueFrom(this.http.get<CustomerOrder[]>(this.url(storeKey) + '/orders', { headers: { 'X-Customer-Session': token } })); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 401) this.session.clear(storeKey); return []; }
  }

  async getOrder(storeKey:string,orderId:string):Promise<CustomerOrder|null>{const token=this.session.token(storeKey);if(!token)return null;try{return await firstValueFrom(this.http.get<CustomerOrder>(`${this.url(storeKey)}/orders/${encodeURIComponent(orderId)}`,{headers:{'X-Customer-Session':token}}));}catch(error){if(error instanceof HttpErrorResponse&&error.status===401)this.session.clear(storeKey);return null;}}
  async requestCancellation(storeKey:string,orderId:string,reason:string):Promise<void>{const token=this.session.token(storeKey);if(!token)throw new Error('Sign in to request cancellation.');await firstValueFrom(this.http.post(`${this.url(storeKey)}/orders/${encodeURIComponent(orderId)}/cancellation-request`, {reason},{headers:{'X-Customer-Session':token}}));}
  async requestReturn(storeKey:string,orderId:string,invoiceItemId:string,quantity:number,reason:string,explanation?:string):Promise<import('../models/storefront.models').StorefrontReturnRequest>{const token=this.session.token(storeKey);if(!token)throw new Error('Sign in to request a return.');return await firstValueFrom(this.http.post<import('../models/storefront.models').StorefrontReturnRequest>(`${this.url(storeKey)}/orders/${encodeURIComponent(orderId)}/items/${encodeURIComponent(invoiceItemId)}/return-request`,{quantity,reason,explanation:explanation||null},{headers:{'X-Customer-Session':token}}));}
  async checkDelivery(storeKey:string,pincode:string):Promise<{deliveryEnabled:boolean;isPincodeServiceable:boolean}>{return await firstValueFrom(this.http.get<{deliveryEnabled:boolean;isPincodeServiceable:boolean}>(`${this.url(storeKey)}/delivery/availability`,{params:{pincode}}));}
  async quote(storeKey:string,lines:readonly CartLine[],pincode:string,promoCode?:string):Promise<CartQuote|null>{if(!lines.length)return null;const token=this.session.token(storeKey);return await firstValueFrom(this.http.post<CartQuote>(`${this.url(storeKey)}/cart/quote`,{pincode,promoCode:promoCode||null,items:lines.map(x=>({productId:x.product.id,quantity:x.quantity}))},{headers:token?{'X-Customer-Session':token}:{}}));}
  async getReviews(storeKey:string,productId:string):Promise<ProductReviewSummary>{const token=this.session.token(storeKey);return await firstValueFrom(this.http.get<ProductReviewSummary>(`${this.url(storeKey)}/products/${encodeURIComponent(productId)}/reviews`,{headers:token?{'X-Customer-Session':token}:{}}));}
  async saveReview(storeKey:string,productId:string,rating:number,reviewText:string):Promise<ProductReview>{return await firstValueFrom(this.http.put<ProductReview>(`${this.url(storeKey)}/products/${encodeURIComponent(productId)}/reviews/mine`,{rating,reviewText},{headers:{'X-Customer-Session':this.session.token(storeKey)}}));}

  async getAddresses(storeKey:string):Promise<CustomerAddress[]>{return await firstValueFrom(this.http.get<CustomerAddress[]>(this.url(storeKey)+'/addresses',{headers:{'X-Customer-Session':this.session.token(storeKey)??''}}));}
  async saveAddress(storeKey:string,address:CustomerAddressInput,addressId?:string):Promise<CustomerAddress>{const url=this.url(storeKey)+'/addresses'+(addressId?'/'+encodeURIComponent(addressId):'');const headers={'X-Customer-Session':this.session.token(storeKey)??''};return await firstValueFrom(addressId?this.http.put<CustomerAddress>(url,address,{headers}):this.http.post<CustomerAddress>(url,address,{headers}));}
  async deleteAddress(storeKey:string,addressId:string):Promise<void>{await firstValueFrom(this.http.delete(this.url(storeKey)+'/addresses/'+encodeURIComponent(addressId),{headers:{'X-Customer-Session':this.session.token(storeKey)??''}}));}
  async setDefaultAddress(storeKey:string,addressId:string):Promise<void>{await firstValueFrom(this.http.post(this.url(storeKey)+'/addresses/'+encodeURIComponent(addressId)+'/default',{}, {headers:{'X-Customer-Session':this.session.token(storeKey)??''}}));}
  async uploadProfileImage(storeKey:string,file:File):Promise<void>{const body=new FormData();body.append('file',file,file.name);await firstValueFrom(this.http.post(this.url(storeKey)+'/session/profile-image',body,{headers:{'X-Customer-Session':this.session.token(storeKey)??''}}));await this.session.refresh(storeKey);}
  async removeProfileImage(storeKey:string):Promise<void>{await firstValueFrom(this.http.delete(this.url(storeKey)+'/session/profile-image',{headers:{'X-Customer-Session':this.session.token(storeKey)??''}}));await this.session.refresh(storeKey);}

  async checkout(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string, paymentMethod: string): Promise<CheckoutResult> {
    return await firstValueFrom(this.http.post<CheckoutResult>(`${this.url(storeKey)}/checkout`, {
      ...customer,
      email: customer.email || null,
      paymentMethod,
      items: lines.map((line) => ({ productId: line.product.id, quantity: line.quantity })),
    }, { headers: { 'Idempotency-Key': idempotencyKey, ...(this.session.token(storeKey) ? { 'X-Customer-Session': this.session.token(storeKey) } : {}) } }));
  }
  async getPaymentReturn(storeKey:string,orderId:string):Promise<StorefrontPaymentReturn|null>{const token=this.session.token(storeKey);if(!token)return null;try{return await firstValueFrom(this.http.get<StorefrontPaymentReturn>(`${this.url(storeKey)}/orders/${encodeURIComponent(orderId)}/payment-status`,{headers:{'X-Customer-Session':token}}));}catch(error){if(error instanceof HttpErrorResponse&&(error.status===401||error.status===404))return null;throw error;}}
  async retryPayment(storeKey:string,orderId:string):Promise<CheckoutResult|null>{const token=this.session.token(storeKey);if(!token)return null;return await firstValueFrom(this.http.post<CheckoutResult>(`${this.url(storeKey)}/orders/${encodeURIComponent(orderId)}/payment-retry`,{}, {headers:{'X-Customer-Session':token}}));}
  async requestAvailability(storeKey:string,productId:string,mobile?:string,consentGiven=false):Promise<AvailabilityRequest>{const token=this.session.token(storeKey);return await firstValueFrom(this.http.post<AvailabilityRequest>(`${this.url(storeKey)}/products/${productId}/availability-request`,{mobile:mobile||null,preferredChannel:'WHATSAPP',consentGiven},{headers:token?{'X-Customer-Session':token}:{}}));}
  async getAvailability(storeKey:string,productId:string):Promise<AvailabilityRequest|null>{const token=this.session.token(storeKey);if(!token)return null;try{return await firstValueFrom(this.http.get<AvailabilityRequest>(`${this.url(storeKey)}/products/${productId}/availability-request`,{headers:{'X-Customer-Session':token}}));}catch(error:any){if(error?.status===404)return null;throw error;}}
  async cancelAvailability(storeKey:string,productId:string):Promise<void>{const token=this.session.token(storeKey);await firstValueFrom(this.http.delete(`${this.url(storeKey)}/products/${productId}/availability-request`,{headers:token?{'X-Customer-Session':token}:{}}));}
  async requestMissingProduct(storeKey:string,keyword:string,mobile?:string,remarks?:string):Promise<void>{const token=this.session.token(storeKey);await firstValueFrom(this.http.post(`${this.url(storeKey)}/missing-product-requests`,{keyword,mobile:mobile||null,remarks:remarks||null,anonymousSessionId:null},{headers:token?{'X-Customer-Session':token}:{}}));}

  private mapProduct(product: ApiProduct): Product {
    return {
      id: product.id,
      categoryId: product.categoryId,
      name: product.name,
      description: product.description ?? '',
      longDescription: product.longDescription ?? undefined,
      specifications: product.specifications ?? [],
      imageUrl: product.imageUrl ? `${environment.apiBaseUrl}${product.imageUrl}` : '/images/product-placeholder.svg',
      images: (product.images ?? []).map(image => ({ id: image.id, url: `${environment.apiBaseUrl}${image.url}`, thumbnailUrl: `${environment.apiBaseUrl}${image.thumbnailUrl}`, isPrimary: image.isPrimary })),
      sellingPrice: product.sellingPrice,
      compareAtPrice: product.compareAtPrice ?? undefined,
      available: product.availability === 'IN_STOCK',
      packSize: product.packSize ?? undefined,
      averageRating: product.averageRating ?? undefined,
      ratingCount: product.ratingCount ?? 0, returnPolicyMode: product.returnPolicyMode, returnWindowDays: product.returnWindowDays ?? undefined, isReturnable: product.isReturnable, returnPolicyDescription: product.returnPolicyDescription ?? undefined,
    };
  }

  private url(storeKey: string): string { return `${environment.apiBaseUrl}/api/store/${encodeURIComponent(storeKey)}`; }
  private asset(path?:string|null):string|undefined{return path?`${environment.apiBaseUrl}${path}`:undefined;}
}
