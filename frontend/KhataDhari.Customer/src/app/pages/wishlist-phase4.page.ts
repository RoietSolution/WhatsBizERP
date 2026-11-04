import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { Product } from '../models/storefront.models';
import { StorefrontNotificationService } from '../storefront-notification.service';
import { WishlistService } from '../wishlist.service';
import { ProductCardComponent } from '../shared/product-card.component';

@Component({
  selector: 'shop-wishlist-page',
  standalone: true,
  imports: [CurrencyPipe, RouterLink, ProductCardComponent],
  template: `
    <section class="wishlist-page">
      <header class="wishlist-header"><div><span class="eyebrow">SAVED FOR LATER</span><h1>Your wishlist</h1><p>Keep the products you love close by.</p></div><a class="browse-link" [routerLink]="['/', storeKey]">Continue shopping <span aria-hidden="true">→</span></a></header>
      @if (loading()) { <div class="state" role="status"><span class="state-icon" aria-hidden="true">⌁</span><h2>Loading your wishlist</h2><p>We’re checking your saved products.</p></div> }
      @else if (!session.active() && !products().length) { <div class="state"><span class="state-icon" aria-hidden="true">♡</span><h2>Wishlist saved on this device</h2><p>Sign in to sync your saved products across devices.</p><a class="primary-link" [routerLink]="['/', storeKey, 'auth']">Sign in</a></div> }
      @else if (!products().length) { <div class="state"><span class="state-icon" aria-hidden="true">♡</span><h2>Your wishlist is empty</h2><p>Save products you love and find them here later.</p><a class="primary-link" [routerLink]="['/', storeKey]">Browse products</a></div> }
      @else { <div class="wishlist-grid">@for (product of products(); track product.id) { <article class="wishlist-card"><shop-product-card [product]="product" [storeKey]="storeKey"/><div class="wishlist-actions"><a [routerLink]="['/', storeKey, 'products', product.id]">View product</a><button type="button" (click)="remove(product)">Remove</button></div></article> }</div> }
    </section>
  `,
  styles: [`
    :host{display:block;min-width:0}.wishlist-page{max-width:1100px;margin:auto;min-width:0}.wishlist-header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;margin-bottom:18px}.eyebrow{display:block;color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.wishlist-header h1{margin:4px 0;font:800 25px Manrope,sans-serif}.wishlist-header p{margin:0;color:var(--store-muted);font-size:12px}.browse-link{display:inline-flex;align-items:center;min-height:40px;color:var(--store-primary-dark);font-size:12px;font-weight:800;text-decoration:none}.browse-link span{margin-left:5px;font-size:16px}.wishlist-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px;min-width:0}.wishlist-card{display:grid;min-width:0;border:1px solid var(--store-border);border-radius:17px;padding:7px;background:#fff;box-shadow:0 7px 20px rgba(15,23,42,.04)}.wishlist-card shop-product-card{min-width:0}.wishlist-actions{display:grid;grid-template-columns:1fr 1fr;gap:7px;padding:7px 4px 4px}.wishlist-actions a,.wishlist-actions button{display:inline-flex;align-items:center;justify-content:center;min-height:40px;border:1px solid var(--store-border);border-radius:9px;padding:6px 5px;color:var(--store-primary-dark);background:#fff;font:800 10px Manrope,sans-serif;text-align:center;text-decoration:none;cursor:pointer}.wishlist-actions button{color:var(--store-danger)}.wishlist-actions a:focus-visible,.wishlist-actions button:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}.state{display:grid;min-height:260px;align-content:center;justify-items:center;gap:8px;border:1px solid var(--store-border);border-radius:18px;padding:24px;background:#fff;text-align:center}.state h2{margin:2px 0;font:800 17px Manrope,sans-serif}.state p{max-width:420px;margin:0;color:var(--store-muted);font-size:12px;line-height:1.6}.state-icon{display:grid;place-items:center;width:44px;height:44px;border-radius:50%;color:var(--store-primary-dark);background:var(--store-primary-soft);font-size:24px}.primary-link{display:inline-flex;align-items:center;min-height:40px;margin-top:8px;border-radius:9px;padding:0 14px;color:#fff;background:var(--store-primary);font-size:11px;font-weight:800;text-decoration:none}@media(max-width:850px){.wishlist-grid{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:560px){.wishlist-header{align-items:flex-start;flex-direction:column;gap:4px}.wishlist-header h1{font-size:22px}.wishlist-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}.wishlist-card{border-radius:14px;padding:5px}.wishlist-actions{gap:5px;padding:6px 2px 2px}.wishlist-actions a,.wishlist-actions button{font-size:9px}}
  `],
})
export class WishlistPhase4Page implements OnInit {
  readonly products = signal<Product[]>([]);
  readonly loading = signal(true);
  storeKey = '';
  constructor(private readonly route: ActivatedRoute, readonly wishlist: WishlistService, readonly session: CustomerSessionService, private readonly data: StorefrontDataService, private readonly notify: StorefrontNotificationService) {}
  ngOnInit(): void { this.storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? this.route.parent?.parent?.snapshot.paramMap.get('storeKey') ?? ''; this.session.restore(this.storeKey); this.wishlist.initialize(this.storeKey); void this.load(); }
  async remove(product: Product): Promise<void> { const updated = await this.wishlist.toggle(product); if (updated) { this.products.set(this.products().filter(item => item.id !== product.id)); this.notify.show('Removed from wishlist'); } else this.notify.show('Could not update wishlist'); }
  private async load(): Promise<void> { try { if (this.session.active()) this.products.set(await this.wishlist.products()); else { const ids = this.wishlist.ids(); const page = await this.data.getProducts(this.storeKey); this.products.set(page.items.filter(product => ids.has(product.id))); } } finally { this.loading.set(false); } }
}
