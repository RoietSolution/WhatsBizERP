import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { CartLine, Category, CheckoutCustomer, CheckoutResult, CustomerOrder, Product, Store } from '../models/storefront.models';
import { StorefrontDataProvider } from './storefront-data.provider';

interface ApiStore { storeKey: string; name: string; tagline?: string; logoUrl?: string | null; accentColor?: string; deliveryMessage?: string; banners?: Array<{slot:'PRIMARY'|'SECONDARY';imageUrl:string;title?:string;subtitle?:string;targetUrl?:string;displayOrder:number}>; paymentMethods?: Array<{code:'RAZORPAY'|'DIRECT_UPI'|'COD';label:string;description:string;isDefault:boolean;usesHostedPaymentPage:boolean}>; }
interface ApiCategory { id: string; name: string; imageUrl?: string | null; }
interface ApiProduct { id: string; categoryId: string; name: string; description?: string | null; imageUrl?: string | null; sellingPrice: number; compareAtPrice?: number | null; availability: 'IN_STOCK' | 'OUT_OF_STOCK'; unitLabel?: string | null; }

@Injectable({ providedIn: 'root' })
export class HttpStorefrontDataProvider implements StorefrontDataProvider {
  constructor(private readonly http: HttpClient) {}

  async getStore(storeKey: string): Promise<Store | null> {
    let store: ApiStore;
    try { store = await firstValueFrom(this.http.get<ApiStore>(this.url(storeKey))); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 404) return null; throw error; }
    return { storeKey: store.storeKey, name: store.name, tagline: store.tagline ?? 'Good things, close to home.', logoUrl: this.asset(store.logoUrl), accentColor: store.accentColor ?? '#145c43', deliveryMessage: store.deliveryMessage ?? 'Fresh picks, close to home.', banners:(store.banners??[]).map(b=>({...b,imageUrl:this.asset(b.imageUrl)!})),paymentMethods:store.paymentMethods??[] };
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

  async getOrders(_storeKey: string): Promise<CustomerOrder[]> { return []; }

  async checkout(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string, paymentProvider: string): Promise<CheckoutResult> {
    return await firstValueFrom(this.http.post<CheckoutResult>(`${this.url(storeKey)}/checkout`, {
      ...customer,
      email: customer.email || null,
      paymentProvider,
      items: lines.map((line) => ({ productId: line.product.id, quantity: line.quantity })),
    }, { headers: { 'Idempotency-Key': idempotencyKey } }));
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
      unitLabel: product.unitLabel ?? undefined,
    };
  }

  private url(storeKey: string): string { return `${environment.apiBaseUrl}/api/store/${encodeURIComponent(storeKey)}`; }
  private asset(path?:string|null):string|undefined{return path?`${environment.apiBaseUrl}${path}`:undefined;}
}
