import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AvailabilityAnnouncement, AvailabilityRequest, CartLine, CartQuote, Category, CheckoutCustomer, CheckoutResult, CustomerAddress, CustomerAddressInput, CustomerOrder, Product, ProductCatalogFilters, ProductPage, ProductReview, ProductReviewSummary, Store, StorefrontOffer, StorefrontPaymentReturn } from '../models/storefront.models';
import { StorefrontDataProvider } from './storefront-data.provider';
import { CustomerSessionService } from '../customer-session.service';

interface ApiStore { storeKey: string; name: string; tagline?: string; logoUrl?: string | null; allCategoryImageUrl?: string | null; featuredCategories?: ApiCategory[]; accentColor?: string; deliveryMessage?: string; freeDeliveryThreshold?:number|null; freeDeliveryEnabled?:boolean; defaultReturnWindowDays?:number; banners?: Array<{slot:'PRIMARY'|'SECONDARY';imageUrl:string;title?:string;subtitle?:string;targetUrl?:string;displayOrder:number;promotionId?:string|null}>; showProductRatings?:boolean; showProductReviews?:boolean; paymentMethods?: Array<{code:'UPI'|'NET_BANKING'|'COD';provider:'RAZORPAY'|'COD';label:string;description:string;isDefault:boolean;usesHostedPaymentPage:boolean}>; customerRewardsEnabled?: boolean; orderContactNumber?: string | null; }
interface ApiCategory { id: string; name: string; imageUrl?: string | null; parentId?: string | null; }
interface ApiOffer extends StorefrontOffer {}
interface ApiProductPage { items: ApiProduct[]; pageNumber: number; pageSize: number; totalCount: number; hasMore: boolean; minimumPrice?: number | null; maximumPrice?: number | null; supportsNewestSort?: boolean; }
interface ApiProductImage { id: string; url: string; thumbnailUrl: string; isPrimary: boolean; width?: number | null; height?: number | null; thumbnailWidth?: number | null; thumbnailHeight?: number | null; }
interface ApiProduct { id: string; categoryId: string; name: string; description?: string | null; longDescription?: string | null; specifications?: Array<{name:string;value:string}>; imageUrl?: string | null; images?: ApiProductImage[] | null; sellingPrice: number; compareAtPrice?: number | null; gstPercentage?: number; gstPricingType?: 'INCLUSIVE' | 'EXCLUSIVE'; availability: 'ORDERING_OPEN'|'ORDERING_CLOSED'|'AVAILABLE_NOW'|'AVAILABLE_LATER'|'UNAVAILABLE_TODAY'|'OUT_OF_STOCK'|'INACTIVE'|'IN_STOCK'; availabilityMessage?: string|null; availabilityWindow?: string|null; availabilityDeliveryWindow?: string|null; availabilityNextTransition?: string|null; canOrderNow?: boolean; availabilityReason?: string|null; nextOrderOpenAtUtc?: string|null; nextOrderCloseAtUtc?: string|null; availabilityServerNowUtc?: string|null; availabilityTimeZoneId?: string|null; isPurchasable?:boolean; partnerSellerId?:string|null; sellerName?:string|null; notPurchasableReason?:string|null; packSize?: string | null; averageRating?: number | null; ratingCount?: number; returnPolicyMode?: "INHERIT_DEFAULT" | "CUSTOM" | "NON_RETURNABLE"; returnWindowDays?: number | null; isReturnable?: boolean; returnPolicyDescription?: string | null; }

@Injectable({ providedIn: 'root' })
export class HttpStorefrontDataProvider implements StorefrontDataProvider {
  constructor(private readonly http: HttpClient, private readonly session: CustomerSessionService) {}

  async getStore(storeKey: string): Promise<Store | null> {
    let store: ApiStore;
    try { store = await firstValueFrom(this.http.get<ApiStore>(this.url(storeKey))); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 404) return null; throw error; }
    return { storeKey: store.storeKey, name: store.name, tagline: store.tagline ?? 'Good things, close to home.', logoUrl: this.asset(store.logoUrl), allCategoryImageUrl:this.asset(store.allCategoryImageUrl), featuredCategories:(store.featuredCategories??[]).map(category=>({id:category.id,name:category.name,parentId:category.parentId??null,imageUrl:this.asset(category.imageUrl)})), accentColor: store.accentColor ?? '#145c43', freeDeliveryThreshold:store.freeDeliveryThreshold??undefined, freeDeliveryEnabled:store.freeDeliveryEnabled===true, defaultReturnWindowDays:Number.isInteger(store.defaultReturnWindowDays)&&store.defaultReturnWindowDays!>=0?store.defaultReturnWindowDays:undefined, deliveryMessage: store.deliveryMessage ?? 'Fresh picks, close to home.', showProductRatings:store.showProductRatings??true, showProductReviews:store.showProductReviews??true, banners:(store.banners??[]).map(b=>({...b,imageUrl:this.asset(b.imageUrl)!})),paymentMethods:store.paymentMethods??[],customerRewardsEnabled:store.customerRewardsEnabled===true,orderContactNumber:store.orderContactNumber??undefined };
  }

  async getCategories(storeKey: string): Promise<Category[]> {
    const categories = await firstValueFrom(this.http.get<ApiCategory[]>(`${this.url(storeKey)}/categories`));
    return categories.map((category) => ({ id: category.id, name: category.name, parentId: category.parentId ?? null, imageUrl:this.asset(category.imageUrl) }));
  }

  async getProducts(storeKey: string, page = 1, pageSize = 24, categoryId?: string, filters?: ProductCatalogFilters): Promise<ProductPage> {
    const result = await firstValueFrom(this.http.get<ApiProductPage>(`${this.url(storeKey)}/products`, { params: {
      page, pageSize, ...(categoryId ? { categoryId } : {}),
      ...(filters?.minPrice !== undefined ? { minPrice: filters.minPrice } : {}),
      ...(filters?.maxPrice !== undefined ? { maxPrice: filters.maxPrice } : {}),
      ...(filters?.inStockOnly ? { inStockOnly: true } : {}),
      ...(filters?.sortBy ? { sortBy: filters.sortBy } : {})
    } }));
    return { ...result, items: result.items.map((product) => this.mapProduct(product)) };
  }
  async getAvailabilityAnnouncements(storeKey:string):Promise<AvailabilityAnnouncement[]>{return await firstValueFrom(this.http.get<AvailabilityAnnouncement[]>(`${this.url(storeKey)}/availability-announcements`));}

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
  async quote(storeKey:string,lines:readonly CartLine[],pincode:string,promoCode?:string):Promise<CartQuote|null>{if(!lines.length)return null;const token=this.session.token(storeKey);const normalizedPincode=pincode?.trim()||'';const endpoint=normalizedPincode?'/cart/quote':'/cart/preview';return await firstValueFrom(this.http.post<CartQuote>(`${this.url(storeKey)}${endpoint}`,{pincode:normalizedPincode||null,promoCode:promoCode||null,items:lines.map(x=>({productId:x.product.id,quantity:x.quantity}))},{headers:token?{'X-Customer-Session':token}:{}}));}
  async previewQuote(storeKey:string,lines:readonly CartLine[],promoCode?:string):Promise<CartQuote|null>{if(!lines.length)return null;const token=this.session.token(storeKey);return await firstValueFrom(this.http.post<CartQuote>(`${this.url(storeKey)}/cart/preview`,{pincode:null,promoCode:promoCode||null,items:lines.map(x=>({productId:x.product.id,quantity:x.quantity}))},{headers:token?{'X-Customer-Session':token}:{}}));}
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
      imageUrl: this.asset(product.imageUrl) ?? '/images/product-placeholder.svg',
      images: (product.images ?? []).map(image => ({ id: image.id, url: this.asset(image.url)!, thumbnailUrl: this.asset(image.thumbnailUrl)!, isPrimary: image.isPrimary, width: image.width, height: image.height, thumbnailWidth: image.thumbnailWidth, thumbnailHeight: image.thumbnailHeight })),
      sellingPrice: product.sellingPrice,
      gstPercentage: product.gstPercentage ?? 0,
      gstPricingType: product.gstPricingType ?? 'EXCLUSIVE',
      compareAtPrice: product.compareAtPrice ?? undefined,
      available: product.isPurchasable !== false && product.canOrderNow !== false && (product.availability === 'ORDERING_OPEN' || product.availability === 'AVAILABLE_NOW' || product.availability === 'IN_STOCK'),
      isPurchasable: product.isPurchasable ?? true,
      partnerSellerId: product.partnerSellerId ?? undefined,
      sellerName: product.sellerName ?? undefined,
      notPurchasableReason: product.notPurchasableReason ?? undefined,
      availability: product.availability === 'IN_STOCK' ? 'AVAILABLE_NOW' : product.availability === 'ORDERING_CLOSED' ? 'AVAILABLE_LATER' : product.availability,
      availabilityMessage: product.availabilityMessage ?? undefined,
      availabilityWindow: product.availabilityWindow ?? undefined,
      availabilityDeliveryWindow: product.availabilityDeliveryWindow ?? undefined,
      availabilityNextTransition: product.availabilityNextTransition ?? undefined,
      canOrderNow: product.canOrderNow ?? product.availability !== 'ORDERING_CLOSED',
      availabilityReason: product.availabilityReason ?? undefined,
      nextOrderOpenAtUtc: product.nextOrderOpenAtUtc ?? undefined,
      nextOrderCloseAtUtc: product.nextOrderCloseAtUtc ?? undefined,
      availabilityServerNowUtc: product.availabilityServerNowUtc ?? undefined,
      availabilityTimeZoneId: product.availabilityTimeZoneId ?? undefined,
      availabilityClientReceivedAtUtc: Date.now(),
      packSize: product.packSize ?? undefined,
      averageRating: product.averageRating ?? undefined,
      ratingCount: product.ratingCount ?? 0, returnPolicyMode: product.returnPolicyMode, returnWindowDays: product.returnWindowDays ?? undefined, isReturnable: product.isReturnable, returnPolicyDescription: product.returnPolicyDescription ?? undefined,
    };
  }

  private url(storeKey: string): string { return `${environment.apiBaseUrl}/api/store/${encodeURIComponent(storeKey)}`; }
  private asset(path?:string|null):string|undefined{return path?(/^https:\/\//i.test(path)?path:`${environment.apiBaseUrl}${path.startsWith('/')?path:`/${path}`}`):undefined;}
}
