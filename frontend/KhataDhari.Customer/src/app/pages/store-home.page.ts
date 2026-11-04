import { AfterViewInit, Component, DestroyRef, ElementRef, HostListener, OnDestroy, OnInit, afterRenderEffect, computed, inject, signal, viewChild } from '@angular/core';
import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { AvailabilityAnnouncement, Category, Product, ProductPage, Store } from '../models/storefront.models';
import { StorefrontDataService } from '../data/storefront-data.service';
import { ProductCardComponent } from '../shared/product-card.component';
import { RouterLink } from '@angular/router';
import { environment } from '../../environments/environment';
import { CustomerRewards, CustomerRewardsService } from '../customer-rewards.service';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontActivityService } from '../storefront-activity.service';
import { StoreShell } from '../store-shell';
import { ScheduleAnnouncementComponent } from '../shared/schedule-announcement.component';
import { CampaignCategoryNavigationService } from '../shared/campaign-category-navigation.service';

@Component({
  standalone: true,
  imports: [ProductCardComponent, RouterLink, ScheduleAnnouncementComponent],
  template: `
    @if (storeKey) {
      @if (showRewards()) {
        @if (rewardsLoading()) {
          <section class="home-rewards rewards-skeleton" aria-label="Loading rewards"><i></i><i></i></section>
        } @else {
          <section class="home-rewards" aria-label="Customer rewards">
            <a class="home-reward-card coins" [routerLink]="rewardLink()" [queryParams]="rewardQuery()">
              <span class="home-reward-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M12 3v18M7 7.5C7 5.8 9.2 4.5 12 4.5s5 1.3 5 3-2.2 3-5 3-5 1.3-5 3 2.2 3 5 3 5-1.3 5-3"/></svg></span>
              <span class="home-reward-copy"><strong>My Coins</strong>@if(isCustomerAuthenticated() && customerRewards(); as rewards){<b>{{rewards.wallet?.availableCoins ?? 0}} Coins</b>} @else {<b>Sign in to view</b>}</span><span class="home-reward-arrow" aria-hidden="true">&rsaquo;</span>
            </a>
            <a class="home-reward-card refer" [routerLink]="rewardLink()" [queryParams]="rewardQuery()">
              <span class="home-reward-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M12 5.5v13M5.5 12h13M7 4.5h10l1 3-6 4-6-4zM6 11.5h12v8H6z"/></svg></span>
              <span class="home-reward-copy"><strong>Refer &amp; Earn</strong><b>{{isCustomerAuthenticated() ? 'Share with friends' : 'Sign in to earn'}}</b></span><span class="home-reward-arrow" aria-hidden="true">&rsaquo;</span>
            </a>
          </section>
        }
      }
      @if (visibleAnnouncements().length) { <section class="availability-announcements" aria-label="Scheduled ordering">@for (item of visibleAnnouncements(); track item.scheduleId) { <shop-schedule-announcement [item]="item" (boundary)="refreshScheduleAvailability()" /> }</section> }
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

      <section #productsSection class="product-section" id="products" aria-labelledby="products-title">
        <div class="section-heading"><div><span class="eyebrow">SHOP</span><h2 id="products-title">{{ sectionTitle() }}</h2></div>@if (!loading() && !loadFailed()) { <span class="result-count">{{ filteredProducts().length }} {{ filteredProducts().length === 1 ? 'product' : 'products' }}</span> }</div>
        @if (loading()) {
          <div class="product-grid" role="status" aria-label="Loading products">@for (item of skeletonCards; track $index) { <div class="product-skeleton"><i></i><span></span><span></span><b></b></div> }</div>
        } @else if (loadFailed()) {
          <div class="catalog-state" role="alert"><span class="state-symbol">!</span><h3>We couldn't load the products</h3><p>{{ loadError() }}</p><button type="button" (click)="retry()">Retry</button></div>
        } @else if (filteredProducts().length) {
          <div class="product-grid">@for (product of filteredProducts(); track product.id) { <shop-product-card [product]="product" [storeKey]="storeKey" /> }</div>@if (hasMore() && !loadingMore()) { <div #loadMore class="load-more" aria-hidden="true"></div> } @if (loadingMore()) { <div class="loading-more" role="status">Loading more products...</div> }
        } @else if (!products().length) {
          <div class="catalog-state"><span class="state-symbol">!</span><h3>No products available yet</h3><p>This store is still preparing its online shelves. Please check again later.</p><button type="button" (click)="load()">Refresh</button></div>
        } @else {
          <div class="catalog-state"><span class="state-symbol">!</span><h3>We couldn't find this product.</h3><p>Try another search or request it from this retailer.</p><button type="button" (click)="requestMissingProduct()">Request This Product</button><button type="button" (click)="resetFilters()">Clear filters</button>@if(missingMessage()){<p role="status">{{missingMessage()}}</p>}</div>
        }
      </section>
      </div>
      @if (isMockData) { <p class="source-note">Development preview Ã‚&middot; Sample prices and availability</p> }
    }
  `,
  styles: [`
    .availability-announcements{display:grid;gap:10px;margin:0 0 18px}.availability-announcement{display:flex;align-items:center;justify-content:space-between;gap:14px;border:1px solid #cce6d2;border-radius:15px;padding:13px 15px;background:#f5fbf6}.availability-announcement h2{margin:3px 0 2px;font:800 16px Manrope,sans-serif}.availability-announcement p{margin:0;color:var(--store-muted);font-size:11px}.availability-status{white-space:nowrap;border-radius:99px;padding:6px 9px;color:var(--store-primary-dark);background:#dcefe1;font-size:10px;font-weight:800}
    :host{display:block}
    .home-rewards{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin:0 0 18px}.home-reward-card{display:grid;grid-template-columns:auto minmax(0,1fr) auto;align-items:center;gap:9px;min-height:70px;border:1px solid var(--store-border);border-radius:15px;padding:10px 11px;color:var(--store-text);background:#fff;box-shadow:0 4px 12px rgba(31,48,39,.05);text-decoration:none}.home-reward-card.coins{border-color:#ead9a9;background:#fffaf0}.home-reward-card.refer{border-color:#bfe1c8;background:#f4fbf5}.home-reward-icon{display:grid;width:34px;height:34px;place-items:center;border-radius:11px;color:#8c6917;background:#f6e8b5}.home-reward-card.refer .home-reward-icon{color:var(--store-primary-dark);background:#dcefe1}.home-reward-icon svg{width:20px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.7}.home-reward-copy{display:grid;min-width:0;gap:3px}.home-reward-copy strong{font:800 11px Manrope,sans-serif}.home-reward-copy b{overflow:hidden;color:var(--store-muted);font-size:10px;text-overflow:ellipsis;white-space:nowrap}.home-reward-card.coins .home-reward-copy b{color:#8c6917}.home-reward-arrow{color:var(--store-muted);font-size:24px;line-height:1}.rewards-skeleton{grid-template-columns:repeat(2,minmax(0,1fr))}.rewards-skeleton i{height:70px;border-radius:15px;background:linear-gradient(90deg,#ebeeea 25%,#f8f9f7 50%,#ebeeea 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.catalog-layout{display:flex;min-width:0;flex-direction:column;margin-top:26px}.category-section,.product-section{width:100%;min-width:0}.category-section{margin-bottom:24px}.category-heading{display:flex;align-items:baseline;gap:11px;margin-bottom:12px}.category-heading h2{margin:0;color:var(--store-text);font:800 17px Manrope,sans-serif;letter-spacing:-.3px}.section-heading{display:flex;align-items:end;justify-content:space-between;gap:15px;margin-bottom:14px}.eyebrow{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.section-heading h1,.section-heading h2{margin:3px 0 0;color:var(--store-text);font:800 21px Manrope,sans-serif;letter-spacing:-.55px}.result-count{padding-bottom:2px;color:var(--store-muted);font-size:11px;white-space:nowrap}
    .store-banners{display:grid;gap:12px;margin-bottom:24px}.store-banner{display:block;overflow:hidden;border-radius:var(--store-radius-lg);color:var(--store-text);background:var(--store-surface);text-decoration:none}.store-banner.static{cursor:default}.store-banner img{display:block;width:100%;height:auto;aspect-ratio:5/2;object-fit:contain;background:var(--store-surface-muted)}.store-banner span{display:grid;gap:3px;padding:10px 12px}.store-banner strong{font:750 13px Manrope,sans-serif}.store-banner small{color:var(--store-muted);font-size:10px}.category-icon img{width:100%;height:100%;border-radius:inherit;object-fit:cover}
    .category-row{display:grid;grid-template-rows:repeat(2,auto);grid-auto-flow:column;grid-auto-columns:82px;gap:8px 12px;overflow-x:auto;overscroll-behavior-inline:contain;padding:2px 2px 10px;scrollbar-width:thin;scroll-snap-type:x proximity}.category-chip{display:grid;flex:0 0 82px;justify-items:center;align-content:start;gap:6px;min-width:0;border:1px solid transparent;border-radius:14px;padding:9px 5px;color:var(--store-text);background:transparent;cursor:pointer;scroll-snap-align:start;transition:background .16s,color .16s,border-color .16s}.category-chip:hover{background:var(--store-surface-muted)}.category-chip.selected{color:var(--store-primary-dark);border-color:var(--store-primary);background:var(--store-primary-soft)}.category-icon{display:grid;width:52px;height:52px;place-items:center;overflow:hidden;border-radius:13px;background:var(--store-surface-muted);font-size:21px}.selected .category-icon{background:#fff}.category-icon svg{width:23px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.6}.category-chip small{display:-webkit-box;max-width:100%;overflow:hidden;font-size:10px;font-weight:750;line-height:1.25;text-align:center;-webkit-box-orient:vertical;-webkit-line-clamp:2}
    .product-grid{display:grid;width:100%;grid-template-columns:repeat(5,minmax(0,1fr));gap:14px}.catalog-state{display:grid;min-height:270px;align-content:center;justify-items:center;border:1px dashed var(--store-border);border-radius:var(--store-radius-lg);padding:30px;text-align:center;background:var(--store-surface)}.state-symbol{display:grid;width:48px;height:48px;place-items:center;border-radius:15px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 23px Manrope,sans-serif}.catalog-state h3{margin:14px 0 5px;font:800 17px Manrope,sans-serif}.catalog-state p{max-width:360px;margin:0;color:var(--store-muted);font-size:12px;line-height:1.55}.catalog-state button{margin-top:16px;border:1px solid var(--store-primary);border-radius:10px;padding:9px 15px;color:var(--store-primary-dark);background:#fff;font-size:11px;font-weight:800;cursor:pointer}
    .chip-skeleton{display:flex;gap:12px;overflow:hidden}.chip-skeleton i{flex:0 0 82px;height:86px;border-radius:12px;background:#e9ede8}.product-skeleton{overflow:hidden;height:278px;border:1px solid var(--store-border);border-radius:17px;background:#fff}.product-skeleton i,.product-skeleton span,.product-skeleton b,.chip-skeleton i{display:block;background:linear-gradient(90deg,#ebeeea 25%,#f8f9f7 50%,#ebeeea 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.product-skeleton i{height:166px}.product-skeleton span{width:80%;height:11px;margin:13px 12px 0;border-radius:5px}.product-skeleton span+span{width:55%;margin-top:8px}.product-skeleton b{width:90px;height:28px;margin:16px 12px 0;border-radius:8px}@keyframes shimmer{to{background-position:-200% 0}}.source-note{margin:30px 0 0;color:#929b94;text-align:center;font-size:10px}
    @media(max-width:1050px){.product-grid{grid-template-columns:repeat(4,minmax(0,1fr))}}
    @media(max-width:850px){.product-grid{grid-template-columns:repeat(3,minmax(0,1fr))}}
    @media(max-width:700px){.catalog-layout{margin-top:20px}.category-section{margin-bottom:18px}.product-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:11px}.section-heading h1,.section-heading h2{font-size:19px}}
    @media(max-width:560px){.category-row{display:flex;gap:8px}.category-chip{flex-basis:74px;padding:7px 4px}.category-icon{width:46px;height:46px}.section-heading{flex-wrap:wrap;gap:3px}.result-count{font-size:10px}.product-skeleton{height:235px}.product-skeleton i{height:135px}.store-banners{gap:10px;margin-bottom:21px}.store-banner{border-radius:15px}.store-banner span{padding:9px 11px}.store-banner strong{font-size:12px}}
    @media(max-width:340px){.product-grid{grid-template-columns:minmax(0,1fr)}}@media(max-width:360px){.category-chip{flex-basis:68px}}
`],
})
export class StoreHomePage implements OnInit, AfterViewInit, OnDestroy {
  readonly store=signal<Store|null>(null);
  readonly categories = signal<Category[]>([]); readonly products = signal<Product[]>([]); readonly failedCategoryImages = signal<Set<string>>(new Set());
  readonly loading = signal(true); readonly loadingMore = signal(false); readonly hasMore = signal(true); readonly loadFailed = signal(false); readonly loadError = signal('There may be a temporary connection problem.'); readonly isMockData = environment.useMockData;
  private retryBlockedUntil = 0;
  readonly selectedCategory = signal('all'); readonly query = signal(''); readonly missingMessage=signal('');
  readonly customerRewards = signal<CustomerRewards|null>(null); readonly rewardsLoading = signal(false); readonly announcements = signal<AvailabilityAnnouncement[]>([]);
  readonly visibleAnnouncements=computed(()=>this.announcements().filter(item=>this.filteredProducts().some(product=>item.productIds.includes(product.id)&&(item.status.startsWith('ORDERING_OPEN')||product.canOrderNow===false))));
  private refreshingSchedules=false;
  async refreshScheduleAvailability():Promise<void>{if(this.refreshingSchedules)return;this.refreshingSchedules=true;try{await Promise.all([this.loadAnnouncements(),this.load()]);}finally{this.refreshingSchedules=false;}}
  readonly showRewards = computed(() => this.store()?.customerRewardsEnabled === true && (!this.isCustomerAuthenticated() || this.rewardsLoading() || this.customerRewards()?.enabled === true));
  readonly skeletonCards = Array.from({ length: 10 }); readonly skeletonChips = Array.from({ length: 6 });
  storeKey = ''; private page = 0; private readonly pageSize = 24; private observer?: IntersectionObserver; private availabilityTimer?: ReturnType<typeof setTimeout>; private readonly destroyRef = inject(DestroyRef);
  readonly filteredProducts = computed(() => { const term=this.query().toLocaleLowerCase().trim(); return this.products().filter(product => !term||`${product.name} ${product.description}`.toLocaleLowerCase().includes(term)); });
readonly sectionTitle = computed(() => this.query() ? `Results for "${this.query()}"` : this.selectedCategory()==='all' ? 'All products' : 'Products');
  constructor(private readonly route:ActivatedRoute,private readonly router:Router,private readonly location:Location,private readonly data:StorefrontDataService,private readonly session:CustomerSessionService,private readonly rewards:CustomerRewardsService,private readonly activity:StorefrontActivityService,private readonly shell:StoreShell,private readonly campaignNavigation:CampaignCategoryNavigationService){afterRenderEffect(()=>this.scrollToCampaignProducts());}
  bannerLink(item:{promotionId?:string|null}):string[]|null{return item.promotionId?["/",this.storeKey,"offers",item.promotionId]:null;}
  bannerHref(item:{promotionId?:string|null;targetUrl?:string}):string|null{return item.promotionId?null:item.targetUrl||null;}
  banner(slot:'PRIMARY'|'SECONDARY'){return this.store()?.banners.find(item=>item.slot===slot);}
  categoryImageFailed(id:string):void{this.failedCategoryImages.update(current=>new Set(current).add(id));}
  ngAfterViewInit():void { this.observeSentinel(); }
  @HostListener('document:visibilitychange') onResume():void { if(document.visibilityState==='visible'&&!this.loading())void this.refreshScheduleAvailability(); }
  ngOnDestroy():void { ++this.catalogueRequest;if(this.campaignNavigation.pending())this.campaignNavigation.cancel();this.observer?.disconnect(); if(this.availabilityTimer)clearTimeout(this.availabilityTimer); }
  private searchTimer?: ReturnType<typeof setTimeout>;
  private catalogueRequest=0;
  private routeInitialized=false;
  private readonly loadedCategory=signal<string|null>(null);
  private readonly productSection=viewChild<ElementRef<HTMLElement>>('productsSection');
  private scrollToCampaignProducts():void{
    const target=this.campaignNavigation.pending(),section=this.productSection()?.nativeElement;
    if(!target||!section||target.storeKey!==this.storeKey||target.categoryId!==this.selectedCategory()||this.loading()||this.loadFailed()||this.loadedCategory()!==target.categoryId||this.router.getCurrentNavigation())return;
    if(!this.campaignNavigation.consume(target.sequence))return;
    const header=section.closest('.store-frame')?.querySelector('.store-header');
    section.style.scrollMarginTop=`${Math.ceil(header?.getBoundingClientRect().height??0)+16}px`;
    section.scrollIntoView({block:'start',behavior:window.matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});
  }
  @HostListener('window:wheel') @HostListener('window:touchstart') @HostListener('window:pointerdown') cancelCampaignScroll():void{this.campaignNavigation.cancel();}
  @HostListener('window:keydown',['$event']) cancelCampaignScrollKey(event:KeyboardEvent):void{if(['ArrowUp','ArrowDown','PageUp','PageDown','Home','End',' '].includes(event.key))this.cancelCampaignScroll();}
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';void this.loadRewards();void this.loadAnnouncements();this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params:ParamMap)=>{const category=params.get('category')??'all';const reload=!this.routeInitialized||this.selectedCategory()!==category;this.routeInitialized=true;const query=params.get('q')??'';this.query.set(query);this.selectedCategory.set(category);if(reload)void this.load();if(category!=='all')this.activity.record(this.storeKey,'CATEGORY_VIEW',undefined,undefined,category);clearTimeout(this.searchTimer);if(query.trim().length>=2)this.searchTimer=setTimeout(()=>this.activity.recordDemandSearch(this.storeKey,query.trim(),this.filteredProducts().length),500);});}
  isCustomerAuthenticated():boolean{return this.session.isAuthenticated(this.storeKey);}
  rewardLink():string[]{return ['/',this.storeKey,'account','rewards'];}
  rewardQuery():Record<string,string>|null{return this.isCustomerAuthenticated()?null:{returnUrl:'/'+this.storeKey+'/account/rewards'};}
  private async loadRewards():Promise<void>{if(!this.isCustomerAuthenticated())return;this.customerRewards.set(null);this.rewardsLoading.set(true);try{await this.session.restore(this.storeKey);if(!this.session.active())return;this.customerRewards.set(await this.rewards.get(this.storeKey));}catch{this.customerRewards.set(null);}finally{this.rewardsLoading.set(false);}}
  private async loadAnnouncements():Promise<void>{try{const items=await this.data.getAvailabilityAnnouncements(this.storeKey);this.announcements.set(items.map(item=>({...item,clientReceivedAtUtc:Date.now()})));}catch{this.announcements.set([]);}}
  selectCategory(id:string):void{this.campaignNavigation.cancel();const tree=this.router.createUrlTree([], {relativeTo:this.route,queryParams:{category:id==='all'?null:id},queryParamsHandling:'merge'});this.location.replaceState(this.router.serializeUrl(tree));this.selectedCategory.set(id);if(id!=='all')this.activity.record(this.storeKey,'CATEGORY_VIEW',undefined,undefined,id);void this.load();}
  resetFilters():void{void this.router.navigate([], {relativeTo:this.route,queryParams:{q:null,category:null},queryParamsHandling:'merge'});}
  async requestMissingProduct():Promise<void>{const keyword=this.query().trim();if(keyword.length<2){this.missingMessage.set('Search for a product name first.');return;}try{await this.data.requestMissingProduct(this.storeKey,keyword);this.missingMessage.set('Product request submitted.');}catch(error:any){this.missingMessage.set(error?.error?.message??'Product request could not be submitted.');}}
  async load():Promise<void>{
    const remaining=this.retryBlockedUntil-Date.now();if(remaining>0){this.loadError.set(`The catalogue is temporarily busy. Please retry in ${Math.ceil(remaining/1000)} seconds.`);return;}
    const request=++this.catalogueRequest,key=this.storeKey,category=this.selectedCategory();
    this.loading.set(true);this.loadFailed.set(false);this.loadedCategory.set(null);this.loadError.set('There may be a temporary connection problem.');
    try{
      const store=await this.shell.waitForStore(key);if(!store)throw new Error('Store metadata unavailable');
      if(request!==this.catalogueRequest)return;
      const [categories,products]=await Promise.all([this.data.getCategories(key),this.data.getProducts(key,1,this.pageSize,category==='all'?undefined:category)]);
      if(request!==this.catalogueRequest||key!==this.storeKey||category!==this.selectedCategory())return;
      this.store.set(store);this.categories.set(categories);this.products.set(products.items);this.scheduleAvailabilityRefresh(products.items);
      this.page=products.pageNumber;this.hasMore.set(products.hasMore);this.loadedCategory.set(category);setTimeout(()=>this.observeSentinel());
    }catch(error){
      if(request!==this.catalogueRequest)return;
      this.loadFailed.set(true);const response=error instanceof HttpErrorResponse?error:null;const retryAfter=Number(response?.headers.get('Retry-After')??'');
      if(response?.status===429){const delay=Number.isFinite(retryAfter)&&retryAfter>0?Math.min(retryAfter,120)*1000:5000;this.retryBlockedUntil=Date.now()+delay;this.loadError.set('The catalogue is temporarily busy. Please wait a moment and try again.');}
      else{this.retryBlockedUntil=0;this.loadError.set('There may be a temporary connection problem.');}
    }finally{if(request===this.catalogueRequest)this.loading.set(false);}
  }
  private scheduleAvailabilityRefresh(items:Product[]):void{if(this.availabilityTimer)clearTimeout(this.availabilityTimer);const next=items.map(x=>x.availabilityNextTransition?Date.parse(x.availabilityNextTransition):NaN).filter(Number.isFinite).sort((a,b)=>a-b)[0];if(next){const delay=Math.max(1000,Math.min(next-Date.now()+500,24*60*60*1000));this.availabilityTimer=setTimeout(()=>void this.load(),delay);}}
  retry():void{void this.load();}
  private observeSentinel(): void { const target = document.querySelector(".load-more"); if (target) { this.observer?.disconnect(); this.observer = new IntersectionObserver(entries => { if (entries[0]?.isIntersecting) void this.loadNextPage(); }); this.observer.observe(target); } }
  private async loadNextPage(): Promise<void> { if (this.loading() || this.loadingMore() || !this.hasMore()) return; this.loadingMore.set(true);const request=this.catalogueRequest,category=this.selectedCategory(); try { const next = await this.data.getProducts(this.storeKey, this.page + 1, this.pageSize, category==='all'?undefined:category); if(request!==this.catalogueRequest||category!==this.selectedCategory())return; const known = new Set(this.products().map(x => x.id)); this.products.update(items => [...items, ...next.items.filter(x => !known.has(x.id))]); this.page = next.pageNumber; this.hasMore.set(next.hasMore); setTimeout(() => this.observeSentinel()); } finally { this.loadingMore.set(false); } }
}

