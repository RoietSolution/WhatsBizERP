import { AfterViewInit, Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Category, Product, ProductPage, Store } from '../models/storefront.models';
import { StorefrontDataService } from '../data/storefront-data.service';
import { ProductCardComponent } from '../shared/product-card.component';
import { RouterLink } from '@angular/router';
import { environment } from '../../environments/environment';

@Component({
  standalone: true,
  imports: [ProductCardComponent, RouterLink],
  template: `
    @if (storeKey) {
      @if (store()?.banners?.length) {
        <section class="store-banners" aria-label="Store promotions">
          @if (banner('PRIMARY'); as primary) {
            <a class="store-banner primary" [routerLink]="bannerLink(primary)" [href]="bannerHref(primary)" [class.static]="!primary.promotionId && !primary.targetUrl"><img [src]="primary.imageUrl" [alt]="primary.title || 'Store promotion'" />@if(primary.title || primary.subtitle){<span>@if(primary.title){<strong>{{primary.title}}</strong>}@if(primary.subtitle){<small>{{primary.subtitle}}</small>}</span>}</a>
          }
          @if (banner('SECONDARY'); as secondary) {
            <a class="store-banner secondary" [routerLink]="bannerLink(secondary)" [href]="bannerHref(secondary)" [class.static]="!secondary.promotionId && !secondary.targetUrl"><img [src]="secondary.imageUrl" [alt]="secondary.title || 'Store promotion'" />@if(secondary.title || secondary.subtitle){<span>@if(secondary.title){<strong>{{secondary.title}}</strong>}@if(secondary.subtitle){<small>{{secondary.subtitle}}</small>}</span>}</a>
          }
        </section>
      }

      <div class="catalog-layout">
        <nav class="category-section" aria-label="Shop by category">
          <div class="category-heading"><span class="eyebrow">EXPLORE</span><h2>Shop by category</h2></div>
        @if (loading()) {
          <div class="chip-skeleton" role="status" aria-label="Loading categories">@for (item of skeletonChips; track $index) { <i></i> }</div>
        } @else {
          <div class="category-row">
            <button type="button" class="category-chip" [class.selected]="selectedCategory() === 'all'" [attr.aria-current]="selectedCategory() === 'all' ? 'true' : null" (click)="selectCategory('all')"><span class="category-icon">@if (store()?.allCategoryImageUrl && !failedCategoryImages().has('all')) { <img [src]="store()!.allCategoryImageUrl" alt="" (error)="categoryImageFailed('all')" /> } @else { <svg viewBox="0 0 24 24"><path d="M4 4h6v6H4zm10 0h6v6h-6zM4 14h6v6H4zm10 0h6v6h-6z"/></svg> }</span><small>All</small></button>
            @for (category of categories(); track category.id) {
              <button type="button" class="category-chip" [class.selected]="selectedCategory() === category.id" [attr.aria-current]="selectedCategory() === category.id ? 'true' : null" (click)="selectCategory(category.id)">
                <span class="category-icon">@if (category.imageUrl && !failedCategoryImages().has(category.id)) { <img [src]="category.imageUrl" alt="" (error)="categoryImageFailed(category.id)" /> } @else if (category.emoji) { {{ category.emoji }} } @else { <svg viewBox="0 0 24 24"><path d="M5 7.5 12 4l7 3.5v9L12 20l-7-3.5zM5 7.5l7 3.5 7-3.5M12 11v9"/></svg> }</span><small>{{ category.name }}</small>
              </button>
            }
          </div>
        }
      </nav>

      <section class="product-section" id="products" aria-labelledby="products-title">
        <div class="section-heading"><div><span class="eyebrow">SHOP</span><h2 id="products-title">{{ sectionTitle() }}</h2></div>@if (!loading() && !loadFailed()) { <span class="result-count">{{ filteredProducts().length }} {{ filteredProducts().length === 1 ? 'product' : 'products' }}</span> }</div>
        @if (loading()) {
          <div class="product-grid" role="status" aria-label="Loading products">@for (item of skeletonCards; track $index) { <div class="product-skeleton"><i></i><span></span><span></span><b></b></div> }</div>
        } @else if (loadFailed()) {
          <div class="catalog-state" role="alert"><span class="state-symbol">!</span><h3>We couldn't load the products</h3><p>There may be a temporary connection problem.</p><button type="button" (click)="load()">Retry</button></div>
        } @else if (filteredProducts().length) {
          <div class="product-grid">@for (product of filteredProducts(); track product.id) { <shop-product-card [product]="product" [storeKey]="storeKey" /> }</div>@if (hasMore() && !loadingMore()) { <div #loadMore class="load-more" aria-hidden="true"></div> } @if (loadingMore()) { <div class="loading-more" role="status">Loading more products...</div> }
        } @else if (!products().length) {
          <div class="catalog-state"><span class="state-symbol">!</span><h3>No products available yet</h3><p>This store is still preparing its online shelves. Please check again later.</p><button type="button" (click)="load()">Refresh</button></div>
        } @else {
          <div class="catalog-state"><span class="state-symbol">!</span><h3>No matching products</h3><p>Try another search or clear the selected category.</p><button type="button" (click)="resetFilters()">Clear filters</button></div>
        }
      </section>
      </div>
      @if (isMockData) { <p class="source-note">Development preview · Sample prices and availability</p> }
    }
  `,
  styles: [`
    :host{display:block}
    .catalog-layout{display:flex;min-width:0;flex-direction:column;margin-top:26px}.category-section,.product-section{width:100%;min-width:0}.category-section{margin-bottom:24px}.category-heading{display:flex;align-items:baseline;gap:11px;margin-bottom:12px}.category-heading h2{margin:0;color:var(--store-text);font:800 17px Manrope,sans-serif;letter-spacing:-.3px}.section-heading{display:flex;align-items:end;justify-content:space-between;gap:15px;margin-bottom:14px}.eyebrow{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.section-heading h1,.section-heading h2{margin:3px 0 0;color:var(--store-text);font:800 21px Manrope,sans-serif;letter-spacing:-.55px}.result-count{padding-bottom:2px;color:var(--store-muted);font-size:11px;white-space:nowrap}
    .store-banners{display:grid;gap:12px;margin-bottom:24px}.store-banner{display:block;overflow:hidden;border-radius:var(--store-radius-lg);color:var(--store-text);background:var(--store-surface);text-decoration:none}.store-banner.static{cursor:default}.store-banner img{display:block;width:100%;height:auto;aspect-ratio:5/2;object-fit:contain;background:var(--store-surface-muted)}.store-banner span{display:grid;gap:3px;padding:10px 12px}.store-banner strong{font:750 13px Manrope,sans-serif}.store-banner small{color:var(--store-muted);font-size:10px}.category-icon img{width:100%;height:100%;border-radius:inherit;object-fit:cover}
    .category-row{display:grid;grid-template-rows:repeat(2,auto);grid-auto-flow:column;grid-auto-columns:82px;gap:8px 12px;overflow-x:auto;overscroll-behavior-inline:contain;padding:2px 2px 10px;scrollbar-width:thin;scroll-snap-type:x proximity}.category-chip{display:grid;flex:0 0 82px;justify-items:center;align-content:start;gap:6px;min-width:0;border:1px solid transparent;border-radius:14px;padding:9px 5px;color:var(--store-text);background:transparent;cursor:pointer;scroll-snap-align:start;transition:background .16s,color .16s,border-color .16s}.category-chip:hover{background:var(--store-surface-muted)}.category-chip.selected{color:var(--store-primary-dark);border-color:var(--store-primary);background:var(--store-primary-soft)}.category-icon{display:grid;width:52px;height:52px;place-items:center;overflow:hidden;border-radius:13px;background:var(--store-surface-muted);font-size:21px}.selected .category-icon{background:#fff}.category-icon svg{width:23px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.6}.category-chip small{display:-webkit-box;max-width:100%;overflow:hidden;font-size:10px;font-weight:750;line-height:1.25;text-align:center;-webkit-box-orient:vertical;-webkit-line-clamp:2}
    .product-grid{display:grid;width:100%;grid-template-columns:repeat(5,minmax(0,1fr));gap:14px}.catalog-state{display:grid;min-height:270px;align-content:center;justify-items:center;border:1px dashed var(--store-border);border-radius:var(--store-radius-lg);padding:30px;text-align:center;background:var(--store-surface)}.state-symbol{display:grid;width:48px;height:48px;place-items:center;border-radius:15px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 23px Manrope,sans-serif}.catalog-state h3{margin:14px 0 5px;font:800 17px Manrope,sans-serif}.catalog-state p{max-width:360px;margin:0;color:var(--store-muted);font-size:12px;line-height:1.55}.catalog-state button{margin-top:16px;border:1px solid var(--store-primary);border-radius:10px;padding:9px 15px;color:var(--store-primary-dark);background:#fff;font-size:11px;font-weight:800;cursor:pointer}
    .chip-skeleton{display:flex;gap:12px;overflow:hidden}.chip-skeleton i{flex:0 0 82px;height:86px;border-radius:12px;background:#e9ede8}.product-skeleton{overflow:hidden;height:278px;border:1px solid var(--store-border);border-radius:17px;background:#fff}.product-skeleton i,.product-skeleton span,.product-skeleton b,.chip-skeleton i{display:block;background:linear-gradient(90deg,#ebeeea 25%,#f8f9f7 50%,#ebeeea 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.product-skeleton i{height:166px}.product-skeleton span{width:80%;height:11px;margin:13px 12px 0;border-radius:5px}.product-skeleton span+span{width:55%;margin-top:8px}.product-skeleton b{width:90px;height:28px;margin:16px 12px 0;border-radius:8px}@keyframes shimmer{to{background-position:-200% 0}}.source-note{margin:30px 0 0;color:#929b94;text-align:center;font-size:10px}
    @media(max-width:1050px){.product-grid{grid-template-columns:repeat(4,minmax(0,1fr))}}
    @media(max-width:850px){.product-grid{grid-template-columns:repeat(3,minmax(0,1fr))}}
    @media(max-width:700px){.catalog-layout{margin-top:20px}.category-section{margin-bottom:18px}.product-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:11px}.section-heading h1,.section-heading h2{font-size:19px}}
    @media(max-width:560px){.category-row{display:flex;gap:8px}.category-chip{flex-basis:74px;padding:7px 4px}.category-icon{width:46px;height:46px}.section-heading{flex-wrap:wrap;gap:3px}.result-count{font-size:10px}.product-skeleton{height:235px}.product-skeleton i{height:135px}.store-banners{gap:10px;margin-bottom:21px}.store-banner{border-radius:15px}.store-banner span{padding:9px 11px}.store-banner strong{font-size:12px}}
    @media(max-width:340px){.product-grid{grid-template-columns:minmax(0,1fr)}}@media(max-width:360px){.category-chip{flex-basis:68px}}
    @media(prefers-reduced-motion:reduce){.product-skeleton i,.product-skeleton span,.product-skeleton b,.chip-skeleton i{animation:none}}
  `],
})
export class StoreHomePage implements OnInit, AfterViewInit, OnDestroy {
  readonly store=signal<Store|null>(null);
  readonly categories = signal<Category[]>([]); readonly products = signal<Product[]>([]); readonly failedCategoryImages = signal<Set<string>>(new Set());
  readonly loading = signal(true); readonly loadingMore = signal(false); readonly hasMore = signal(true); readonly loadFailed = signal(false); readonly isMockData = environment.useMockData;
  readonly selectedCategory = signal('all'); readonly query = signal('');
  readonly skeletonCards = Array.from({ length: 10 }); readonly skeletonChips = Array.from({ length: 6 });
  storeKey = ''; private page = 0; private readonly pageSize = 24; private observer?: IntersectionObserver; private readonly destroyRef = inject(DestroyRef);
  readonly filteredProducts = computed(() => { const term=this.query().toLocaleLowerCase().trim(); return this.products().filter(product => (this.selectedCategory()==='all'||product.categoryId===this.selectedCategory())&&(!term||`${product.name} ${product.description}`.toLocaleLowerCase().includes(term))); });
readonly sectionTitle = computed(() => this.query() ? `Results for "${this.query()}"` : this.selectedCategory()==='all' ? 'All products' : this.categories().find(category=>category.id===this.selectedCategory())?.name ?? 'Products');
  constructor(private readonly route:ActivatedRoute,private readonly router:Router,private readonly location:Location,private readonly data:StorefrontDataService){}
  bannerLink(item:{promotionId?:string|null}):string[]|null{return item.promotionId?["/",this.storeKey,"offers",item.promotionId]:null;}
  bannerHref(item:{promotionId?:string|null;targetUrl?:string}):string|null{return item.promotionId?null:item.targetUrl||null;}
  banner(slot:'PRIMARY'|'SECONDARY'){return this.store()?.banners.find(item=>item.slot===slot);}
  categoryImageFailed(id:string):void{this.failedCategoryImages.update(current=>new Set(current).add(id));}
  ngAfterViewInit():void { this.observeSentinel(); }
  ngOnDestroy():void { this.observer?.disconnect(); }
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params:ParamMap)=>{this.query.set(params.get('q')??'');this.selectedCategory.set(params.get('category')??'all');});void this.load();}
  selectCategory(id:string):void{const tree=this.router.createUrlTree([], {relativeTo:this.route,queryParams:{category:id==='all'?null:id},queryParamsHandling:'merge'});this.location.replaceState(this.router.serializeUrl(tree));this.selectedCategory.set(id);}
  resetFilters():void{void this.router.navigate([], {relativeTo:this.route,queryParams:{q:null,category:null},queryParamsHandling:'merge'});}
async load():Promise<void>{this.loading.set(true);this.loadFailed.set(false);try{const[store,categories,products]=await Promise.all([this.data.getStore(this.storeKey),this.data.getCategories(this.storeKey),this.data.getProducts(this.storeKey, 1, this.pageSize)]);this.store.set(store);this.categories.set(categories);this.products.set(products.items); this.page = products.pageNumber; this.hasMore.set(products.hasMore); setTimeout(() => this.observeSentinel());}catch{this.loadFailed.set(true);}finally{this.loading.set(false);}}
  private observeSentinel(): void { const target = document.querySelector(".load-more"); if (target) { this.observer?.disconnect(); this.observer = new IntersectionObserver(entries => { if (entries[0]?.isIntersecting) void this.loadNextPage(); }); this.observer.observe(target); } }
  private async loadNextPage(): Promise<void> { if (this.loading() || this.loadingMore() || !this.hasMore()) return; this.loadingMore.set(true); try { const next = await this.data.getProducts(this.storeKey, this.page + 1, this.pageSize); const known = new Set(this.products().map(x => x.id)); this.products.update(items => [...items, ...next.items.filter(x => !known.has(x.id))]); this.page = next.pageNumber; this.hasMore.set(next.hasMore); setTimeout(() => this.observeSentinel()); } finally { this.loadingMore.set(false); } }
}

