import { Component, OnInit, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CartService } from '../cart/cart.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { Product } from '../models/storefront.models';

@Component({
  standalone: true,
  imports: [CurrencyPipe, RouterLink],
  template: `
    @if (loading()) {
      <div class="not-found" role="status">Loading product…</div>
    } @else if (loadFailed()) {
      <div class="not-found" role="alert"><h1>We could not load this product</h1><button type="button" (click)="load()">Try again</button></div>
    } @else if (product(); as item) {
      <a class="back-link" [routerLink]="['/', storeKey]">&larr; Continue browsing</a>
      <article class="detail-card">
        <div class="photo"><img [src]="item.imageUrl" [alt]="item.name" (error)="imageFailed($event)" /></div>
        <div class="info">
          <span class="unit">{{ item.unitLabel }}</span><h1>{{ item.name }}</h1>
          <p class="description">{{ item.description }}</p>
          <div class="price-line"><strong>{{ item.sellingPrice | currency:'INR':'symbol':'1.0-2' }}</strong>
            @if (item.compareAtPrice) { <del>{{ item.compareAtPrice | currency:'INR':'symbol':'1.0-2' }}</del> }
          </div>
          <p class="availability" [class.unavailable]="!item.available"><i></i>{{ item.available ? 'Available today' : 'Out of stock' }}</p>
          <button class="add-button" type="button" [disabled]="!item.available" (click)="add(item)">{{ added() ? 'Added to cart' : item.available ? 'Add to cart' : 'Currently unavailable' }}</button>
        </div>
      </article>
    } @else {
      <div class="not-found"><h1>Product not found</h1><a [routerLink]="['/', storeKey]">Back to store</a></div>
    }
  `,
  styles: [`
    :host{display:block}.back-link{display:inline-block;margin:0 0 17px;color:var(--green);font-size:12px;font-weight:700;text-decoration:none}.detail-card{display:grid;grid-template-columns:1.1fr 1fr;gap:34px;padding:22px;border:1px solid #eceee7;border-radius:22px;background:#fff}.photo{overflow:hidden;min-height:360px;border-radius:17px;background:#f4f4ef}.photo img{width:100%;height:100%;min-height:360px;object-fit:cover}.info{align-self:center;padding:16px 8px}.unit{color:#899087;font-size:11px}.info h1{margin:8px 0 10px;color:#253128;font:800 29px Manrope,sans-serif;letter-spacing:-1px}.description{color:#737b71;font-size:14px;line-height:1.7}.price-line{display:flex;align-items:center;gap:11px;margin-top:20px}.price-line strong{color:#26382a;font:800 25px Manrope,sans-serif}.price-line del{color:#a0a49b;font-size:14px}.availability{display:flex;align-items:center;gap:7px;margin:18px 0;color:#338052;font-size:12px;font-weight:700}.availability i{width:8px;height:8px;border-radius:50%;background:#49a86d}.availability.unavailable{color:#b25346}.availability.unavailable i{background:#cf715e}.add-button{width:100%;min-height:48px;border:0;border-radius:13px;color:#fff;background:var(--green);font-weight:800;cursor:pointer}.add-button:disabled{background:#b8bdb6;cursor:not-allowed}.not-found{padding:50px;text-align:center}.not-found a{color:var(--green)}@media(max-width:650px){.detail-card{grid-template-columns:1fr;gap:12px;padding:12px;border-radius:18px}.photo,.photo img{min-height:260px;height:260px}.info{padding:10px 5px}.info h1{font-size:24px}}
  `],
})
export class ProductDetailsPage implements OnInit {
  readonly product = signal<Product | null>(null);
  readonly added = signal(false);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  storeKey = '';
  private productId = '';
  constructor(private readonly route: ActivatedRoute, private readonly data: StorefrontDataService, private readonly cart: CartService) {}
  ngOnInit(): void {
    this.storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? '';
    this.productId = this.route.snapshot.paramMap.get('productId') ?? '';
    void this.load();
  }
  add(product: Product): void { if (this.cart.add(product)) { this.added.set(true); } }
  imageFailed(event: Event): void { (event.target as HTMLImageElement).src = '/images/product-placeholder.svg'; }
  async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    try { this.product.set(await this.data.getProduct(this.storeKey, this.productId)); }
    catch { this.loadFailed.set(true); }
    finally { this.loading.set(false); }
  }
}
