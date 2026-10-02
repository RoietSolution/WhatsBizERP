import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';
import { Product } from './models/storefront.models';

interface ApiProduct { id:string;categoryId:string;name:string;description?:string;imageUrl?:string;sellingPrice:number;compareAtPrice?:number;availability:'IN_STOCK'|'OUT_OF_STOCK';packSize?:string;averageRating?:number;ratingCount?:number; }
import { CustomerSessionService } from './customer-session.service';

@Injectable({ providedIn: 'root' })
export class WishlistService {
  readonly ids = signal<ReadonlySet<string>>(new Set());
  private storeKey = '';
  private loaded = false;

  constructor(private readonly http: HttpClient, private readonly session: CustomerSessionService) {}

  initialize(storeKey: string): void {
    const normalized = storeKey.toLowerCase();
    if (this.storeKey === normalized && this.loaded) return;
    this.storeKey = normalized;
    this.loaded = true;
    const local = this.localIds();
    this.ids.set(new Set(local));
    if (this.session.token(normalized)) void this.loadAndMerge(local);
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
    if (!token) return [];
    try { const rows=await firstValueFrom(this.http.get<ApiProduct[]>(this.url('/wishlist'), { headers: { 'X-Customer-Session': token } }));return rows.map(row=>this.map(row)); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 401) this.session.clear(this.storeKey); return []; }
  }

  async mergeAfterCheckout(storeKey: string): Promise<void> {
    this.initialize(storeKey);
    await this.loadAndMerge(this.localIds());
  }

  private async loadAndMerge(local: string[]): Promise<void> {
    const token = this.session.token(this.storeKey);
    if (!token) return;
    const headers = { 'X-Customer-Session': token };
    try {
      if (local.length) await firstValueFrom(this.http.post(this.url('/wishlist/merge'), { productIds: local }, { headers }));
      const products = await firstValueFrom(this.http.get<ApiProduct[]>(this.url('/wishlist'), { headers }));
      const merged = new Set(products.map(product => product.id));
      this.ids.set(merged); this.saveLocal(merged);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) this.session.clear(this.storeKey);
    }
  }

  private map(row:ApiProduct):Product{return{id:row.id,categoryId:row.categoryId,name:row.name,description:row.description??'',imageUrl:row.imageUrl?environment.apiBaseUrl+row.imageUrl:'/images/product-placeholder.svg',sellingPrice:row.sellingPrice,compareAtPrice:row.compareAtPrice,available:row.availability==='IN_STOCK',packSize:row.packSize,averageRating:row.averageRating,ratingCount:row.ratingCount};}

  private localIds(): string[] {
    try { const value = JSON.parse(localStorage.getItem(this.localKey()) ?? '[]'); return Array.isArray(value) ? value.filter(x => typeof x === 'string').slice(0, 200) : []; }
    catch { return []; }
  }
  private saveLocal(ids: ReadonlySet<string>): void { localStorage.setItem(this.localKey(), JSON.stringify([...ids])); }
  private localKey(): string { return 'khatadhari.wishlist.' + this.storeKey; }
  private url(path: string): string { return environment.apiBaseUrl + '/api/store/' + encodeURIComponent(this.storeKey) + path; }
}