import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CartService } from './cart/cart.service';
import { StorefrontDataService } from './data/storefront-data.service';
import { Store } from './models/storefront.models';

@Component({
  selector: 'shop-store-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    @if (loading()) {
      <div class="loading-screen"><span class="loader"></span><p>Opening your neighbourhood store…</p></div>
    } @else if (loadFailed()) {
      <main class="store-missing"><span class="missing-icon">!</span><h1>Store temporarily unavailable</h1><p>Please check your connection and try again.</p><button type="button" (click)="retry()">Try again</button></main>
    } @else if (store(); as currentStore) {
      <header class="store-header">
        <div class="header-inner">
          <a class="brand" [routerLink]="['/', currentStore.storeKey]" aria-label="Store home">
            @if (currentStore.logoUrl) { <img [src]="currentStore.logoUrl" alt="" /> }
            @else { <span class="brand-mark">{{ currentStore.name.slice(0, 1) }}</span> }
            <span class="brand-copy"><strong>{{ currentStore.name }}</strong><small>{{ currentStore.tagline }}</small></span>
          </a>
          <div class="delivery-note"><span>📍</span> Fresh picks, close to home</div>
          <button class="desktop-cart" type="button" (click)="goCart()" aria-label="Open cart">
            <span>▣</span><span>Cart</span><b>{{ cart.itemCount() }}</b>
          </button>
        </div>
        <form class="search-wrap" (submit)="search($event)">
          <span class="search-icon">⌕</span>
          <input aria-label="Search products" placeholder="Search milk, fruits, snacks…" [value]="searchText" (input)="updateSearch($event)" />
          @if (searchText) { <button type="button" class="clear-search" (click)="clearSearch()" aria-label="Clear search">×</button> }
          <button class="search-submit" type="submit">Search</button>
        </form>
      </header>

      <main class="store-content"><router-outlet /></main>

      <nav class="bottom-nav" aria-label="Main navigation">
        <a [routerLink]="['/', currentStore.storeKey]" routerLinkActive="active" [routerLinkActiveOptions]="{exact:true}">
          <span class="nav-icon">⌂</span><small>Home</small>
        </a>
        <button type="button" (click)="focusSearch()"><span class="nav-icon">⌕</span><small>Search</small></button>
        <a [routerLink]="['/', currentStore.storeKey, 'cart']" routerLinkActive="active" class="cart-nav">
          <span class="nav-icon">▣ @if (cart.itemCount() > 0) { <i>{{ cart.itemCount() }}</i> }</span><small>Cart</small>
        </a>
        <a [routerLink]="['/', currentStore.storeKey, 'orders']" routerLinkActive="active">
          <span class="nav-icon">◷</span><small>Orders</small>
        </a>
      </nav>
    } @else {
      <main class="store-missing">
        <span class="missing-icon">🛍️</span><h1>We couldn’t find that store</h1>
        <p>Check the store link and try again.</p><a routerLink="/">Back to shops</a>
      </main>
    }
  `,
  styles: [`
    :host { display: block; min-height: 100vh; }
    .store-header { position: sticky; z-index: 20; top: 0; padding: 13px 24px 12px; background: rgba(255,255,255,.96); border-bottom: 1px solid #eeefe8; backdrop-filter: blur(14px); }
    .header-inner, .search-wrap { width: min(1080px, 100%); margin: 0 auto; }
    .header-inner { display: flex; align-items: center; gap: 24px; min-height: 44px; }
    .brand { display: flex; align-items: center; gap: 10px; min-width: 220px; text-decoration: none; }
    .brand-mark { display: grid; width: 42px; height: 42px; place-items: center; border-radius: 14px; color: #fff; background: var(--green); font: 800 21px Manrope,sans-serif; }
    .brand img { width: 42px; height: 42px; object-fit: cover; border-radius: 14px; }
    .brand-copy { display: flex; flex-direction: column; gap: 1px; }
    .brand-copy strong { font: 800 17px Manrope,sans-serif; letter-spacing: -.5px; }
    .brand-copy small { color: var(--muted); font-size: 10px; }
    .delivery-note { display: flex; align-items: center; gap: 7px; margin: auto; color: #677168; font-size: 12px; }
    .desktop-cart { display: flex; align-items: center; gap: 8px; border: 0; padding: 9px 12px; border-radius: 12px; color: var(--green); background: #f1f7f1; font-weight: 700; cursor: pointer; }
    .desktop-cart b { display: grid; width: 21px; height: 21px; place-items: center; border-radius: 50%; color: #fff; background: var(--green); font-size: 11px; }
    .search-wrap { display: flex; align-items: center; gap: 10px; height: 43px; margin-top: 12px; padding: 0 10px 0 14px; border: 1px solid #ededE7; border-radius: 13px; background: #f8f8f5; }
    .search-icon { color: #68736a; font-size: 23px; line-height: 1; transform: rotate(-20deg); }
    input { flex: 1; min-width: 0; border: 0; outline: 0; color: var(--ink); background: transparent; font-size: 13px; }
    input::placeholder { color: #92968e; }
    .search-submit, .clear-search { border: 0; color: var(--green); background: transparent; font-size: 12px; font-weight: 700; cursor: pointer; }
    .clear-search { color: #81877e; font-size: 20px; }
    .store-content { width: min(1080px, 100%); min-height: calc(100vh - 130px); margin: 0 auto; padding: 26px 24px 60px; }
    .bottom-nav { display: none; }
    .loading-screen { min-height: 70vh; display: grid; align-content: center; justify-items: center; color: var(--muted); font-size: 13px; }
    .loader { width: 27px; height: 27px; border: 3px solid #dbe8dd; border-top-color: var(--green); border-radius: 50%; animation: spin .8s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
    .store-missing { display: grid; min-height: 100vh; align-content: center; justify-items: center; padding: 25px; text-align: center; }
    .missing-icon { font-size: 42px; }.store-missing h1 { margin: 14px 0 5px; font: 800 25px Manrope,sans-serif; }.store-missing p { color: var(--muted); }.store-missing a { margin-top: 10px; color: var(--green); font-weight: 700; }
    .store-missing button { margin-top: 9px; padding: 10px 15px; border: 0; border-radius: 10px; color: #fff; background: var(--green); font-weight: 700; cursor: pointer; }
    @media (max-width: 640px) {
      .store-header { padding: 10px 15px 11px; }
      .header-inner { min-height: 42px; }
      .brand { min-width: 0; gap: 9px; }.brand-mark,.brand img { width: 38px; height: 38px; border-radius: 13px; }
      .brand-copy strong { font-size: 16px; }.brand-copy small { font-size: 9px; }
      .delivery-note { display: none; }.desktop-cart { margin-left: auto; padding: 8px 9px; font-size: 0; }.desktop-cart span:first-child { font-size: 16px; }
      .search-wrap { margin-top: 10px; height: 42px; }
      .store-content { min-height: calc(100vh - 120px); padding: 17px 15px 98px; }
      .bottom-nav { position: fixed; z-index: 30; right: 0; bottom: 0; left: 0; display: grid; grid-template-columns: repeat(4,1fr); padding: 7px 10px max(8px, env(safe-area-inset-bottom)); border-top: 1px solid #e9ebe3; background: rgba(255,255,255,.97); backdrop-filter: blur(12px); }
      .bottom-nav a,.bottom-nav button { display: flex; flex-direction: column; align-items: center; gap: 1px; border: 0; padding: 3px; color: #8b9088; background: transparent; text-decoration: none; cursor: pointer; }
      .bottom-nav small { font-size: 10px; font-weight: 600; }.bottom-nav .active { color: var(--green); }
      .nav-icon { position: relative; min-height: 24px; font-size: 22px; line-height: 1.1; }
      .nav-icon i { position: absolute; top: -3px; right: -13px; display: grid; width: 16px; height: 16px; place-items: center; border-radius: 50%; color: white; background: #dd7754; font-size: 9px; font-style: normal; }
    }
  `],
})
export class StoreShell implements OnInit {
  readonly store = signal<Store | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  searchText = '';
  private readonly destroyRef = inject(DestroyRef);

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly data: StorefrontDataService,
    readonly cart: CartService,
  ) {}

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params: ParamMap) => {
      const storeKey = params.get('storeKey') ?? '';
      this.loading.set(true);
      this.loadFailed.set(false);
      this.cart.useStore(storeKey);
      void this.load(storeKey);
    });
  }

  updateSearch(event: Event): void { this.searchText = (event.target as HTMLInputElement).value; }
  clearSearch(): void { this.searchText = ''; this.search(new Event('submit')); }
  search(event: Event): void {
    event.preventDefault();
    const storeKey = this.store()?.storeKey;
    if (storeKey) void this.router.navigate(['/', storeKey], { queryParams: { q: this.searchText.trim() || null } });
  }
  goCart(): void { const key = this.store()?.storeKey; if (key) void this.router.navigate(['/', key, 'cart']); }
  focusSearch(): void { document.querySelector<HTMLInputElement>('.search-wrap input')?.focus(); }
  retry(): void { const key = this.route.snapshot.paramMap.get('storeKey') ?? ''; this.loading.set(true); this.loadFailed.set(false); void this.load(key); }

  private async load(storeKey: string): Promise<void> {
    try { this.store.set(await this.data.getStore(storeKey)); }
    catch { this.store.set(null); this.loadFailed.set(true); }
    finally { this.loading.set(false); }
  }
}
