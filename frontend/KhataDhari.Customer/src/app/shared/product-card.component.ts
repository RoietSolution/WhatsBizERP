import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, HostListener, OnInit, computed, effect, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { discountPercent, Product } from '../models/storefront.models';
import { StorefrontNotificationService } from '../storefront-notification.service';
import { CartService } from '../cart/cart.service';
import { WishlistService } from '../wishlist.service';
import { StorefrontCountdownService } from '../storefront-countdown.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { environment } from '../../environments/environment';

@Component({
 selector:'shop-product-card',standalone:true,
 imports:[CurrencyPipe,DecimalPipe,RouterLink],
 template:`<article class="product-card" [class.unavailable]="!product().available">
 @if(!discoveryOnly()){<button class="heart" type="button" (click)="toggleWishlist($event)" [class.saved]="wishlist.has(product().id)" [attr.aria-label]="wishlist.has(product().id)?'Remove '+product().name+' from wishlist':'Add '+product().name+' to wishlist'"><span>{{ wishlist.has(product().id) ? '♥' : '♡' }}</span></button>}
 <a class="product-image" [routerLink]="['/',storeKey(),'products',product().id]">
 @if(!imageUnavailable()){<picture>@if(!responsiveSourceFailed()){ @if(responsiveSource(); as source){<source [attr.type]="source.type" [attr.media]="source.media" [srcset]="source.srcset" [sizes]="source.sizes" />}}<img [src]="primaryImage()?.url || product().imageUrl" [alt]="product().name" width="600" height="508" [attr.loading]="eagerImage() ? 'eager' : 'lazy'" [attr.fetchpriority]="imagePriority() ? 'high' : null" decoding="async" referrerpolicy="no-referrer" (error)="imageLoadFailed($event)"/></picture>}@else{<span class="placeholder">Image coming soon</span>}
 @if(product().badge){<span class="badge">{{product().badge}}</span>}</a>
 <div class="info"><a class="name" [routerLink]="['/',storeKey(),'products',product().id]">{{product().name}}</a>
 @if(discoveryOnly() && product().sellerName){<div class="seller-attribution"><strong>Seller: {{product().sellerName}}</strong><span>Available through: {{retailerName()||storeKey()}}</span></div>}
 @else if(discoveryOnly()){<div class="seller-attribution"><strong>Sold by {{retailerName()||storeKey()}}</strong></div>}
 @else if(product().sellerName){<div class="seller-attribution"><strong>Seller: {{product().sellerName}}</strong><span>Operating storefront: {{storeKey()}}</span></div>}
 @if(product().packSize){<span class="pack">{{product().packSize}}</span>}
 @if(product().ratingCount){<span class="rating">&#9733; {{product().averageRating|number:'1.1-1'}} ({{product().ratingCount}})</span>}
 <div class="price"><strong>{{product().sellingPrice|currency:'INR':'symbol':'1.0-2'}}</strong>@if(product().gstPricingType === 'EXCLUSIVE' && (product().gstPercentage ?? 0) > 0){<small>+ GST</small>}@if(product().compareAtPrice){<del>{{product().compareAtPrice|currency:'INR':'symbol':'1.0-0'}}</del>}@if(this.discountPercent(product()); as discount){<span class="discount">{{discount}}% OFF</span>}</div>
 <small class="tax">Inclusive of all taxes</small>
 @if(discoveryOnly()){<div class="discovery-actions">@if(!product().available){<span class="stock">{{product().partnerSellerId&&product().isPurchasable===false?'Partner purchases are not available yet':product().availability==='OUT_OF_STOCK'?'OUT OF STOCK':product().canOrderNow===false?'Ordering Closed':product().availability==='AVAILABLE_LATER'?'AVAILABLE LATER':product().availability==='UNAVAILABLE_TODAY'?'NOT AVAILABLE TODAY':'Currently unavailable'}}</span>}<a class="buy view-product" [routerLink]="['/',storeKey(),'products',product().id]">View Product</a></div>}
 @else if(!product().available){<span class="stock">{{product().partnerSellerId&&product().isPurchasable===false?'Partner purchases are not available yet':product().availability==='OUT_OF_STOCK'?'OUT OF STOCK':product().canOrderNow===false?'Ordering Closed':product().availability==='AVAILABLE_LATER'?'AVAILABLE LATER':product().availability==='UNAVAILABLE_TODAY'?'NOT AVAILABLE TODAY':'Currently unavailable'}}</span>}@else{<div class="purchase-actions"><button class="add" type="button" (click)="add()">{{quantity()?'In cart ('+quantity()+')':'Add to cart'}}</button><button class="buy" type="button" (click)="buyNow()">Buy now</button></div>}
 </div></article>`,
 styles:[`:host{display:block;min-width:0}.product-card{position:relative;display:flex;height:100%;min-width:0;flex-direction:column;overflow:hidden;border:1px solid var(--store-border);border-radius:17px;background:var(--store-surface)}.heart{position:absolute;z-index:3;top:8px;right:8px;display:grid;width:38px;height:38px;place-items:center;border:1px solid var(--store-border);border-radius:50%;color:#59645d;background:rgba(255,255,255,.94);font-size:22px;cursor:pointer}.heart.saved{color:#cf3154}.product-image{position:relative;display:block;overflow:hidden;aspect-ratio:1.18/1;background:#f4f6f2}.product-image picture{display:block;width:100%;height:100%}.product-image img{display:block;box-sizing:border-box;width:100%;height:100%;object-fit:contain;object-position:center;padding:8px;background:#f4f6f2}.placeholder{display:grid;height:100%;place-items:center;color:var(--store-muted);font-size:9px}.badge{position:absolute;top:8px;left:8px;border-radius:99px;padding:4px 7px;background:var(--store-accent-soft);font-size:8px;font-weight:800}.info{display:flex;flex:1;min-width:0;flex-direction:column;padding:10px 11px 11px}.name{display:-webkit-box;min-height:35px;overflow:hidden;color:var(--store-text);font-size:12px;line-height:1.45;font-weight:750;text-decoration:none;-webkit-box-orient:vertical;-webkit-line-clamp:2}.seller-attribution{display:grid;gap:2px;margin-top:5px;border-left:2px solid var(--store-primary-soft);padding-left:7px;font-size:9px}.seller-attribution span{color:var(--store-muted)}.pack{margin-top:3px;color:var(--store-muted);font-size:10px}.rating{margin-top:5px;color:#9a6810;font-size:10px;font-weight:800}.price{display:flex;align-items:baseline;gap:6px;margin-top:7px}.price strong{font:800 14px Manrope,sans-serif;white-space:nowrap}.price del{color:#9ba39d;font-size:9px}.discount{color:var(--store-danger);font-size:9px;font-weight:850;white-space:nowrap}.tax{color:var(--store-muted);font-size:8px}.purchase-actions{display:flex;align-items:stretch;gap:7px;margin-top:auto;padding-top:11px}.discovery-actions{display:flex;flex-direction:column;align-items:stretch;gap:6px;margin-top:auto;padding-top:11px}.purchase-actions button,.view-product{flex:1 1 0;min-width:0;min-height:36px;border-radius:9px;padding:6px 4px;font-size:10px;font-weight:800;line-height:1.2;cursor:pointer;transition:background .16s,box-shadow .16s,transform .16s}.purchase-actions button:hover{transform:translateY(-1px);box-shadow:0 3px 10px rgba(20,92,67,.14)}.purchase-actions button:focus-visible,.view-product:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}.add{border:1px solid var(--store-primary);color:var(--store-primary-dark);background:#fff}.add:hover{background:var(--store-primary-soft)}.buy{border:1px solid var(--store-primary);color:#fff;background:var(--store-primary)}.buy:hover{background:var(--store-primary-dark)}.stock{display:grid;gap:5px;align-self:stretch;margin-top:auto;padding-top:8px;color:var(--store-danger);font-size:9px;font-weight:850;letter-spacing:.15px}.stock small{display:block;font-weight:650;letter-spacing:0}.countdown{display:grid;gap:2px}.countdown strong{font:800 13px Manrope,sans-serif;letter-spacing:.04em}.unavailable img{opacity:.65;filter:saturate(.55)}@media(max-width:390px){.product-card{border-radius:14px}.product-image{aspect-ratio:1.05/1}.info{padding:9px 8px}.purchase-actions{gap:4px}.purchase-actions button,.view-product{font-size:9px}.heart{width:36px;height:36px}}@media(max-width:360px){.name{font-size:11px}.price strong{font-size:12px}.purchase-actions button,.view-product{min-height:34px}}`]
})
export class ProductCardComponent implements OnInit{ readonly discountPercent = discountPercent;
 readonly initialProduct=input.required<Product>({alias:'product'});readonly refreshedProduct=signal<Product|null>(null);
 readonly product=computed(()=>this.refreshedProduct()??this.initialProduct());readonly storeKey=input.required<string>();readonly discoveryOnly=input(false);readonly retailerName=input<string|undefined>();readonly imageUnavailable=signal(false);readonly responsiveSourceFailed=signal(false);readonly imagePriority=input(false);readonly eagerImage=input(false);
 readonly responsiveSource=computed(()=>this.buildResponsiveSource());
 readonly productImageSizes='(max-width: 340px) calc(100vw - 24px), (max-width: 700px) calc((100vw - 43px) / 2), (max-width: 850px) calc((100vw - 76px) / 3), (max-width: 1050px) calc((100vw - 90px) / 4), calc((min(1240px, 100vw) - 104px) / 5)';
 private checkedBoundary='';
 private readonly resetProduct=effect(()=>{this.initialProduct();untracked(()=>{this.refreshedProduct.set(null);this.checkedBoundary='';this.imageUnavailable.set(false);this.responsiveSourceFailed.set(false);});});
 private readonly boundaryRefresh=effect(()=>{
   if(this.discoveryOnly())return;const product=this.product();const boundary=product.canOrderNow===false?product.nextOrderOpenAtUtc:product.nextOrderCloseAtUtc;
   const remaining=this.clock.remaining(boundary,product.availabilityServerNowUtc,product.availabilityClientReceivedAtUtc);
   if(boundary&&remaining===0&&this.checkedBoundary!==boundary){this.checkedBoundary=boundary;untracked(()=>void this.refreshAvailability());}
 });
 @HostListener('document:visibilitychange') onResume():void{if(!this.discoveryOnly()&&document.visibilityState==='visible'&&this.product().availabilityReason)void this.refreshAvailability();}
 private async refreshAvailability():Promise<void>{try{const product=await this.data.getProduct(this.storeKey(),this.product().id);if(product)this.refreshedProduct.set(product);}catch{this.refreshedProduct.set({...this.product(),available:false});}}
 readonly quantity=computed(()=>this.cart.lines().find(x=>x.product.id===this.product().id)?.quantity??0);
 constructor(private readonly cart:CartService,readonly wishlist:WishlistService,private readonly router:Router,private readonly notify:StorefrontNotificationService,readonly clock:StorefrontCountdownService,private readonly data:StorefrontDataService){}
 primaryImage(){return this.product().images?.find(image=>image.isPrimary)??this.product().images?.[0]??null;}
 private buildResponsiveSource():{type:string|null;media?:string;srcset:string;sizes:string}|null{
   const image=this.primaryImage();const rawUrl=image?.url||this.product().imageUrl;if(!rawUrl)return null;
   try{
     const url=new URL(rawUrl,location.origin);
     if(url.protocol==='https:'&&url.hostname.toLowerCase()==='cdn.shopify.com'){
       const srcset=[320,480,640,960].map(width=>{const candidate=new URL(url);candidate.searchParams.set('width',String(width));return `${candidate.toString()} ${width}w`;}).join(', ');
       return {type:null,srcset,sizes:this.productImageSizes};
     }
     if(image&&image.thumbnailUrl!==image.url){
       // Product cards should never promote the original merely because a high-DPR
       // device needs more pixels than the stored thumbnail provides. The original
       // remains the <img src> fallback if the thumbnail request itself fails.
       return {type:null,srcset:image.thumbnailUrl,sizes:this.productImageSizes};
     }
     const thumbnail=this.productThumbnailUrl();
     return thumbnail?{type:null,srcset:thumbnail,sizes:this.productImageSizes}:null;
   }catch{return null;}
 }
 productThumbnailUrl():string|null{try{const url=new URL(this.product().imageUrl,location.origin);const apiOrigin=new URL(environment.apiBaseUrl).origin;if(url.origin!==apiOrigin||!/^\/api\/store\/[^/]+\/products\/[^/]+\/image$/i.test(url.pathname))return null;url.searchParams.set('thumbnail','true');return url.toString();}catch{return null;}}
 imageLoadFailed(event:Event):void{const image=event.target as HTMLImageElement;if(!this.responsiveSourceFailed()&&image.currentSrc&&image.currentSrc!==image.src){this.responsiveSourceFailed.set(true);return;}this.imageUnavailable.set(true);}
 ngOnInit():void{if(!this.discoveryOnly())this.wishlist.initialize(this.storeKey());}
 async add():Promise<void>{await this.addCurrent(false);}
 async buyNow():Promise<void>{await this.addCurrent(true);}
 private async addCurrent(navigate:boolean):Promise<void>{try{const product=await this.data.getProduct(this.storeKey(),this.product().id);if(!product?.available||product.canOrderNow===false){this.notify.show(product?.canOrderNow===false?'Ordering Closed':'This product is currently unavailable.');return;}if(this.cart.add(product)){if(navigate)void this.router.navigate(['/',this.storeKey(),'cart']);else this.notify.show('Added to cart');}}catch{this.notify.show('Unable to refresh availability. Please try again.');}}
 toggleWishlist(event:Event):void{event.preventDefault();event.stopPropagation();this.wishlist.toggle(this.product()).then(ok => ok ? this.notify.show("Product added to wishlist") : this.notify.show("Could not update wishlist"));}
}
