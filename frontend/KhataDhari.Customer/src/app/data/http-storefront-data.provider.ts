import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { CartLine, CartQuote, Category, CheckoutCustomer, CheckoutResult, CustomerOrder, Product, ProductReview, ProductReviewSummary, Store, StorefrontOffer } from '../models/storefront.models';
import { StorefrontDataProvider } from './storefront-data.provider';
import { CustomerSessionService } from '../customer-session.service';

interface ApiStore { storeKey: string; name: string; tagline?: string; logoUrl?: string | null; allCategoryImageUrl?: string | null; accentColor?: string; deliveryMessage?: string; freeDeliveryThreshold?:number|null; banners?: Array<{slot:'PRIMARY'|'SECONDARY';imageUrl:string;title?:string;subtitle?:string;targetUrl?:string;displayOrder:number;promotionId?:string|null}>; showProductRatings?:boolean; showProductReviews?:boolean; paymentMethods?: Array<{code:'UPI'|'NET_BANKING'|'COD';provider:'RAZORPAY'|'COD';label:string;description:string;isDefault:boolean;usesHostedPaymentPage:boolean}>; }
interface ApiCategory { id: string; name: string; imageUrl?: string | null; }
interface ApiOffer extends StorefrontOffer {}
interface ApiProduct { id: string; categoryId: string; name: string; description?: string | null; imageUrl?: string | null; sellingPrice: number; compareAtPrice?: number | null; availability: 'IN_STOCK' | 'OUT_OF_STOCK'; packSize?: string | null; averageRating?: number | null; ratingCount?: number; }

@Injectable({ providedIn: 'root' })
export class HttpStorefrontDataProvider implements StorefrontDataProvider {
  constructor(private readonly http: HttpClient, private readonly session: CustomerSessionService) {}

  async getStore(storeKey: string): Promise<Store | null> {
    let store: ApiStore;
    try { store = await firstValueFrom(this.http.get<ApiStore>(this.url(storeKey))); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 404) return null; throw error; }
    return { storeKey: store.storeKey, name: store.name, tagline: store.tagline ?? 'Good things, close to home.', logoUrl: this.asset(store.logoUrl), allCategoryImageUrl:this.asset(store.allCategoryImageUrl), accentColor: store.accentColor ?? '#145c43', freeDeliveryThreshold:store.freeDeliveryThreshold??undefined, deliveryMessage: store.deliveryMessage ?? 'Fresh picks, close to home.', showProductRatings:store.showProductRatings??true, showProductReviews:store.showProductReviews??true, banners:(store.banners??[]).map(b=>({...b,imageUrl:this.asset(b.imageUrl)!})),paymentMethods:store.paymentMethods??[] };
  }

  async getCategories(storeKey: string): Promise<Category[]> {
    const categories = await firstValueFrom(this.http.get<ApiCategory[]>(`${this.url(storeKey)}/categories`));
    return categories.map((category) => ({ id: category.id, name: category.name, imageUrl:this.asset(category.imageUrl) }));
  }

  async getProducts(storeKey: string): Promise<Product[]> {
    const products = await firstValueFrom(this.http.get<ApiProduct[]>(`${this.url(storeKey)}/products`));
    return products.map((product) => this.mapProduct(product));
  }

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
  async quote(storeKey:string,lines:readonly CartLine[],pincode:string):Promise<CartQuote|null>{if(!lines.length)return null;const token=this.session.token(storeKey);return await firstValueFrom(this.http.post<CartQuote>(`${this.url(storeKey)}/cart/quote`,{pincode,items:lines.map(x=>({productId:x.product.id,quantity:x.quantity}))},{headers:token?{'X-Customer-Session':token}:{}}));}
  async getReviews(storeKey:string,productId:string):Promise<ProductReviewSummary>{const token=this.session.token(storeKey);return await firstValueFrom(this.http.get<ProductReviewSummary>(`${this.url(storeKey)}/products/${encodeURIComponent(productId)}/reviews`,{headers:token?{'X-Customer-Session':token}:{}}));}
  async saveReview(storeKey:string,productId:string,rating:number,reviewText:string):Promise<ProductReview>{return await firstValueFrom(this.http.put<ProductReview>(`${this.url(storeKey)}/products/${encodeURIComponent(productId)}/reviews/mine`,{rating,reviewText},{headers:{'X-Customer-Session':this.session.token(storeKey)}}));}

  async checkout(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string, paymentMethod: string): Promise<CheckoutResult> {
    return await firstValueFrom(this.http.post<CheckoutResult>(`${this.url(storeKey)}/checkout`, {
      ...customer,
      email: customer.email || null,
      paymentMethod,
      items: lines.map((line) => ({ productId: line.product.id, quantity: line.quantity })),
    }, { headers: { 'Idempotency-Key': idempotencyKey, ...(this.session.token(storeKey) ? { 'X-Customer-Session': this.session.token(storeKey) } : {}) } }));
  }

  private mapProduct(product: ApiProduct): Product {
    return {
      id: product.id,
      categoryId: product.categoryId,
      name: product.name,
      description: product.description ?? '',
      imageUrl: product.imageUrl ? `${environment.apiBaseUrl}${product.imageUrl}` : '/images/product-placeholder.svg',
      sellingPrice: product.sellingPrice,
      compareAtPrice: product.compareAtPrice ?? undefined,
      available: product.availability === 'IN_STOCK',
      packSize: product.packSize ?? undefined,
      averageRating: product.averageRating ?? undefined,
      ratingCount: product.ratingCount ?? 0,
    };
  }

  private url(storeKey: string): string { return `${environment.apiBaseUrl}/api/store/${encodeURIComponent(storeKey)}`; }
  private asset(path?:string|null):string|undefined{return path?`${environment.apiBaseUrl}${path}`:undefined;}
}
