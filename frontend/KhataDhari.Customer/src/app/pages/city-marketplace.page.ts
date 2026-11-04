import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, input, isDevMode, signal } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { filter, Subscription } from 'rxjs';
import { CityInfo, CityProduct, CityProductPage, CityStore, PublicMarketplaceApiService } from '../data/public-marketplace-api.service';
import { Product } from '../models/storefront.models';
import { ProductCardComponent } from '../shared/product-card.component';

@Component({
  selector:'shop-city-marketplace',standalone:true,imports:[CommonModule,RouterLink,ProductCardComponent],
  template:`
  <main class="city-page">
    @if(loadingCity()){<div class="loading-card" role="status">Loading local marketplace…</div>}
    @else if(cityError()){<section class="state-card" role="alert"><h1>Marketplace unavailable</h1><p>We couldn't load this city marketplace.</p><button type="button" (click)="loadCity()">Retry</button></section>}
    @else if(city();as current){
      <header class="city-hero"><div class="city-brand">@if(current.logoUrl && !logoFailed()){<img class="city-logo" [src]="api.asset(current.logoUrl)" alt="{{current.marketplaceName}} logo" (error)="logoFailed.set(true)"/>}@else{<span class="bazaar-mark" aria-hidden="true">K</span>}<div><h1>{{current.marketplaceName}}</h1><p class="city-tagline">Apne Shehar Ki Dukaan, Ab Online</p></div></div><small class="powered">Powered by KhataDhari</small></header>
      <section class="city-banner-hero" aria-label="{{current.marketplaceName}} marketplace banner">@if(current.bannerUrl && !bannerFailed()){<img class="city-banner" [src]="api.asset(current.bannerUrl)" alt="" loading="eager" fetchpriority="high" (error)="onBannerImageError(current.bannerUrl)"/>}<div class="banner-overlay">@if(current.bannerHeadline){<h2>{{current.bannerHeadline}}</h2>}@if(current.bannerSubtitle){<p>{{current.bannerSubtitle}}</p>}<a class="banner-cta" [routerLink]="['/',slug(),'products']">Explore Products <span aria-hidden="true">→</span></a></div></section>
      <nav class="tabs" aria-label="City marketplace sections"><a [routerLink]="['/',slug(),'products']" [class.active]="activeTab()==='products'">All Products</a><a [routerLink]="['/',slug(),'stores']" [class.active]="activeTab()==='stores'">Local Stores</a></nav>
      @if(isCityHome() && activeTab()==='products'){@if(productsPage()?.categories?.length){<section class="city-category-section" aria-label="Shop by category"><div class="section-head"><span class="eyebrow">DISCOVER</span><h2>Shop by category</h2></div><div class="category-chips"><button type="button" [class.selected]="!categoryId()" (click)="selectCategory('')">All</button>@for(c of productsPage()!.categories;track c.categoryKey){<button type="button" class="city-category-card" [class.selected]="categoryId()===c.categoryKey" [attr.aria-pressed]="categoryId()===c.categoryKey" (click)="selectCategory(c.categoryKey)"><span class="city-category-image">@if(c.imageUrl && !failedCategoryImages().has(c.categoryKey)){<img [src]="api.asset(c.imageUrl)" alt="" loading="lazy" (error)="categoryImageFailed(c.categoryKey)"/>}@else{<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5.5h16v13H4zM7 15l3.5-3.5 2.5 2 2-2 3 3.5M8 9h.01"/></svg>}</span><span>{{c.categoryName}}</span><small>{{c.productCount}} {{c.productCount===1?'product':'products'}}</small></button>}</div></section>}@if(filteredStores().length){<section class="home-stores" aria-label="Explore local stores"><div class="section-head"><div><span class="eyebrow">NEARBY</span><h2>Explore local stores</h2></div><a [routerLink]="['/',slug(),'stores']">View all stores</a></div><div class="home-store-row">@for(store of filteredStores().slice(0,4);track store.storeKey){<a class="home-store" [routerLink]="['/',store.storeKey]">@if(store.logoUrl){<img [src]="api.asset(store.logoUrl)" [alt]="store.storeName+' logo'" loading="lazy"/>}@else{<span class="store-fallback">{{store.storeName.slice(0,1)}}</span>}<span><strong>{{store.storeName}}</strong><small>{{store.businessType||store.shortDescription}}</small></span><b aria-hidden="true">›</b></a>}</div></section>}}
      <section class="filters" aria-label="Marketplace filters">
        <label class="search"><span class="sr-only">Search products and stores</span><span aria-hidden="true">⌕</span><input [value]="search()" (input)="setSearch($event)" placeholder="Search products or local stores" /></label>
        @if(activeTab()==='products'){
        @if(!isCityHome()){<label><span class="sr-only">Filter by category</span><select [value]="categoryId()" (change)="setCategory($event)"><option value="">All categories</option>@for(c of productsPage()?.categories??[];track c.categoryKey){<option [value]="c.categoryKey">{{c.categoryName}}</option>}</select></label>}
          <label><span class="sr-only">Filter by brand</span><select [value]="brandId()" (change)="setBrand($event)"><option value="">All brands</option>@for(b of productsPage()?.brands??[];track b.brandKey){<option [value]="b.brandKey">{{b.brandName}}</option>}</select></label>
        }@else{
          <label><span class="sr-only">Filter by business type</span><select [value]="businessType()" (change)="setBusinessType($event)"><option value="">All store types</option>@for(t of businessTypes();track t){<option [value]="t">{{t}}</option>}</select></label>
          <label><span class="sr-only">Filter by locality</span><select [value]="locality()" (change)="setLocality($event)"><option value="">All localities</option>@for(l of localities();track l){<option [value]="l">{{l}}</option>}</select></label>
        }
        @if(search()||categoryId()||brandId()||businessType()||locality()){<button class="clear" type="button" (click)="clearFilters()">Clear filters</button>}
      </section>
      @if(activeTab()==='products'){
        @if(loadingProducts()){<div class="loading-card" role="status">Loading products…</div>}
        @else if(productsError()){<section class="state-card" role="alert"><h2>Products couldn't load</h2><p>Try again in a moment.</p><button type="button" (click)="loadProducts()">Retry</button></section>}
        @else if(productsPage()?.items?.length){<div class="product-grid">@for(card of productCards();track card.product.id){<shop-product-card [product]="card.product" [storeKey]="card.storeKey" [retailerName]="card.retailerName" [discoveryOnly]="true"/>}</div>
          @if(productsPage()?.hasMore){<button class="more" type="button" (click)="nextPage()">Load more products</button>}
        }@else{<section class="empty"><span>⌕</span><h2>{{search()?'No products match your search':'No products available yet'}}</h2><p>Try changing your filters or browse local stores.</p></section>}
      }@else{
        @if(filteredStores().length){<div class="store-grid">@for(store of filteredStores();track store.storeKey){<article class="store-card"><div class="store-logo">@if(store.logoUrl){<img [src]="api.asset(store.logoUrl)" [alt]="store.storeName+' logo'" loading="lazy"/>}@else{<span aria-hidden="true">{{store.storeName.slice(0,1)}}</span>}</div><div class="store-copy"><span class="business">{{store.businessType||'Local retailer'}}</span><h2>{{store.storeName}}</h2><p>{{store.shortDescription}}</p>@if(store.locality){<small>{{store.locality}}</small>}</div><a class="explore" [routerLink]="['/',store.storeKey]">Explore Store <span aria-hidden="true">→</span></a></article>}</div>}
        @else{<section class="empty"><span>⌕</span><h2>{{search()||businessType()||locality()?'No stores match your filters':'No local stores published yet'}}</h2><p>Clear filters or check back later.</p></section>}
      }
    }
  </main>`,
  styleUrl:'./city-marketplace.page.scss'
})
export class CityMarketplacePage implements OnInit,OnDestroy {
  readonly slug=input.required<string>();readonly city=signal<CityInfo|null>(null);
  readonly productCards=computed(()=> (this.productsPage()?.items??[]).map(item=>({product:this.toStorefrontProduct(item),storeKey:item.operatingStoreKey,retailerName:item.operatingStoreName})));readonly stores=signal<CityStore[]>([]);readonly productsPage=signal<CityProductPage|null>(null);readonly loadingCity=signal(true);readonly loadingProducts=signal(false);readonly cityError=signal(false);readonly productsError=signal(false);readonly logoFailed=signal(false);readonly bannerFailed=signal(false);readonly failedCategoryImages=signal<Set<string>>(new Set());readonly search=signal('');readonly categoryId=signal('');readonly brandId=signal('');readonly businessType=signal('');readonly locality=signal('');readonly page=signal(1);
  isCityHome():boolean{return this.router.url.split('?')[0].replace(/\/$/,'')===`/${this.slug()}`;}
  activeTab(): 'products'|'stores'{return this.router.url.split('?')[0].endsWith('/stores')?'stores':'products';}
  businessTypes():string[]{return [...new Set(this.stores().map(x=>x.businessType).filter(Boolean))].sort();}
  localities():string[]{return [...new Set(this.stores().map(x=>x.locality??'').filter(Boolean))].sort();}
  filteredStores():CityStore[]{const q=this.search().trim().toLowerCase();return this.stores().filter(x=>(!q||`${x.storeName} ${x.shortDescription} ${x.businessType}`.toLowerCase().includes(q))&&(!this.businessType()||x.businessType===this.businessType())&&(!this.locality()||x.locality===this.locality()));}
  private routeSub?:Subscription;private searchTimer?:ReturnType<typeof setTimeout>;private searchEventId=crypto.randomUUID();private searchGeneration=0;
  constructor(readonly api:PublicMarketplaceApiService,private readonly router:Router){}
  ngOnInit():void{this.syncCategoryFromUrl();void this.loadCity();this.routeSub=this.router.events.pipe(filter(e=>e instanceof NavigationEnd)).subscribe(()=>{this.syncCategoryFromUrl();if(this.activeTab()==='products')void this.loadProducts();});}
  ngOnDestroy():void{this.routeSub?.unsubscribe();if(this.searchTimer)clearTimeout(this.searchTimer);}
  async loadCity():Promise<void>{this.loadingCity.set(true);this.cityError.set(false);this.logoFailed.set(false);this.bannerFailed.set(false);try{const result=await this.api.city(this.slug());this.city.set(result.city);this.stores.set(result.stores);if(this.activeTab()==='products')await this.loadProducts();}catch{this.cityError.set(true);}finally{this.loadingCity.set(false);}}
  async loadProducts():Promise<void>{const generation=this.searchGeneration;const query=this.search().trim();this.loadingProducts.set(true);this.productsError.set(false);try{const result=await this.api.products(this.slug(),{page:this.page(),pageSize:24,search:query||undefined,searchEventId:query.length>=2?this.searchEventId:undefined,category:this.categoryId()||undefined,brand:this.brandId()||undefined});if(generation===this.searchGeneration)this.productsPage.set(result);}catch{if(generation===this.searchGeneration)this.productsError.set(true);}finally{if(generation===this.searchGeneration)this.loadingProducts.set(false);}}
  setSearch(e:Event):void{const value=(e.target as HTMLInputElement).value;if(value!==this.search()){this.searchGeneration++;this.searchEventId=crypto.randomUUID();}this.search.set(value);this.page.set(1);if(this.activeTab()!=='products')return;if(this.searchTimer)clearTimeout(this.searchTimer);this.searchTimer=setTimeout(()=>void this.loadProducts(),250);}
  setCategory(e:Event):void{this.selectCategory((e.target as HTMLSelectElement).value);}
  private syncCategoryFromUrl():void{const value=this.router.parseUrl(this.router.url).queryParams['category'];this.categoryId.set(typeof value==='string'?value:'');}
  onBannerImageError(url:string|null|undefined):void{this.bannerFailed.set(true);if(isDevMode())console.warn('City marketplace banner failed to load.',url);}
  private toStorefrontProduct(item:CityProduct):Product{return{id:item.id,categoryId:item.categoryId,name:item.name,description:item.description,imageUrl:this.api.asset(item.imageUrl),sellingPrice:item.sellingPrice,gstPercentage:item.gstPercentage,gstPricingType:item.gstPricingType==='EXCLUSIVE'?'EXCLUSIVE':'INCLUSIVE',compareAtPrice:item.compareAtPrice??undefined,available:item.canOrderNow&&item.isPurchasable,availability:item.availability==='OUT_OF_STOCK'?'OUT_OF_STOCK':item.canOrderNow?'ORDERING_OPEN':'ORDERING_CLOSED',availabilityMessage:item.availabilityMessage??undefined,canOrderNow:item.canOrderNow,isPurchasable:item.isPurchasable,partnerSellerId:item.partnerSellerId??undefined,sellerName:item.sellerName??undefined,notPurchasableReason:item.notPurchasableReason??undefined};}
  categoryImageFailed(key:string):void{this.failedCategoryImages.update(current=>new Set(current).add(key));}
  setBrand(e:Event):void{this.brandId.set((e.target as HTMLSelectElement).value);this.page.set(1);void this.loadProducts();}
  selectCategory(value:string):void{this.categoryId.set(value);this.page.set(1);void this.router.navigate(['/',this.slug(),'products'],{queryParams:{category:value||null}});}
  setBusinessType(e:Event):void{this.businessType.set((e.target as HTMLSelectElement).value);}
  setLocality(e:Event):void{this.locality.set((e.target as HTMLSelectElement).value);}
  clearFilters():void{this.search.set('');this.categoryId.set('');this.brandId.set('');this.businessType.set('');this.locality.set('');this.page.set(1);if(this.activeTab()==='products'&&this.router.parseUrl(this.router.url).queryParams['category']){void this.router.navigate(['/',this.slug(),'products']);return;}if(this.activeTab()==='products')void this.loadProducts();}
  nextPage():void{const current=this.productsPage();if(!current?.hasMore||this.loadingProducts())return;this.page.update(x=>x+1);void this.api.products(this.slug(),{page:this.page(),pageSize:24,search:this.search().trim()||undefined,category:this.categoryId()||undefined,brand:this.brandId()||undefined}).then(next=>this.productsPage.set({...next,items:[...(current.items),...next.items]})).catch(()=>this.productsError.set(true));}
}
