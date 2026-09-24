import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink, RouterOutlet } from '@angular/router';
import { CartService } from './cart/cart.service';
import { StorefrontDataService } from './data/storefront-data.service';
import { Store } from './models/storefront.models';

@Component({
  selector: 'shop-store-shell',
  standalone: true,
  imports: [CurrencyPipe, RouterLink, RouterOutlet],
  template: `
    @if (loading()) {
      <div class="shell-skeleton" role="status" aria-label="Opening store">
        <div class="skeleton-header"><i></i><span></span><b></b></div><div class="skeleton-search"></div>
        <div class="skeleton-body"><div></div><div></div><div></div><div></div></div>
      </div>
    } @else if (loadFailed()) {
      <main class="state-page"><span class="state-icon">!</span><h1>Store temporarily unavailable</h1><p>We couldn't connect to this store. Check your connection and try again.</p><button type="button" (click)="retry()">Try again</button></main>
    } @else if (store(); as currentStore) {
      <div class="store-frame" [style.--store-primary]="currentStore.accentColor">
        <header class="store-header">
          <div class="header-inner">
            <a class="brand" [routerLink]="['/', currentStore.storeKey]" aria-label="Store home">
              @if (currentStore.logoUrl) { <img [src]="currentStore.logoUrl" alt="" /> }
              @else { <span class="brand-mark">{{ currentStore.name.slice(0, 1) }}</span> }
              <span class="brand-copy"><strong>{{ currentStore.name }}</strong><small>Powered by KhataDhari</small></span>
            </a>
            <form class="search-wrap" (submit)="search($event)">
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m21 21-4.3-4.3m2.3-5.2a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z"/></svg>
              <input aria-label="Search products" placeholder="Search for products..." [value]="searchText" (input)="updateSearch($event)" />
              @if (searchText) { <button type="button" class="clear-search" (click)="clearSearch()" aria-label="Clear search">&times;</button> }
            </form>
            <button class="desktop-cart" type="button" (click)="goCart()" aria-label="Open cart">
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 4h2l2.1 10.2a2 2 0 0 0 2 1.6h7.8a2 2 0 0 0 2-1.6L20 8H6M10 20h.01M17 20h.01"/></svg>
              <span><strong>Cart</strong>@if (cart.itemCount()) { <small>{{ cart.itemCount() }} items · {{ cart.total() | currency:'INR':'symbol':'1.0-0' }}</small> } @else { <small>Empty</small> }</span>
            </button>
          </div>
        </header>

        <main class="store-content"><router-outlet /></main>

        @if (cart.itemCount() > 0) {
          <button class="mobile-cart-bar" type="button" (click)="goCart()" aria-label="View cart">
            <span><b>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'item' : 'items' }}</b><small>{{ cart.total() | currency:'INR':'symbol':'1.0-0' }}</small></span>
            <strong>View Cart <span aria-hidden="true">→</span></strong>
          </button>
        }
      </div>
    } @else {
      <main class="state-page"><span class="state-icon">?</span><h1>Store not found</h1><p>This shopping link may be incorrect or the store is unavailable.</p><a routerLink="/">Back to shops</a></main>
    }
  `,
  styles: [`
    :host{display:block;min-height:100vh}.store-frame{min-height:100vh;--tenant-primary:var(--store-primary)}
    .store-header{position:sticky;z-index:20;top:0;border-bottom:1px solid var(--store-border);background:rgba(255,255,255,.96);backdrop-filter:blur(14px)}
    .header-inner{display:grid;grid-template-columns:250px minmax(280px,680px) 190px;align-items:center;gap:22px;width:min(var(--store-content-width),100%);min-height:76px;margin:auto;padding:12px 24px}
    .brand{display:flex;align-items:center;gap:10px;min-width:0;text-decoration:none}.brand-mark,.brand img{display:grid;flex:0 0 auto;width:42px;height:42px;place-items:center;border-radius:12px}.brand-mark{color:#fff;background:var(--store-primary);font:800 19px Manrope,sans-serif}.brand img{object-fit:cover}
    .brand-copy{display:grid;min-width:0;line-height:1.15}.brand-copy strong{overflow:hidden;color:var(--store-text);font:800 17px Manrope,sans-serif;text-overflow:ellipsis;white-space:nowrap}.brand-copy small{margin-top:4px;color:var(--store-muted);font-size:10px}
    .search-wrap{display:flex;align-items:center;height:48px;border:1px solid var(--store-border);border-radius:14px;padding:0 12px;background:var(--store-background);transition:.18s}.search-wrap:focus-within{border-color:var(--store-primary);background:#fff;box-shadow:0 0 0 3px var(--store-primary-soft)}.search-wrap svg{width:20px;fill:none;stroke:var(--store-muted);stroke-linecap:round;stroke-width:1.8}.search-wrap input{flex:1;min-width:0;height:100%;border:0;padding:0 10px;color:var(--store-text);background:transparent;outline:0;font-size:14px}.search-wrap input::placeholder{color:#87918a}.clear-search{display:grid;width:30px;height:30px;place-items:center;border:0;border-radius:50%;color:var(--store-muted);background:transparent;font-size:22px;cursor:pointer}.clear-search:hover{background:var(--store-surface-muted)}
    .desktop-cart{display:flex;align-items:center;justify-content:center;gap:10px;min-height:48px;border:1px solid var(--store-primary);border-radius:14px;padding:7px 12px;color:var(--store-primary-dark);background:var(--store-primary-soft);cursor:pointer}.desktop-cart svg{width:23px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.desktop-cart>span{display:grid;text-align:left}.desktop-cart strong{font-size:13px}.desktop-cart small{margin-top:1px;color:var(--store-muted);font-size:10px;white-space:nowrap}
    .store-content{width:min(var(--store-content-width),100%);min-height:calc(100vh - 77px);margin:auto;padding:24px 24px 80px}
    .mobile-cart-bar{display:none}.state-page{display:grid;min-height:100vh;align-content:center;justify-items:center;padding:28px;text-align:center}.state-icon{display:grid;width:58px;height:58px;place-items:center;border-radius:18px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 24px Manrope,sans-serif}.state-page h1{margin:16px 0 5px;font:800 24px Manrope,sans-serif}.state-page p{max-width:390px;margin:0;color:var(--store-muted);line-height:1.6}.state-page button,.state-page a{margin-top:18px;border:0;border-radius:12px;padding:12px 18px;color:#fff;background:var(--store-primary);font-weight:700;text-decoration:none;cursor:pointer}
    .shell-skeleton{width:min(var(--store-content-width),100%);margin:auto;padding:15px 24px}.skeleton-header{display:flex;align-items:center;gap:10px}.skeleton-header i,.skeleton-header span,.skeleton-header b,.skeleton-search,.skeleton-body div{display:block;background:linear-gradient(90deg,#edf0eb 25%,#f8f9f7 50%,#edf0eb 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.skeleton-header i{width:42px;height:42px;border-radius:12px}.skeleton-header span{width:150px;height:17px;border-radius:6px}.skeleton-header b{width:120px;height:42px;margin-left:auto;border-radius:12px}.skeleton-search{height:48px;margin:18px 0;border-radius:14px}.skeleton-body{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-top:40px}.skeleton-body div{height:280px;border-radius:18px}@keyframes shimmer{to{background-position:-200% 0}}
    @media(max-width:800px){.header-inner{grid-template-columns:minmax(0,1fr) auto;gap:10px;min-height:auto;padding:10px 16px 12px}.brand-mark,.brand img{width:38px;height:38px}.brand-copy strong{font-size:16px}.desktop-cart{width:44px;min-height:42px;padding:0}.desktop-cart>span{display:none}.search-wrap{grid-column:1/-1;grid-row:2;height:46px}.store-content{padding:18px 16px 104px}.mobile-cart-bar{position:fixed;z-index:30;right:12px;bottom:max(12px,env(safe-area-inset-bottom));left:12px;display:flex;align-items:center;justify-content:space-between;min-height:58px;border:0;border-radius:16px;padding:9px 14px;color:#fff;background:var(--store-primary-dark);box-shadow:0 12px 32px rgba(12,70,39,.28);cursor:pointer}.mobile-cart-bar>span{display:grid;text-align:left}.mobile-cart-bar b{font-size:12px}.mobile-cart-bar small{margin-top:2px;color:#d9f0e2;font-size:11px}.mobile-cart-bar>strong{font-size:13px}.mobile-cart-bar>strong span{margin-left:5px;font-size:17px}.skeleton-body{grid-template-columns:repeat(2,1fr);gap:10px}.skeleton-body div{height:235px}}
    @media(max-width:360px){.header-inner,.store-content{padding-right:12px;padding-left:12px}.brand-copy small{display:none}.mobile-cart-bar{right:8px;left:8px}}
    @media(prefers-reduced-motion:reduce){.skeleton-header i,.skeleton-header span,.skeleton-header b,.skeleton-search,.skeleton-body div{animation:none}}
  `],
})
export class StoreShell implements OnInit {
  readonly store = signal<Store | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  searchText = '';
  private readonly destroyRef = inject(DestroyRef);

  constructor(private readonly route: ActivatedRoute, private readonly router: Router,
    private readonly data: StorefrontDataService, readonly cart: CartService) {}

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params: ParamMap) => {
      const storeKey = params.get('storeKey') ?? '';
      this.loading.set(true); this.loadFailed.set(false); this.cart.useStore(storeKey); void this.load(storeKey);
    });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => this.searchText = params.get('q') ?? '');
  }
  updateSearch(event: Event): void { this.searchText = (event.target as HTMLInputElement).value; }
  clearSearch(): void { this.searchText = ''; this.navigateSearch(); }
  search(event: Event): void { event.preventDefault(); this.navigateSearch(); }
  goCart(): void { const key = this.store()?.storeKey; if (key) void this.router.navigate(['/', key, 'cart']); }
  retry(): void { const key = this.route.snapshot.paramMap.get('storeKey') ?? ''; this.loading.set(true); this.loadFailed.set(false); void this.load(key); }
  private navigateSearch(): void { const key = this.store()?.storeKey; if (key) void this.router.navigate(['/', key], { queryParams: { q: this.searchText.trim() || null } }); }
  private async load(storeKey: string): Promise<void> { try { this.store.set(await this.data.getStore(storeKey)); } catch { this.store.set(null); this.loadFailed.set(true); } finally { this.loading.set(false); } }
}
