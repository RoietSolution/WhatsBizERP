import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';
import { Product } from './models/storefront.models';
import { CustomerSessionService } from './customer-session.service';

interface ApiProduct { id:string;categoryId:string;name:string;description?:string;imageUrl?:string;sellingPrice:number;compareAtPrice?:number;availability:'IN_STOCK'|'OUT_OF_STOCK';packSize?:string;averageRating?:number;ratingCount?:number; }

@Injectable({ providedIn: 'root' })
export class WishlistService {
  readonly ids = signal<ReadonlySet<string>>(new Set());
  private storeKey = '';
  private customerId = '';
  private loaded = false;

  constructor(private readonly http: HttpClient, private readonly session: CustomerSessionService) {}

  initialize(storeKey: string): void {
    const normalized = storeKey.trim().toLowerCase();
    if (this.storeKey === normalized && this.loaded) return;
    this.storeKey = normalized;
    this.customerId = '';
    this.loaded = true;
    this.ids.set(new Set(this.localIds()));
  }

  async switchCustomer(storeKey: string, customerId: string | null): Promise<void> {
    this.initialize(storeKey);
    const nextCustomer = customerId?.trim().toLowerCase() ?? '';
    if (this.customerId === nextCustomer) return;
    const guestIds = this.customerId ? [] : [...this.ids()];
    this.customerId = nextCustomer;
    const ownedIds = this.localIds();
    this.ids.set(new Set(ownedIds));
    if (nextCustomer) await this.loadAndMerge(guestIds);
  }

  has(productId: string): boolean { return this.ids().has(productId); }

  async toggle(product: Product): Promise<boolean> {
    const next = new Set(this.ids());
    const adding = !next.has(product.id);
    adding ? next.add(product.id) : next.delete(product.id);
    this.ids.set(next);
    this.saveLocal(next);
    const token = this.session.token(this.storeKey);
    if (!token) return true;
    try {
      await firstValueFrom(this.http.request(adding ? 'PUT' : 'DELETE', this.url('/wishlist/' + encodeURIComponent(product.id)), {
        headers: { 'X-Customer-Session': token },
      }));
    } catch {
      const rollback = new Set(this.ids());
      adding ? rollback.delete(product.id) : rollback.add(product.id);
      this.ids.set(rollback); this.saveLocal(rollback); return false; }
    return true;
  }

  async products(): Promise<Product[]> {
    const token = this.session.token(this.storeKey);
    if (!token || !this.customerId) return [];
    try { const rows=await firstValueFrom(this.http.get<ApiProduct[]>(this.url('/wishlist'), { headers: { 'X-Customer-Session': token } }));return rows.map(row=>this.map(row)); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 401) this.session.clear(this.storeKey); return []; }
  }

  async mergeAfterCheckout(storeKey: string): Promise<void> {
    this.initialize(storeKey);
    if (this.customerId) await this.loadAndMerge([...this.ids()]);
  }

  private async loadAndMerge(local: string[]): Promise<void> {
    const targetStore = this.storeKey;
    const targetCustomer = this.customerId;
    const token = this.session.token(targetStore);
    if (!token || !targetCustomer) return;
    const headers = { 'X-Customer-Session': token };
    try {
      if (local.length) await firstValueFrom(this.http.post(this.url('/wishlist/merge', targetStore), { productIds: local }, { headers }));
      const products = await firstValueFrom(this.http.get<ApiProduct[]>(this.url('/wishlist', targetStore), { headers }));
      if (this.storeKey !== targetStore || this.customerId !== targetCustomer) return;
      const merged = new Set(products.map(product => product.id));
      this.ids.set(merged); this.saveLocal(merged);
      if (local.length) this.removeLocal('');
    } catch (error) {
      if (this.storeKey === targetStore && this.customerId === targetCustomer && error instanceof HttpErrorResponse && error.status === 401) this.session.clear(targetStore);
    }
  }

  private map(row:ApiProduct):Product{return{id:row.id,categoryId:row.categoryId,name:row.name,description:row.description??'',imageUrl:row.imageUrl?environment.apiBaseUrl+row.imageUrl:'/images/product-placeholder.svg',sellingPrice:row.sellingPrice,compareAtPrice:row.compareAtPrice,available:row.availability==='IN_STOCK',packSize:row.packSize,averageRating:row.averageRating,ratingCount:row.ratingCount};}

  private localIds(): string[] {
    try { const value = JSON.parse(localStorage.getItem(this.localKey()) ?? '[]'); return Array.isArray(value) ? value.filter(x => typeof x === 'string').slice(0, 200) : []; }
    catch { return []; }
  }
  private saveLocal(ids: ReadonlySet<string>): void { try { localStorage.setItem(this.localKey(), JSON.stringify([...ids])); } catch {} }
  private removeLocal(customerId: string): void { try { localStorage.removeItem(this.localKey(customerId)); } catch {} }
  private localKey(customerId = this.customerId): string { return 'khatadhari.wishlist.' + this.storeKey + ':' + (customerId || 'guest'); }
  private url(path: string, storeKey = this.storeKey): string { return environment.apiBaseUrl + '/api/store/' + encodeURIComponent(storeKey) + path; }
}