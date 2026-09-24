import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { CartLine, Category, CheckoutCustomer, CheckoutResult, CustomerOrder, Product, Store } from '../models/storefront.models';
import { StorefrontDataProvider } from './storefront-data.provider';

interface ApiStore { storeKey: string; name: string; tagline?: string; logoUrl?: string | null; accentColor?: string; deliveryMessage?: string; }
interface ApiCategory { id: string; name: string; }
interface ApiProduct { id: string; categoryId: string; name: string; description?: string | null; imageUrl?: string | null; sellingPrice: number; compareAtPrice?: number | null; availability: 'IN_STOCK' | 'OUT_OF_STOCK'; unitLabel: string; }

@Injectable({ providedIn: 'root' })
export class HttpStorefrontDataProvider implements StorefrontDataProvider {
  constructor(private readonly http: HttpClient) {}

  async getStore(storeKey: string): Promise<Store | null> {
    let store: ApiStore;
    try { store = await firstValueFrom(this.http.get<ApiStore>(this.url(storeKey))); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 404) return null; throw error; }
    return { storeKey: store.storeKey, name: store.name, tagline: store.tagline ?? 'Good things, close to home.', logoUrl: store.logoUrl ?? undefined, accentColor: store.accentColor ?? '#145c43', deliveryMessage: store.deliveryMessage ?? 'Fresh picks, close to home.' };
  }

  async getCategories(storeKey: string): Promise<Category[]> {
    const categories = await firstValueFrom(this.http.get<ApiCategory[]>(`${this.url(storeKey)}/categories`));
    return categories.map((category) => ({ id: category.id, name: category.name }));
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

  async checkoutWithRazorpay(storeKey: string, customer: CheckoutCustomer, lines: readonly CartLine[], idempotencyKey: string): Promise<CheckoutResult> {
    return await firstValueFrom(this.http.post<CheckoutResult>(`${this.url(storeKey)}/checkout/razorpay`, {
      ...customer,
      email: customer.email || null,
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
      unitLabel: product.unitLabel,
    };
  }

  private url(storeKey: string): string { return `${environment.apiBaseUrl}/api/store/${encodeURIComponent(storeKey)}`; }
}
