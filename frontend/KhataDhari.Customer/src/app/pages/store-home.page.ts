import { AfterViewInit, Component, DestroyRef, ElementRef, HostListener, OnDestroy, OnInit, afterRenderEffect, computed, inject, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { AvailabilityAnnouncement, Category, Product, ProductCatalogFilters, ProductPage, ProductSortOption, Store } from '../models/storefront.models';
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
import { markStorefrontStartup, scheduleStorefrontIdle } from '../storefront-startup-performance';

interface CatalogFilterState { categoryId: string; minPrice: number | null; maxPrice: number | null; inStockOnly: boolean; sortBy: ProductSortOption; }
const defaultCatalogFilters = (): CatalogFilterState => ({ categoryId: 'all', minPrice: null, maxPrice: null, inStockOnly: false, sortBy: 'RECOMMENDED' });

@Component({
  standalone: true,
  imports: [ProductCardComponent, RouterLink, ScheduleAnnouncementComponent],
  template: `
    @if (storeKey) {
      @if (visibleAnnouncements().length) { <section class="availability-announcements" aria-label="Scheduled ordering">@for (item of visibleAnnouncements(); track item.scheduleId) { <shop-schedule-announcement [item]="item" (boundary)="refreshScheduleAvailability()" /> }</section> }
      @if (banner('PRIMARY') || banner('SECONDARY')) {
        <section class="store-banners" aria-label="Store promotions">
          @if (banner('PRIMARY'); as primary) {
              <a class="store-banner primary" [routerLink]="bannerLink(primary)" [href]="bannerHref(primary)" [class.static]="!primary.promotionId && !primary.targetUrl"><picture><source type="image/webp" media="(max-width: 420px) and (max-resolution: 1.5dppx)" [srcset]="failedBannerImages().has('PRIMARY') ? null : bannerThumbnail(primary)" /><img [src]="primary.imageUrl" [alt]="primary.title || 'Store promotion'" width="1200" height="480" loading="eager" fetchpriority="high" decoding="async" (error)="bannerImageFailed('PRIMARY')" /></picture>@if(primary.title || primary.subtitle){<span class="banner-copy"><small>FEATURED</small>@if(primary.title){<strong>{{primary.title}}</strong>}@if(primary.subtitle){<span>{{primary.subtitle}}</span>}@if(primary.promotionId || primary.targetUrl){<b>Shop now <span aria-hidden="true">&rarr;</span></b>}</span>}</a>
          }
          @if (banner('SECONDARY'); as secondary) {
            <a class="store-banner secondary" [routerLink]="bannerLink(secondary)" [href]="bannerHref(secondary)" [class.static]="!secondary.promotionId && !secondary.targetUrl"><picture><source type="image/webp" media="(max-width: 420px) and (max-resolution: 1.5dppx)" [srcset]="failedBannerImages().has('SECONDARY') ? null : bannerThumbnail(secondary)" /><img [src]="secondary.imageUrl" [alt]="secondary.title || 'Store promotion'" width="1200" height="480" loading="lazy" fetchpriority="low" decoding="async" (error)="bannerImageFailed('SECONDARY')" /></picture>@if(secondary.title || secondary.subtitle){<span class="banner-copy"><small>FEATURED</small>@if(secondary.title){<strong>{{secondary.title}}</strong>}@if(secondary.subtitle){<span>{{secondary.subtitle}}</span>}@if(secondary.promotionId || secondary.targetUrl){<b>Shop now <span aria-hidden="true">&rarr;</span></b>}</span>}</a>
          }
        </section>
      }
      @if (showRewards()) {
        @if (rewardsLoading()) {
          <section class="home-rewards rewards-skeleton" aria-label="Loading rewards"><i></i><i></i></section>
        } @else {
          <section class="home-rewards" aria-label="Customer rewards">
            <a class="home-reward-card coins" [routerLink]="rewardLink()" [queryParams]="rewardQuery()">
              <span class="home-reward-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 3v18M7 7.5C7 5.8 9.2 4.5 12 4.5s5 1.3 5 3-2.2 3-5 3-5 1.3-5 3 2.2 3 5 3 5-1.3 5-3"/></svg></span>
              <span class="home-reward-copy"><strong>My Coins</strong>@if(isCustomerAuthenticated() && customerRewards(); as rewards){<b>{{rewards.wallet?.availableCoins ?? 0}} Coins</b>} @else {<b>Sign in to view</b>}</span><span class="home-reward-arrow" aria-hidden="true">&rsaquo;</span>
            </a>
            <a class="home-reward-card refer" [routerLink]="rewardLink()" [queryParams]="rewardQuery()">
              <span class="home-reward-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M12 5.5v13M5.5 12h13M7 4.5h10l1 3-6 4-6-4zM6 11.5h12v8H6z"/></svg></span>
              <span class="home-reward-copy"><strong>Refer &amp; Earn</strong><b>Invite friends</b></span><span class="home-reward-arrow" aria-hidden="true">&rsaquo;</span>
            </a>
          </section>
        }
      }

      <section class="trust-strip" aria-label="Shopping benefits">
        @if(store()?.freeDeliveryEnabled && store()?.freeDeliveryThreshold; as threshold){<div class="trust-item"><span class="trust-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M3 7h11v11H3zM14 11h4l3 3v4h-7M7 18.5a1.5 1.5 0 1 0 0 .01M18 18.5a1.5 1.5 0 1 0 0 .01"/></svg></span><span><strong>Free delivery</strong><small>Above &#8377;{{threshold}}</small></span></div>}
        <div class="trust-item"><span class="trust-icon shield" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="m12 3 8 3v5c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V6zM8.5 12l2.3 2.3 4.8-5"/></svg></span><span><strong>Secure shopping</strong><small>Shop with confidence</small></span></div>
        @if(returnWindowDays() !== null){<div class="trust-item"><span class="trust-icon returns" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M4 7v5h5M4.7 12A8 8 0 1 1 7 17.7"/><path d="M12 8v4l2.5 1.5"/></svg></span><span><strong>Easy returns</strong><small>@if(returnWindowDays() === 0){Eligible items · delivery day} @else {Eligible items · {{returnWindowDays()}} days}</small></span></div>}
      </section>

      <div class="catalog-layout">
        <nav #categorySection id="home-categories" class="category-section" aria-label="Shop by category">
          <div class="category-heading"><h2>Shop by Category</h2><button type="button" class="view-all" [attr.aria-expanded]="categoriesExpanded()" (click)="toggleCategories()">@if(categoriesExpanded()){Show less <span aria-hidden="true">&minus;</span>} @else {View all <span aria-hidden="true">&rarr;</span>}</button></div>
        @if (loading()) {
          <div class="chip-skeleton" role="status" aria-label="Loading categories">@for (item of skeletonChips; track $index) { <i></i> }</div>
        } @else {
          <div class="category-row" [class.expanded]="categoriesExpanded()">
            <button type="button" class="category-chip" [class.selected]="selectedCategory() === 'all'" [attr.aria-current]="selectedCategory() === 'all' ? 'true' : null" (click)="selectCategory('all')"><span class="category-icon">@if (store()?.allCategoryImageUrl && !failedCategoryImages().has('all')) { <picture><source type="image/webp" [srcset]="failedCategoryThumbnails().has('all') ? null : presentationThumbnail(store()!.allCategoryImageUrl!)" /><img [src]="store()!.allCategoryImageUrl" alt="" width="300" height="300" loading="lazy" decoding="async" (error)="categoryImageLoadFailed('all',$event)" /></picture> } @else { <svg viewBox="0 0 24 24"><path d="M4 4h6v6H4zm10 0h6v6h-6zM4 14h6v6H4zm10 0h6v6h-6z"/></svg> }</span><small>All</small></button>
            @for (category of categories(); track category.id) {
              <button type="button" class="category-chip" [class.selected]="selectedCategory() === category.id" [attr.aria-current]="selectedCategory() === category.id ? 'true' : null" (click)="selectCategory(category.id)">
                <span class="category-icon">@if (category.imageUrl && !failedCategoryImages().has(category.id)) { <picture><source type="image/webp" [srcset]="failedCategoryThumbnails().has(category.id) ? null : presentationThumbnail(category.imageUrl)" /><img [src]="category.imageUrl" alt="" width="300" height="300" loading="lazy" decoding="async" (error)="categoryImageLoadFailed(category.id,$event)" /></picture> } @else if (category.emoji) { {{ category.emoji }} } @else { <svg viewBox="0 0 24 24"><path d="M5 7.5 12 4l7 3.5v9L12 20l-7-3.5zM5 7.5l7 3.5 7-3.5M12 11v9"/></svg> }</span><small>{{ category.name }}</small>
              </button>
            }
          </div>
        }
      </nav>

      <section #productsSection class="product-section" id="products" aria-labelledby="products-title">
        <div class="section-heading"><div class="product-heading-copy"><h2 id="products-title">Explore Products</h2>@if(query() || selectedCategory() !== 'all'){<small>{{sectionTitle()}}</small>}</div><div class="product-heading-actions">@if (!loading() && !loadFailed()) { <span class="result-count" aria-live="polite">{{ displayedCount() }} {{ displayedCount() === 1 ? 'product' : 'products' }}</span> }<button #filterTrigger type="button" class="filter-button" [attr.aria-expanded]="filterOpen()" aria-haspopup="dialog" (click)="openFilter($event)"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h10M18 7h2M4 17h2m4 0h10M14 4v6M8 14v6"/></svg>Filter@if(activeFilterCount()){<span class="active-filter-count">{{activeFilterCount()}}</span>}</button></div></div>
        @if (loading()) {
          <div class="product-grid" role="status" aria-label="Loading products">@for (item of skeletonCards; track $index) { <div class="product-skeleton"><i></i><span></span><span></span><b></b></div> }</div>
        } @else if (loadFailed()) {
          <div class="catalog-state" role="alert"><span class="state-symbol">!</span><h3>We couldn't load the products</h3><p>{{ loadError() }}</p><button type="button" (click)="retry()">Retry</button></div>
        } @else if (filteredProducts().length) {
          <div class="product-grid">@for (product of filteredProducts(); track product.id; let index = $index) { <shop-product-card [product]="product" [storeKey]="storeKey" [imagePriority]="index === 0 && !banner('PRIMARY')" [eagerImage]="index < 2" /> }</div>@if (hasMore() && !loadingMore()) { <div #loadMore class="load-more" aria-hidden="true"></div> } @if (loadingMore()) { <div class="loading-more" role="status">Loading more products...</div> }
        } @else if (!serverTotalCount() && !activeFilterCount() && !query()) {
          <div class="catalog-state"><span class="state-symbol">!</span><h3>No products available yet</h3><p>This store is still preparing its online shelves. Please check again later.</p><button type="button" (click)="load()">Refresh</button></div>
        } @else {
          <div class="catalog-state"><span class="state-symbol">!</span><h3>{{activeFilterCount() ? 'No products match these filters' : 'We couldn’t find this product'}}</h3><p>{{activeFilterCount() ? 'Try changing your filters to see more products.' : 'Try another search or request it from this retailer.'}}</p>@if(activeFilterCount()){<button type="button" (click)="clearAppliedFilters()">Clear filters</button>} @else {<button type="button" (click)="requestMissingProduct()">Request This Product</button>}@if(missingMessage()){<p role="status">{{missingMessage()}}</p>}</div>
        }
      </section>
      </div>
      @if(filterOpen()){
        <div class="filter-overlay" (click)="closeFilter()">
          <section #filterPanel class="filter-panel" role="dialog" aria-modal="true" aria-labelledby="filter-title" (click)="$event.stopPropagation()" (keydown)="trapFilterFocus($event)">
            <header class="filter-panel-header"><h2 id="filter-title">Filter &amp; Sort</h2><button #filterClose type="button" class="filter-close" aria-label="Close filters" (click)="closeFilter()">&times;</button></header>
            <div class="filter-panel-body">
              <fieldset class="filter-group"><legend>Category</legend><label class="filter-select-label" for="catalog-filter-category">Choose a category</label><select id="catalog-filter-category" [value]="draftFilters().categoryId" (change)="updateDraftCategory($event)"><option value="all">All categories</option>@for(category of categories();track category.id){<option [value]="category.id">{{category.name}}</option>}</select></fieldset>
              <fieldset class="filter-group"><legend>Price range</legend><div class="filter-price-row"><label for="catalog-filter-minimum">Minimum price (₹)<input id="catalog-filter-minimum" type="number" inputmode="decimal" min="0" [attr.max]="priceBounds().maximum" [placeholder]="priceBounds().minimum === null ? 'No minimum' : priceBounds().minimum" [value]="draftFilters().minPrice ?? ''" (input)="updateDraftPrice('minPrice',$event)" /></label><label for="catalog-filter-maximum">Maximum price (₹)<input id="catalog-filter-maximum" type="number" inputmode="decimal" min="0" [attr.max]="priceBounds().maximum" [placeholder]="priceBounds().maximum === null ? 'No maximum' : priceBounds().maximum" [value]="draftFilters().maxPrice ?? ''" (input)="updateDraftPrice('maxPrice',$event)" /></label></div>@if(filterError()){<p class="filter-error" role="alert">{{filterError()}}</p>}@else if(priceBounds().minimum !== null && priceBounds().maximum !== null){<small class="filter-range-hint">Catalog prices: ₹{{priceBounds().minimum}}–₹{{priceBounds().maximum}}</small>}</fieldset>
              <fieldset class="filter-group"><legend>Availability</legend><label class="filter-check"><input type="checkbox" [checked]="draftFilters().inStockOnly" (change)="updateDraftStock($event)" /><span>In-stock products only</span></label></fieldset>
              <fieldset class="filter-group"><legend>Sort by</legend><label class="filter-select-label" for="catalog-filter-sort">Product order</label><select id="catalog-filter-sort" [value]="draftFilters().sortBy" (change)="updateDraftSort($event)"><option value="RECOMMENDED">Recommended</option><option value="PRICE_LOW_TO_HIGH">Price: Low to High</option><option value="PRICE_HIGH_TO_LOW">Price: High to Low</option>@if(supportsNewestSort()){<option value="NEWEST">Newest Arrivals</option>}</select></fieldset>
            </div>
            <footer class="filter-panel-footer"><button type="button" class="filter-clear" (click)="clearDraftFilters()">Clear All</button><button type="button" class="filter-apply" [disabled]="!!filterError()" (click)="applyFilters()">Apply Filters</button></footer>
          </section>
        </div>
      }
      @if (isMockData) { <p class="source-note">Development preview Ã‚&middot; Sample prices and availability</p> }
    }
  `,
  styles: [`
    .availability-announcements{display:grid;gap:10px;margin:0 0 18px}.availability-announcement{display:flex;align-items:center;justify-content:space-between;gap:14px;border:1px solid #cce6d2;border-radius:15px;padding:13px 15px;background:#f5fbf6}.availability-announcement h2{margin:3px 0 2px;font:800 16px Manrope,sans-serif}.availability-announcement p{margin:0;color:var(--store-muted);font-size:11px}.availability-status{white-space:nowrap;border-radius:99px;padding:6px 9px;color:var(--store-primary-dark);background:#dcefe1;font-size:10px;font-weight:800}
    :host{display:block}
    .home-rewards{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin:0 0 18px}.home-reward-card{display:grid;grid-template-columns:auto minmax(0,1fr) auto;align-items:center;gap:9px;min-height:70px;border:1px solid var(--store-border);border-radius:15px;padding:10px 11px;color:var(--store-text);background:#fff;box-shadow:0 4px 12px rgba(31,48,39,.05);text-decoration:none}.home-reward-card.coins{border-color:#ead9a9;background:#fffaf0}.home-reward-card.refer{border-color:#bfe1c8;background:#f4fbf5}.home-reward-icon{display:grid;width:34px;height:34px;place-items:center;border-radius:11px;color:#8c6917;background:#f6e8b5}.home-reward-card.refer .home-reward-icon{color:var(--store-primary-dark);background:#dcefe1}.home-reward-icon svg{width:20px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.7}.home-reward-copy{display:grid;min-width:0;gap:3px}.home-reward-copy strong{font:800 11px Manrope,sans-serif}.home-reward-copy b{overflow:hidden;color:var(--store-muted);font-size:10px;text-overflow:ellipsis;white-space:nowrap}.home-reward-card.coins .home-reward-copy b{color:#8c6917}.home-reward-arrow{color:var(--store-muted);font-size:24px;line-height:1}.rewards-skeleton{grid-template-columns:repeat(2,minmax(0,1fr))}.rewards-skeleton i{height:70px;border-radius:15px;background:linear-gradient(90deg,#ebeeea 25%,#f8f9f7 50%,#ebeeea 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.catalog-layout{display:flex;min-width:0;flex-direction:column;margin-top:26px}.category-section,.product-section{width:100%;min-width:0}.category-section{margin-bottom:24px}.category-heading{display:flex;align-items:baseline;gap:11px;margin-bottom:12px}.category-heading h2{margin:0;color:var(--store-text);font:800 17px Manrope,sans-serif;letter-spacing:-.3px}.section-heading{display:flex;align-items:end;justify-content:space-between;gap:15px;margin-bottom:14px}.eyebrow{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.section-heading h1,.section-heading h2{margin:3px 0 0;color:var(--store-text);font:800 21px Manrope,sans-serif;letter-spacing:-.55px}.result-count{padding-bottom:2px;color:var(--store-muted);font-size:11px;white-space:nowrap}
    .store-banners{display:grid;gap:12px;margin-bottom:24px}.store-banner{display:block;overflow:hidden;border-radius:var(--store-radius-lg);color:var(--store-text);background:var(--store-surface);text-decoration:none}.store-banner.static{cursor:default}.store-banner picture{display:block;aspect-ratio:5/2;background:var(--store-surface-muted)}.store-banner img{display:block;width:100%;height:100%;aspect-ratio:5/2;object-fit:contain;background:var(--store-surface-muted)}.store-banner span{display:grid;gap:3px;padding:10px 12px}.store-banner strong{font:750 13px Manrope,sans-serif}.store-banner small{color:var(--store-muted);font-size:10px}.category-icon img{width:100%;height:100%;border-radius:inherit;object-fit:cover}
    .category-row{display:grid;grid-template-rows:repeat(2,auto);grid-auto-flow:column;grid-auto-columns:82px;gap:8px 12px;overflow-x:auto;overscroll-behavior-inline:contain;padding:2px 2px 10px;scrollbar-width:thin;scroll-snap-type:x proximity}.category-chip{display:grid;flex:0 0 82px;justify-items:center;align-content:start;gap:6px;min-width:0;border:1px solid transparent;border-radius:14px;padding:9px 5px;color:var(--store-text);background:transparent;cursor:pointer;scroll-snap-align:start;transition:background .16s,color .16s,border-color .16s}.category-chip:hover{background:var(--store-surface-muted)}.category-chip.selected{color:var(--store-primary-dark);border-color:var(--store-primary);background:var(--store-primary-soft)}.category-icon{display:grid;width:52px;height:52px;place-items:center;overflow:hidden;border-radius:13px;background:var(--store-surface-muted);font-size:21px}.selected .category-icon{background:#fff}.category-icon svg{width:23px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.6}.category-chip small{display:-webkit-box;max-width:100%;overflow:hidden;font-size:10px;font-weight:750;line-height:1.25;text-align:center;-webkit-box-orient:vertical;-webkit-line-clamp:2}
    .product-grid{display:grid;width:100%;grid-template-columns:repeat(5,minmax(0,1fr));gap:14px}.catalog-state{display:grid;min-height:270px;align-content:center;justify-items:center;border:1px dashed var(--store-border);border-radius:var(--store-radius-lg);padding:30px;text-align:center;background:var(--store-surface)}.state-symbol{display:grid;width:48px;height:48px;place-items:center;border-radius:15px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 23px Manrope,sans-serif}.catalog-state h3{margin:14px 0 5px;font:800 17px Manrope,sans-serif}.catalog-state p{max-width:360px;margin:0;color:var(--store-muted);font-size:12px;line-height:1.55}.catalog-state button{margin-top:16px;border:1px solid var(--store-primary);border-radius:10px;padding:9px 15px;color:var(--store-primary-dark);background:#fff;font-size:11px;font-weight:800;cursor:pointer}
    .chip-skeleton{display:flex;gap:12px;overflow:hidden}.chip-skeleton i{flex:0 0 82px;height:86px;border-radius:12px;background:#e9ede8}.product-skeleton{overflow:hidden;height:278px;border:1px solid var(--store-border);border-radius:17px;background:#fff}.product-skeleton i,.product-skeleton span,.product-skeleton b,.chip-skeleton i{display:block;background:linear-gradient(90deg,#ebeeea 25%,#f8f9f7 50%,#ebeeea 75%);background-size:200% 100%;animation:shimmer 1.2s infinite}.product-skeleton i{height:166px}.product-skeleton span{width:80%;height:11px;margin:13px 12px 0;border-radius:5px}.product-skeleton span+span{width:55%;margin-top:8px}.product-skeleton b{width:90px;height:28px;margin:16px 12px 0;border-radius:8px}@keyframes shimmer{to{background-position:-200% 0}}.source-note{margin:30px 0 0;color:#929b94;text-align:center;font-size:10px}
    @media(max-width:1050px){.product-grid{grid-template-columns:repeat(4,minmax(0,1fr))}}
    @media(max-width:850px){.product-grid{grid-template-columns:repeat(3,minmax(0,1fr))}}
    @media(max-width:700px){.catalog-layout{margin-top:20px}.category-section{margin-bottom:18px}.product-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:11px}.section-heading h1,.section-heading h2{font-size:19px}}
    @media(max-width:560px){.category-row{display:flex;gap:8px}.category-chip{flex-basis:74px;padding:7px 4px}.category-icon{width:46px;height:46px}.section-heading{flex-wrap:wrap;gap:3px}.result-count{font-size:10px}.product-skeleton{height:235px}.product-skeleton i{height:135px}.store-banners{gap:10px;margin-bottom:21px}.store-banner{border-radius:15px}.store-banner span{padding:9px 11px}.store-banner strong{font-size:12px}}
    @media(max-width:340px){.product-grid{grid-template-columns:minmax(0,1fr)}}@media(max-width:360px){.category-chip{flex-basis:68px}}
    .catalog-layout{margin-top:22px}.category-section{margin-bottom:16px}.category-heading{margin-bottom:10px}.category-row{grid-template-columns:repeat(auto-fit,minmax(min(100%,78px),1fr));grid-auto-flow:row;gap:8px;min-width:0;overflow:visible;padding:2px 0;scrollbar-width:none}.category-chip{width:100%;flex:initial;padding:8px 5px}.category-chip:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}
    @media(max-width:700px){.catalog-layout{margin-top:20px}.category-section{margin-bottom:18px}}
    @media(max-width:560px){.category-row{display:flex;gap:8px;overflow-x:auto;overscroll-behavior-inline:contain;padding:2px 2px 10px;scrollbar-width:thin;scroll-snap-type:x proximity}.category-chip{width:auto;flex:0 0 74px;padding:7px 4px}}
    @media(max-width:360px){.category-chip{flex-basis:68px}}
  `, `
    :host{display:block;min-width:0;color:var(--store-text)}
    .store-banners{grid-template-columns:minmax(0,1fr);gap:10px;margin-bottom:14px}
    .store-banner{display:block;min-width:0;min-height:0;border:0;border-radius:var(--store-radius-lg);background:var(--store-surface);box-shadow:none}
    .store-banner picture{display:block;aspect-ratio:5/2;background:var(--store-surface-muted)}
    .store-banner img{aspect-ratio:5/2;object-fit:contain;background:var(--store-surface-muted)}
    .store-banner .banner-copy{display:grid;gap:3px;padding:7px 10px 0}
    .store-banner .banner-copy>small{padding:0;color:var(--store-primary);font-size:9px;font-weight:850;letter-spacing:1.3px}
    .store-banner .banner-copy>strong{padding:0;color:#14231a;font:850 clamp(20px,3vw,34px)/1.08 Manrope,sans-serif;letter-spacing:-.8px}
    .store-banner .banner-copy>span{padding:0;color:#3d4c42;font-size:13px;line-height:1.4}.store-banner .banner-copy b span{display:inline;padding:0}
    .store-banner .banner-copy>b{display:inline-flex;align-items:center;justify-content:center;gap:8px;width:max-content;min-height:34px;margin-top:2px;border-radius:999px;padding:0 13px;color:#fff;background:var(--store-primary);font:800 11px Manrope,sans-serif}
    .store-banner.static .banner-copy>b{display:none}
    .home-rewards{gap:12px;margin:0 0 20px}
    .home-reward-card{min-height:78px;gap:10px;border-radius:16px;padding:10px 13px;box-shadow:0 6px 18px rgba(31,48,39,.045);transition:transform .16s,box-shadow .16s}
    .home-reward-card:hover{transform:translateY(-2px);box-shadow:0 10px 22px rgba(31,48,39,.09)}
    .home-reward-card.coins{border-color:#f0d889;background:linear-gradient(110deg,#fffaf0,#fffdf5)}
    .home-reward-card.refer{border-color:#b9e5c8;background:linear-gradient(110deg,#f2fbf5,#f9fefa)}
    .home-reward-icon{width:38px;height:38px;border-radius:13px}
    .home-reward-icon svg{width:23px;height:23px}
    .home-reward-copy{gap:4px}
    .home-reward-copy strong{font:850 13px Manrope,sans-serif}
    .home-reward-copy b{font-size:11px;white-space:normal}
    .home-reward-card.coins .home-reward-copy b{color:#9b7414}
    .home-reward-card.refer .home-reward-copy b{color:var(--store-primary-dark)}
    .trust-strip{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));align-items:center;gap:16px;margin:0 0 24px;border-radius:17px;padding:14px 18px;background:linear-gradient(100deg,#f0f8f1,#f7fbf7)}
    .trust-item{display:flex;min-width:0;align-items:center;gap:14px}
    .trust-icon{display:grid;flex:0 0 48px;width:48px;height:48px;place-items:center;color:var(--store-primary);border-radius:14px;background:#e2f2e6}
    .trust-icon svg{width:31px;height:31px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.7}
    .trust-item>span:last-child{display:grid;min-width:0;gap:3px}
    .trust-item strong{font:850 15px Manrope,sans-serif;text-transform:capitalize}
    .trust-item small{color:var(--store-muted);font-size:12px;line-height:1.4}
    .trust-divider{width:1px;height:46px;background:#d7e2da}
    .catalog-layout{gap:20px;margin-top:4px}.category-section,.product-section{scroll-margin-top:140px}
    .category-section{margin:0}
    .category-heading{display:flex;align-items:center;justify-content:space-between;gap:12px;margin:0 0 12px;border-left:5px solid var(--store-primary);padding-left:12px}
    .category-heading h2,.section-heading h2{margin:0;color:var(--store-text);font:850 23px/1.2 Manrope,sans-serif;letter-spacing:-.6px}
    .view-all{display:inline-flex;align-items:center;gap:7px;min-height:38px;border:0;border-radius:10px;padding:0 8px;color:var(--store-primary-dark);background:transparent;font:800 13px Manrope,sans-serif;cursor:pointer}
    .view-all:hover{background:var(--store-primary-soft)}
    .view-all span{font-size:18px}
    .category-row{display:flex;gap:12px;overflow-x:auto;overscroll-behavior-inline:contain;padding:3px 2px 9px;scrollbar-width:thin;scroll-snap-type:x proximity}
    .category-chip{display:grid;flex:0 0 112px;justify-items:center;align-content:start;gap:8px;min-height:132px;border:1px solid transparent;border-radius:17px;padding:10px 7px;color:var(--store-text);background:transparent;scroll-snap-align:start}
    .category-chip:hover{background:#f5faf6}
    .category-chip.selected{border:2px solid var(--store-primary);color:var(--store-primary-dark);background:#f3fbf5;box-shadow:0 4px 10px rgba(11,112,64,.06)}
    .category-icon{width:78px;height:78px;border-radius:50%;background:#eff6f0}
    .category-chip:nth-child(3n+2) .category-icon{background:#fff0ed}
    .category-chip:nth-child(3n+3) .category-icon{background:#fff8df}
    .category-chip:nth-child(4n) .category-icon{background:#eaf7ec}
    .category-chip.selected .category-icon{background:#fff}
    .category-icon img{object-fit:contain;padding:8px}
    .category-chip small{display:-webkit-box;max-width:100%;overflow:hidden;font-size:12px;font-weight:750;line-height:1.25;text-align:center;-webkit-box-orient:vertical;-webkit-line-clamp:2}
    .category-row.expanded{display:grid;grid-template-columns:repeat(auto-fit,minmax(96px,1fr));overflow:visible}
    .category-row.expanded .category-chip{width:100%;min-width:0}
    .section-heading{align-items:center;margin:0 0 14px;border-left:5px solid var(--store-primary);padding-left:12px}
    .product-heading-copy{display:grid;min-width:0;gap:4px}
    .product-heading-copy small{overflow:hidden;color:var(--store-muted);font-size:11px;text-overflow:ellipsis;white-space:nowrap}
    .product-heading-actions{display:flex;align-items:center;gap:12px}
    .result-count{padding:0;color:var(--store-muted);font-size:13px}
    .filter-button{display:inline-flex;align-items:center;justify-content:center;gap:8px;min-height:42px;border:1px solid var(--store-border);border-radius:13px;padding:0 14px;color:var(--store-text);background:#fff;font:750 12px Manrope,sans-serif;cursor:pointer}
    .filter-button:hover{border-color:var(--store-primary);color:var(--store-primary-dark);background:#f8fcf9}
    .filter-button svg{width:19px;height:19px;fill:none;stroke:var(--store-primary);stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}
    .product-grid{grid-template-columns:repeat(5,minmax(0,1fr));gap:16px}
    .product-skeleton{height:320px;border-radius:19px}
    .product-skeleton i{height:190px}
    .source-note{margin-top:22px}
    @media(max-width:1050px){.product-grid{grid-template-columns:repeat(4,minmax(0,1fr))}}
    @media(max-width:850px){.product-grid{grid-template-columns:repeat(3,minmax(0,1fr))}.category-chip{flex-basis:100px}}
    @media(max-width:700px){.store-banners{margin-bottom:12px}.store-banner{min-height:0;border-radius:15px}.store-banner picture{aspect-ratio:5/2;min-height:0}.store-banner .banner-copy{gap:3px;padding:6px 10px 0}.store-banner .banner-copy>strong{font-size:20px}.store-banner .banner-copy>span{font-size:11px}.store-banner .banner-copy>b{min-height:32px;padding:0 12px;font-size:10px}.home-rewards{gap:8px;margin-bottom:16px}.home-reward-card{grid-template-columns:auto minmax(0,1fr) auto;gap:7px;min-height:74px;border-radius:14px;padding:8px}.home-reward-icon{width:32px;height:32px;border-radius:11px}.home-reward-copy strong{font-size:11px}.home-reward-copy b{font-size:9px}.trust-strip{gap:10px;margin-bottom:20px;border-radius:14px;padding:12px 10px}.trust-item{gap:8px}.trust-icon{flex-basis:35px;width:35px;height:35px;border-radius:11px}.trust-icon svg{width:24px;height:24px}.trust-item strong{font-size:11px}.trust-item small{font-size:9px}.catalog-layout{gap:17px;margin-top:0}.category-heading{margin-bottom:9px}.category-heading h2,.section-heading h2{font-size:19px}.category-row{gap:7px}.category-chip{flex-basis:79px;min-height:108px;gap:6px;padding:8px 5px}.category-icon{width:58px;height:58px}.category-chip small{font-size:10px}.category-row.expanded{grid-template-columns:repeat(auto-fit,minmax(72px,1fr));gap:5px}.category-row.expanded .category-chip{flex-basis:auto}.section-heading{gap:8px}.product-heading-actions{gap:8px}.result-count{font-size:10px}.filter-button{min-height:38px;gap:5px;padding:0 10px;font-size:10px}.filter-button svg{width:16px;height:16px}.product-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.product-skeleton{height:255px}.product-skeleton i{height:145px}}
    @media(max-width:390px){.store-content{padding-right:12px;padding-left:12px}.store-banner .banner-copy>strong{font-size:19px}.home-reward-card{padding:7px}.home-reward-icon{width:30px;height:30px}.home-reward-copy strong{font-size:10px}.home-reward-copy b{font-size:8px}.trust-strip{gap:7px;padding:10px 7px}.trust-item{gap:6px}.trust-icon{flex-basis:30px;width:30px;height:30px}.trust-icon svg{width:21px;height:21px}.trust-item strong{font-size:10px}.trust-item small{font-size:8px}.category-heading h2,.section-heading h2{font-size:17px}.product-heading-actions{gap:5px}}
    @media(max-width:340px){.home-rewards{grid-template-columns:repeat(2,minmax(0,1fr));gap:5px}.home-reward-card{grid-template-columns:auto minmax(0,1fr);position:relative;padding:6px}.home-reward-arrow{display:none}.trust-strip{grid-template-columns:1fr}.trust-divider{width:100%;height:1px}.trust-item{padding:0 8px}.product-heading-actions{flex-wrap:wrap;justify-content:flex-end}}
    .active-filter-count{display:inline-grid;min-width:18px;height:18px;place-items:center;border-radius:50%;padding:0 4px;color:#fff;background:var(--store-primary);font-size:9px}
    .filter-overlay{position:fixed;z-index:300;inset:0;display:flex;justify-content:flex-end;background:rgba(15,27,20,.42);backdrop-filter:blur(2px)}
    .filter-panel{display:flex;width:min(410px,100%);height:100%;max-height:100dvh;flex-direction:column;border-left:1px solid var(--store-border);background:var(--store-surface);box-shadow:-12px 0 32px rgba(10,35,20,.15);outline:0}
    .filter-panel-header{display:flex;flex:0 0 auto;align-items:center;justify-content:space-between;border-bottom:1px solid var(--store-border);padding:17px 20px}.filter-panel-header h2{margin:0;color:var(--store-text);font:850 19px Manrope,sans-serif}.filter-close{display:grid;width:40px;height:40px;place-items:center;border:1px solid var(--store-border);border-radius:50%;color:var(--store-text);background:#fff;font-size:27px;line-height:1;cursor:pointer}.filter-close:focus-visible,.filter-panel button:focus-visible,.filter-panel select:focus-visible,.filter-panel input:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}
    .filter-panel-body{flex:1 1 auto;overflow:auto;overscroll-behavior:contain;padding:8px 20px 20px}.filter-group{display:grid;gap:10px;margin:0;padding:17px 0;border:0;border-bottom:1px solid var(--store-border)}.filter-group legend{padding:0;color:var(--store-text);font:800 14px Manrope,sans-serif}.filter-select-label{color:var(--store-muted);font-size:11px}.filter-group select,.filter-price-row input{width:100%;min-width:0;min-height:44px;border:1px solid var(--store-border);border-radius:10px;padding:0 11px;color:var(--store-text);background:#fff;font:500 13px Manrope,sans-serif}.filter-price-row{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.filter-price-row label{display:grid;min-width:0;gap:6px;color:var(--store-muted);font-size:10px;font-weight:700}.filter-price-row input{font-size:14px}.filter-range-hint{color:var(--store-muted);font-size:10px}.filter-error{margin:0;color:#a52a2a;font-size:11px;line-height:1.4}.filter-check{display:flex;align-items:center;gap:10px;min-height:42px;color:var(--store-text);font-size:13px}.filter-check input{width:20px;height:20px;accent-color:var(--store-primary)}
    .filter-panel-footer{display:grid;grid-template-columns:minmax(0,.8fr) minmax(0,1.2fr);gap:10px;flex:0 0 auto;border-top:1px solid var(--store-border);padding:12px 20px max(12px,env(safe-area-inset-bottom));background:var(--store-surface)}.filter-panel-footer button{min-height:46px;border:1px solid var(--store-primary);border-radius:11px;font:800 12px Manrope,sans-serif;cursor:pointer}.filter-clear{color:var(--store-primary-dark);background:#fff}.filter-apply{color:#fff;background:var(--store-primary)}.filter-apply:hover:not(:disabled){background:var(--store-primary-dark)}.filter-apply:disabled{opacity:.5;cursor:not-allowed}
    @media(max-width:700px){.filter-overlay{align-items:flex-end;justify-content:center}.filter-panel{width:100%;height:min(86dvh,760px);max-height:calc(100dvh - env(safe-area-inset-top));border:1px solid var(--store-border);border-bottom:0;border-radius:20px 20px 0 0;box-shadow:0 -12px 35px rgba(10,35,20,.18)}.filter-panel-header{padding:12px 16px}.filter-panel-header h2{font-size:17px}.filter-panel-body{padding:4px 16px 16px}.filter-group{padding:13px 0}.filter-panel-footer{padding-right:16px;padding-left:16px}}
    @media(max-width:360px){.filter-price-row{grid-template-columns:1fr}.filter-panel{height:min(90dvh,760px)}}
  `],
})
export class StoreHomePage implements OnInit, AfterViewInit, OnDestroy {
  readonly store=signal<Store|null>(null); readonly returnWindowDays=computed(()=>{const value=this.store()?.defaultReturnWindowDays;return Number.isInteger(value)&&value!>=0?value!:null;});
  readonly categoriesExpanded = signal(false);
  readonly categories = signal<Category[]>([]); readonly products = signal<Product[]>([]); readonly failedCategoryImages = signal<Set<string>>(new Set()); readonly failedCategoryThumbnails = signal<Set<string>>(new Set()); readonly failedBannerImages=signal<Set<string>>(new Set());
  readonly loading = signal(true); readonly loadingMore = signal(false); readonly hasMore = signal(true); readonly loadFailed = signal(false); readonly loadError = signal('There may be a temporary connection problem.'); readonly isMockData = environment.useMockData;
  private retryBlockedUntil = 0;
  readonly selectedCategory = signal('all'); readonly query = signal(''); readonly missingMessage=signal('');
  readonly filterOpen=signal(false); readonly appliedFilters=signal<CatalogFilterState>(defaultCatalogFilters()); readonly draftFilters=signal<CatalogFilterState>(defaultCatalogFilters()); readonly filterError=signal('');
  readonly priceBounds=signal<{minimum:number|null;maximum:number|null}>({minimum:null,maximum:null}); readonly supportsNewestSort=signal(false); readonly serverTotalCount=signal(0);
  readonly activeFilterCount=computed(()=>{const filters=this.appliedFilters();return Number(filters.categoryId!=='all')+Number(filters.minPrice!==null||filters.maxPrice!==null)+Number(filters.inStockOnly)+Number(filters.sortBy!=='RECOMMENDED');});
  readonly customerRewards = signal<CustomerRewards|null>(null); readonly rewardsLoading = signal(false); readonly announcements = signal<AvailabilityAnnouncement[]>([]);
  readonly visibleAnnouncements=computed(()=>this.announcements().filter(item=>this.filteredProducts().some(product=>item.productIds.includes(product.id)&&(item.status.startsWith('ORDERING_OPEN')||product.canOrderNow===false))));
  private refreshingSchedules=false;
  private startupWorkScheduled=false;
  async refreshScheduleAvailability():Promise<void>{if(this.refreshingSchedules)return;this.refreshingSchedules=true;try{await Promise.all([this.loadAnnouncements(),this.load()]);}finally{this.refreshingSchedules=false;}}
  readonly showRewards = computed(() => this.store()?.customerRewardsEnabled === true && (!this.isCustomerAuthenticated() || this.rewardsLoading() || this.customerRewards()?.enabled === true));
  readonly skeletonCards = Array.from({ length: 10 }); readonly skeletonChips = Array.from({ length: 6 });
  storeKey = ''; private page = 0; private readonly pageSize = 24; private observer?: IntersectionObserver; private availabilityTimer?: ReturnType<typeof setTimeout>; private readonly destroyRef = inject(DestroyRef);
  readonly filteredProducts = computed(() => { const term=this.query().toLocaleLowerCase().trim(); return this.products().filter(product => !term||`${product.name} ${product.description}`.toLocaleLowerCase().includes(term)); });
  readonly displayedCount=computed(()=>this.query().trim()?this.filteredProducts().length:this.serverTotalCount());
readonly sectionTitle = computed(() => this.query() ? `Results for "${this.query()}"` : this.selectedCategory()==='all' ? 'All products' : 'Products');
  constructor(private readonly route:ActivatedRoute,private readonly router:Router,private readonly data:StorefrontDataService,private readonly session:CustomerSessionService,private readonly rewards:CustomerRewardsService,private readonly activity:StorefrontActivityService,private readonly shell:StoreShell,private readonly campaignNavigation:CampaignCategoryNavigationService){
    afterRenderEffect(()=>this.scrollToCampaignProducts());
    afterRenderEffect(()=>{
      const category=this.loadedCategory();
      if(this.loading()||(!this.loadFailed()&&category===null)||this.startupWorkScheduled)return;
      this.startupWorkScheduled=true;
      if(!this.loadFailed())markStorefrontStartup('product-grid-rendered',{storeKey:this.storeKey,count:this.filteredProducts().length,category});
      this.shell.markStartupContentReady(this.storeKey);
      const initialSearch=this.pendingInitialSearchTerm;this.pendingInitialSearchTerm='';
      if(initialSearch)scheduleStorefrontIdle(()=>{if(!this.destroyRef.destroyed&&this.query().trim()===initialSearch)this.scheduleDemandSearch(initialSearch);});
      scheduleStorefrontIdle(()=>{
        if(this.destroyRef.destroyed)return;
        void this.loadRewards();
      });
    });
  }
  bannerLink(item:{promotionId?:string|null}):string[]|null{return item.promotionId?["/",this.storeKey,"offers",item.promotionId]:null;}
  bannerHref(item:{promotionId?:string|null;targetUrl?:string}):string|null{return item.promotionId?null:item.targetUrl||null;}
  presentationThumbnail(url:string):string{return `${url}${url.includes('?')?'&':'?'}thumbnail=true`;}
  bannerThumbnail(item:{imageUrl:string}):string{return this.presentationThumbnail(item.imageUrl);}
  bannerImageFailed(slot:'PRIMARY'|'SECONDARY'):void{this.failedBannerImages.update(current=>new Set(current).add(slot));}
  banner(slot:'PRIMARY'|'SECONDARY'){return this.store()?.banners.find(item=>item.slot===slot);}
  toggleCategories():void{this.categoriesExpanded.update(expanded=>!expanded);}
  focusCategories():void{this.categoriesExpanded.set(true);requestAnimationFrame(()=>this.categorySection()?.nativeElement.scrollIntoView({block:'start',behavior:window.matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'}));}
  scrollToProducts():void{this.productSection()?.nativeElement.scrollIntoView({block:'start',behavior:window.matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});}
  categoryImageFailed(id:string):void{this.failedCategoryImages.update(current=>new Set(current).add(id));}
  categoryImageLoadFailed(id:string,event:Event):void{const image=event.target as HTMLImageElement;if(!this.failedCategoryThumbnails().has(id)&&image.currentSrc&&image.currentSrc!==image.src){this.failedCategoryThumbnails.update(current=>new Set(current).add(id));return;}this.categoryImageFailed(id);}
  ngAfterViewInit():void { this.observeSentinel(); }
  @HostListener('document:visibilitychange') onResume():void { if(document.visibilityState==='visible'&&!this.loading())void this.refreshScheduleAvailability(); }
  ngOnDestroy():void { ++this.catalogueRequest;if(this.campaignNavigation.pending())this.campaignNavigation.cancel();this.observer?.disconnect(); if(this.availabilityTimer)clearTimeout(this.availabilityTimer);if(this.filterOpen())document.body.style.overflow=this.previousBodyOverflow; }
  private searchTimer?: ReturnType<typeof setTimeout>;
  private pendingInitialSearchTerm='';
  private catalogueRequest=0;
  private routeInitialized=false;
  private readonly loadedCategory=signal<string|null>(null);
  private readonly productSection=viewChild<ElementRef<HTMLElement>>('productsSection');
  private readonly categorySection=viewChild<ElementRef<HTMLElement>>('categorySection');
  private readonly filterTrigger=viewChild<ElementRef<HTMLButtonElement>>('filterTrigger');
  private readonly filterPanel=viewChild<ElementRef<HTMLElement>>('filterPanel');
  private readonly filterClose=viewChild<ElementRef<HTMLButtonElement>>('filterClose');
  private filterTriggerElement:HTMLElement|null=null;
  private previousBodyOverflow='';
  private scrollToCampaignProducts():void{
    const target=this.campaignNavigation.pending(),section=this.productSection()?.nativeElement;
    if(!target||!section||target.storeKey!==this.storeKey||target.categoryId!==this.selectedCategory()||this.loading()||this.loadFailed()||this.loadedCategory()!==target.categoryId||this.router.getCurrentNavigation())return;
    if(!this.campaignNavigation.consume(target.sequence))return;
    const header=section.closest('.store-frame')?.querySelector('.store-header');
    section.style.scrollMarginTop=`${Math.ceil(header?.getBoundingClientRect().height??0)+16}px`;
    section.scrollIntoView({block:'start',behavior:window.matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});
  }
  @HostListener('window:wheel') @HostListener('window:touchstart') @HostListener('window:pointerdown') cancelCampaignScroll():void{this.campaignNavigation.cancel();}
  @HostListener('window:keydown',['$event']) cancelCampaignScrollKey(event:KeyboardEvent):void{if(this.filterOpen()&&event.key==='Escape'){event.preventDefault();this.closeFilter();return;}if(['ArrowUp','ArrowDown','PageUp','PageDown','Home','End',' '].includes(event.key))this.cancelCampaignScroll();}
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';if(this.isCustomerAuthenticated())this.rewardsLoading.set(true);void this.loadAnnouncements();this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params:ParamMap)=>{const category=params.get('category')??'all';const next:CatalogFilterState={categoryId:category,minPrice:this.queryPrice(params.get('minPrice')),maxPrice:this.queryPrice(params.get('maxPrice')),inStockOnly:params.get('inStockOnly')==='true',sortBy:this.querySort(params.get('sortBy'))};if(next.minPrice!==null&&next.maxPrice!==null&&next.minPrice>next.maxPrice){next.minPrice=null;next.maxPrice=null;}const previous=this.filterSignature(this.appliedFilters());const nextSignature=this.filterSignature(next);const reload=!this.routeInitialized||previous!==nextSignature;this.routeInitialized=true;const query=params.get('q')??'';this.query.set(query);this.appliedFilters.set(next);this.draftFilters.set({...next});this.selectedCategory.set(category);if(reload)void this.load();if(category!=='all')this.activity.record(this.storeKey,'CATEGORY_VIEW',undefined,undefined,category);clearTimeout(this.searchTimer);this.pendingInitialSearchTerm=query.trim().length>=2?query.trim():'';if(this.startupWorkScheduled&&this.pendingInitialSearchTerm){const term=this.pendingInitialSearchTerm;this.pendingInitialSearchTerm='';this.scheduleDemandSearch(term);}});}
  isCustomerAuthenticated():boolean{return this.session.isAuthenticated(this.storeKey);}
  rewardLink():string[]{return ['/',this.storeKey,'account','rewards'];}
  rewardQuery():Record<string,string>|null{return this.isCustomerAuthenticated()?null:{returnUrl:'/'+this.storeKey+'/account/rewards'};}
  private async loadRewards():Promise<void>{if(!this.isCustomerAuthenticated())return;this.customerRewards.set(null);this.rewardsLoading.set(true);try{await this.session.restore(this.storeKey);if(!this.session.active())return;this.customerRewards.set(await this.rewards.get(this.storeKey));}catch{this.customerRewards.set(null);}finally{this.rewardsLoading.set(false);}}
  private async loadAnnouncements():Promise<void>{try{const items=await this.data.getAvailabilityAnnouncements(this.storeKey);this.announcements.set(items.map(item=>({...item,clientReceivedAtUtc:Date.now()})));}catch{this.announcements.set([]);}}
  private scheduleDemandSearch(term:string):void{clearTimeout(this.searchTimer);this.searchTimer=setTimeout(()=>{if(this.query().trim()===term)this.activity.recordDemandSearch(this.storeKey,term,this.filteredProducts().length);},500);}
  selectCategory(id:string):void{this.campaignNavigation.cancel();const next={...this.appliedFilters(),categoryId:id};this.appliedFilters.set(next);this.draftFilters.set({...this.draftFilters(),categoryId:id});this.selectedCategory.set(id);if(id!=='all')this.activity.record(this.storeKey,'CATEGORY_VIEW',undefined,undefined,id);void this.updateFilterUrl(next);void this.load();}
  resetFilters():void{this.clearAppliedFilters();}
  openFilter(event:Event):void{this.filterTriggerElement=event.currentTarget as HTMLElement;this.draftFilters.set({...this.appliedFilters()});this.filterError.set('');this.previousBodyOverflow=document.body.style.overflow;document.body.style.overflow='hidden';this.filterOpen.set(true);setTimeout(()=>this.filterClose()?.nativeElement.focus());}
  closeFilter():void{if(!this.filterOpen())return;this.filterOpen.set(false);document.body.style.overflow=this.previousBodyOverflow;const target=this.filterTriggerElement??this.filterTrigger()?.nativeElement;setTimeout(()=>target?.focus());}
  trapFilterFocus(event:KeyboardEvent):void{if(event.key!=='Tab')return;const panel=this.filterPanel()?.nativeElement;if(!panel)return;const controls=Array.from(panel.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),select:not(:disabled),a[href],[tabindex]:not([tabindex="-1"])'));if(!controls.length)return;const first=controls[0],last=controls[controls.length-1];if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus();}else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus();}}
  updateDraftCategory(event:Event):void{this.draftFilters.update(current=>({...current,categoryId:(event.target as HTMLSelectElement).value}));}
  updateDraftPrice(field:'minPrice'|'maxPrice',event:Event):void{const raw=(event.target as HTMLInputElement).value;this.draftFilters.update(current=>({...current,[field]:raw===''?null:Number(raw)}));this.validateDraftFilters();}
  updateDraftStock(event:Event):void{this.draftFilters.update(current=>({...current,inStockOnly:(event.target as HTMLInputElement).checked}));}
  updateDraftSort(event:Event):void{this.draftFilters.update(current=>({...current,sortBy:this.querySort((event.target as HTMLSelectElement).value)}));}
  clearDraftFilters():void{this.clearAppliedFilters();}
  async applyFilters():Promise<void>{if(!this.validateDraftFilters())return;const next={...this.draftFilters()};this.appliedFilters.set(next);this.selectedCategory.set(next.categoryId);this.closeFilter();await this.updateFilterUrl(next);void this.load();}
  clearAppliedFilters():void{const next=defaultCatalogFilters();this.filterError.set('');this.appliedFilters.set(next);this.draftFilters.set({...next});this.selectedCategory.set('all');this.page=0;this.hasMore.set(true);this.products.set([]);void this.updateFilterUrl(next);void this.load();}
  private validateDraftFilters():boolean{const {minPrice,maxPrice}=this.draftFilters();let error='';if(minPrice!==null&&(!Number.isFinite(minPrice)||minPrice<0||minPrice>1_000_000_000))error='Enter a valid non-negative minimum price.';else if(maxPrice!==null&&(!Number.isFinite(maxPrice)||maxPrice<0||maxPrice>1_000_000_000))error='Enter a valid non-negative maximum price.';else if(minPrice!==null&&maxPrice!==null&&minPrice>maxPrice)error='Minimum price must not exceed maximum price.';this.filterError.set(error);return !error;}
  private queryPrice(value:string|null):number|null{if(value===null||value.trim()==='')return null;const parsed=Number(value);return Number.isFinite(parsed)&&parsed>=0&&parsed<=1_000_000_000?parsed:null;}
  private querySort(value:string|null):ProductSortOption{return value==='PRICE_LOW_TO_HIGH'||value==='PRICE_HIGH_TO_LOW'||value==='NEWEST'?value:'RECOMMENDED';}
  private filterSignature(state:CatalogFilterState):string{return `${state.categoryId}|${state.minPrice??''}|${state.maxPrice??''}|${state.inStockOnly}|${state.sortBy}`;}
  private apiFilters(state=this.appliedFilters()):ProductCatalogFilters{return {minPrice:state.minPrice??undefined,maxPrice:state.maxPrice??undefined,inStockOnly:state.inStockOnly||undefined,sortBy:state.sortBy==='RECOMMENDED'?undefined:state.sortBy};}
  private updateFilterUrl(state:CatalogFilterState):Promise<boolean>{return this.router.navigate([], {relativeTo:this.route,queryParams:{category:state.categoryId==='all'?null:state.categoryId,minPrice:state.minPrice,maxPrice:state.maxPrice,inStockOnly:state.inStockOnly?true:null,sortBy:state.sortBy==='RECOMMENDED'?null:state.sortBy},queryParamsHandling:'merge'});}
  async requestMissingProduct():Promise<void>{const keyword=this.query().trim();if(keyword.length<2){this.missingMessage.set('Search for a product name first.');return;}try{await this.data.requestMissingProduct(this.storeKey,keyword);this.missingMessage.set('Product request submitted.');}catch(error:any){this.missingMessage.set(error?.error?.message??'Product request could not be submitted.');}}
  async load():Promise<void>{
    const remaining=this.retryBlockedUntil-Date.now();if(remaining>0){this.loadError.set(`The catalogue is temporarily busy. Please retry in ${Math.ceil(remaining/1000)} seconds.`);return;}
    const request=++this.catalogueRequest,key=this.storeKey,category=this.selectedCategory(),filterState={...this.appliedFilters()};
    this.loading.set(true);this.loadFailed.set(false);this.loadedCategory.set(null);this.loadError.set('There may be a temporary connection problem.');
    try{
      const store=await this.shell.waitForStore(key);if(!store)throw new Error('Store metadata unavailable');
      if(request!==this.catalogueRequest)return;
      // Banner URLs arrive with the store response. Publish them before the
      // slower catalogue requests so the browser can start the LCP image early.
      this.store.set(store);
      const [categories,products]=await Promise.all([this.data.getCategories(key),this.data.getProducts(key,1,this.pageSize,category==='all'?undefined:category,this.apiFilters(filterState))]);
      if(request!==this.catalogueRequest||key!==this.storeKey||category!==this.selectedCategory())return;
      this.categories.set(categories);this.products.set(products.items);this.serverTotalCount.set(products.totalCount);this.priceBounds.set({minimum:products.minimumPrice??null,maximum:products.maximumPrice??null});this.supportsNewestSort.set(products.supportsNewestSort===true);this.scheduleAvailabilityRefresh(products.items);
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
  private async loadNextPage(): Promise<void> { if (this.loading() || this.loadingMore() || !this.hasMore()) return; this.loadingMore.set(true);const request=this.catalogueRequest,category=this.selectedCategory(),filters=this.filterSignature(this.appliedFilters()); try { const next = await this.data.getProducts(this.storeKey, this.page + 1, this.pageSize, category==='all'?undefined:category,this.apiFilters()); if(request!==this.catalogueRequest||category!==this.selectedCategory()||filters!==this.filterSignature(this.appliedFilters()))return; const known = new Set(this.products().map(x => x.id)); this.products.update(items => [...items, ...next.items.filter(x => !known.has(x.id))]); this.serverTotalCount.set(next.totalCount);this.page = next.pageNumber; this.hasMore.set(next.hasMore); setTimeout(() => this.observeSentinel()); } finally { this.loadingMore.set(false); } }
}

