import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Category, Product } from '../models/storefront.models';
import { StorefrontDataService } from '../data/storefront-data.service';
import { ProductCardComponent } from '../shared/product-card.component';
import { environment } from '../../environments/environment';

@Component({
  standalone: true,
  imports: [ProductCardComponent],
  template: `
    @if (storeKey) {
      <section class="store-intro" aria-label="Store information">
        <div><span class="status-dot"></span><strong>Ready for your order</strong><small>{{ deliveryMessage() }}</small></div>
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 12h16M14 6l6 6-6 6"/></svg>
      </section>

      <section class="category-section" aria-labelledby="category-title">
        <div class="section-heading"><div><span class="eyebrow">BROWSE</span><h1 id="category-title">Shop by category</h1></div></div>
        @if (loading()) {
          <div class="chip-skeleton" role="status" aria-label="Loading categories">@for (item of skeletonChips; track $index) { <i></i> }</div>
        } @else {
          <div class="category-strip">
            <button type="button" class="category-chip" [class.selected]="selectedCategory() === 'all'" (click)="selectCategory('all')"><span class="category-icon"><svg viewBox="0 0 24 24"><path d="M4 4h6v6H4zm10 0h6v6h-6zM4 14h6v6H4zm10 0h6v6h-6z"/></svg></span><small>All</small></button>
            @for (category of categories(); track category.id) {
              <button type="button" class="category-chip" [class.selected]="selectedCategory() === category.id" (click)="selectCategory(category.id)">
                <span class="category-icon">@if (category.emoji) { {{ category.emoji }} } @else { <svg viewBox="0 0 24 24"><path d="M5 7.5 12 4l7 3.5v9L12 20l-7-3.5zM5 7.5l7 3.5 7-3.5M12 11v9"/></svg> }</span><small>{{ category.name }}</small>
              </button>
            }
          </div>
        }
      </section>

      <section class="product-section" id="products" aria-labelledby="products-title">
        <div class="section-heading"><div><span class="eyebrow">SHOP</span><h2 id="products-title">{{ sectionTitle() }}</h2></div>@if (!loading() && !loadFailed()) { <span class="result-count">{{ filteredProducts().length }} {{ filteredProducts().length === 1 ? 'product' : 'products' }}</span> }</div>
        @if (loading()) {
          <div class="product-grid" role="status" aria-label="Loading products">@for (item of skeletonCards; track $index) { <div class="product-skeleton"><i></i><span></span><span></span><b></b></div> }</div>
        } @else if (loadFailed()) {
          <div class="catalog-state" role="alert"><span class="state-symbol">!</span><h3>We couldn't load the products</h3><p>There may be a temporary connection problem.</p><button type="button" (click)="load()">Retry</button></div>
        } @else if (filteredProducts().length) {
          <div class="product-grid">@for (product of filteredProducts(); track product.id) { <shop-product-card [product]="product" [storeKey]="storeKey" /> }</div>
        } @else if (!products().length) {
          <div class="catalog-state"><span class="state-symbol">○</span><h3>No products available yet</h3><p>This store is still preparing its online shelves. Please check again later.</p><button type="button" (click)="load()">Refresh</button></div>
        } @else {
          <div class="catalog-state"><span class="state-symbol">⌕</span><h3>No matching products</h3><p>Try another search or clear the selected category.</p><button type="button" (click)="resetFilters()">Clear filters</button></div>
        }
      </section>
      @if (isMockData) { <p class="source-note">Development preview · Sample prices and availability</p> }
    }
  `,
  styles: [`
    :host{display:block}.store-intro{display:flex;align-items:center;justify-content:space-between;min-height:66px;border:1px solid #dcebdd;border-radius:var(--store-radius-lg);padding:12px 18px;background:linear-gradient(100deg,var(--store-primary-soft),#f9fbf7)}.store-intro>div{display:grid;grid-template-columns:auto 1fr;align-items:center;column-gap:8px}.store-intro strong{font:750 13px Manrope,sans-serif}.store-intro small{grid-column:2;color:var(--store-muted);font-size:11px}.status-dot{grid-row:1/3;width:9px;height:9px;border-radius:50%;background:var(--store-success);box-shadow:0 0 0 4px rgba(22,132,75,.12)}.store-intro svg{width:22px;fill:none;stroke:var(--store-primary);stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}
    .category-section{margin-top:28px}.product-section{margin-top:31px}.section-heading{display:flex;align-items:end;justify-content:space-between;gap:15px;margin-bottom:14px}.eyebrow{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.section-heading h1,.section-heading h2{margin:3px 0 0;color:var(--store-text);font:800 21px Manrope,sans-serif;letter-spacing:-.55px}.result-count{padding-bottom:2px;color:var(--store-muted);font-size:11px;white-space:nowrap}
    .category-strip{display:flex;gap:10px;overflow-x:auto;padding:2px 1px 6px;scroll-snap-type:x proximity;scrollbar-width:none}.category-strip::-webkit-scrollbar{display:none}.category-chip{display:flex;flex:0 0 auto;align-items:center;gap:8px;min-height:46px;border:1px solid var(--store-border);border-radius:13px;padding:6px 13px 6px 7px;color:var(--store-text);background:var(--store-surface);scroll-snap-align:start;cursor:pointer;transition:border-color .16s,background .16s,color .16s}.category-chip:hover{border-color:#b9d7c4}.category-chip.selected{border-color:var(--store-primary);color:var(--store-primary-dark);background:var(--store-primary-soft)}.category-icon{display:grid;width:32px;height:32px;place-items:center;border-radius:9px;background:var(--store-surface-muted);font-size:17px}.selected .category-icon{background:#fff}.category-icon svg{width:17px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.6}.category-chip small{max-width:130px;font-size:11px;font-weight:700;white-space:nowrap}
    .product-grid{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:14px}.catalog-state{display:grid;min-height:270px;align-content:center;justify-items:center;border:1px dashed var(--store-border);border-radius:var(--store-radius-lg);padding:30px;text-align:center;background:var(--store-surface)}.state-symbol{display:grid;width:48px;height:48px;place-items:center;border-radius:15px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 23px Manrope,sans-serif}.catalog-state h3{margin:14px 0 5px;font:800 17px Manrope,sans-serif}.catalog-state p{max-width:360px;margin:0;color:var(--store-muted);font-size:12px;line-height:1.55}.catalog-state button{margin-top:16px;border:1px solid var(--store-primary);border-radius:10px;padding:9px 15px;color:var(--store-primary-dark);background:#fff;font-size:11px;font-weight:800;cursor:pointer}
    .chip-skeleton{display:flex;gap:10px;overflow:hidden}.chip-skeleton i{flex:0 0 112px;height:46px;border-radius:13px;background:#e9ede8}.product-skeleton{overflow:hidden;height:278px;border:1px solid var(--store-border);border-radius:17px;background:#fff}.product-skeleton i,.product-skeleton span,.product-skeleton b,.chip-skeleton i{display:block;background:linear-gradient(90deg,#ebeeea 25%,#f8f9f7 50%,#ebeeea 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.product-skeleton i{height:166px}.product-skeleton span{width:80%;height:11px;margin:13px 12px 0;border-radius:5px}.product-skeleton span+span{width:55%;margin-top:8px}.product-skeleton b{width:90px;height:28px;margin:16px 12px 0;border-radius:8px}@keyframes shimmer{to{background-position:-200% 0}}.source-note{margin:30px 0 0;color:#929b94;text-align:center;font-size:10px}
    @media(max-width:1050px){.product-grid{grid-template-columns:repeat(4,minmax(0,1fr))}}
    @media(max-width:760px){.store-intro{min-height:60px;padding:10px 14px}.category-section{margin-top:22px}.product-section{margin-top:25px}.product-grid{grid-template-columns:repeat(3,minmax(0,1fr));gap:11px}.section-heading h1,.section-heading h2{font-size:19px}}
    @media(max-width:560px){.product-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.category-strip{margin-right:-16px;padding-right:16px}.product-skeleton{height:235px}.product-skeleton i{height:135px}}
    @media(max-width:340px){.product-grid{gap:8px}.category-chip{padding-right:10px}.category-icon{width:30px;height:30px}}
    @media(prefers-reduced-motion:reduce){.product-skeleton i,.product-skeleton span,.product-skeleton b,.chip-skeleton i{animation:none}}
  `],
})
export class StoreHomePage implements OnInit {
  readonly categories = signal<Category[]>([]); readonly products = signal<Product[]>([]);
  readonly loading = signal(true); readonly loadFailed = signal(false); readonly isMockData = environment.useMockData;
  readonly selectedCategory = signal('all'); readonly query = signal(''); readonly deliveryMessage = signal('Fresh picks, close to home.');
  readonly skeletonCards = Array.from({ length: 10 }); readonly skeletonChips = Array.from({ length: 6 });
  storeKey = ''; private readonly destroyRef = inject(DestroyRef);
  readonly filteredProducts = computed(() => { const term=this.query().toLocaleLowerCase().trim(); return this.products().filter(product => (this.selectedCategory()==='all'||product.categoryId===this.selectedCategory())&&(!term||`${product.name} ${product.description}`.toLocaleLowerCase().includes(term))); });
  readonly sectionTitle = computed(() => this.query() ? `Results for “${this.query()}”` : this.selectedCategory()==='all' ? 'All products' : this.categories().find(category=>category.id===this.selectedCategory())?.name ?? 'Products');
  constructor(private readonly route:ActivatedRoute,private readonly router:Router,private readonly data:StorefrontDataService){}
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params:ParamMap)=>{this.query.set(params.get('q')??'');this.selectedCategory.set(params.get('category')??'all');});void this.load();}
  selectCategory(id:string):void{void this.router.navigate([], {relativeTo:this.route,queryParams:{category:id==='all'?null:id},queryParamsHandling:'merge'});}
  resetFilters():void{void this.router.navigate([], {relativeTo:this.route,queryParams:{q:null,category:null},queryParamsHandling:'merge'});}
  async load():Promise<void>{this.loading.set(true);this.loadFailed.set(false);try{const[store,categories,products]=await Promise.all([this.data.getStore(this.storeKey),this.data.getCategories(this.storeKey),this.data.getProducts(this.storeKey)]);if(store)this.deliveryMessage.set(store.deliveryMessage);this.categories.set(categories);this.products.set(products);}catch{this.loadFailed.set(true);}finally{this.loading.set(false);}}
}
