import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Product } from '../models/storefront.models';
import { CartService } from '../cart/cart.service';
import { WishlistService } from '../wishlist.service';

@Component({
 selector:'shop-product-card',standalone:true,
 imports:[CurrencyPipe,DecimalPipe,RouterLink],
 template:`<article class="product-card" [class.unavailable]="!product().available">
 <button class="heart" type="button" (click)="toggleWishlist($event)" [class.saved]="wishlist.has(product().id)" [attr.aria-label]="wishlist.has(product().id)?'Remove '+product().name+' from wishlist':'Add '+product().name+' to wishlist'"><span [innerHTML]="wishlist.has(product().id)?'&#9829;':'&#9825;'"></span></button>
 <a class="product-image" [routerLink]="['/',storeKey(),'products',product().id]">
 @if(!imageUnavailable()){<img [src]="product().imageUrl" [alt]="product().name" loading="lazy" (error)="imageUnavailable.set(true)"/>}@else{<span class="placeholder">Image coming soon</span>}
 @if(product().badge){<span class="badge">{{product().badge}}</span>}</a>
 <div class="info"><a class="name" [routerLink]="['/',storeKey(),'products',product().id]">{{product().name}}</a>
 @if(product().packSize){<span class="pack">{{product().packSize}}</span>}
 @if(product().ratingCount){<span class="rating">&#9733; {{product().averageRating|number:'1.1-1'}} ({{product().ratingCount}})</span>}
 <div class="price"><strong>{{product().sellingPrice|currency:'INR':'symbol':'1.0-2'}}</strong>@if(product().compareAtPrice){<del>{{product().compareAtPrice|currency:'INR':'symbol':'1.0-0'}}</del>}</div>
 <small class="tax">Inclusive of all taxes</small>
 @if(!product().available){<span class="stock">OUT OF STOCK</span>}@else{<div class="purchase-actions"><button class="add" type="button" (click)="add()">{{quantity()?'In cart ('+quantity()+')':'Add to cart'}}</button><button class="buy" type="button" (click)="buyNow()">Buy now</button></div>}
 </div></article>`,
 styles:[`:host{display:block;min-width:0}.product-card{position:relative;display:flex;height:100%;min-width:0;flex-direction:column;overflow:hidden;border:1px solid var(--store-border);border-radius:17px;background:var(--store-surface)}.heart{position:absolute;z-index:3;top:8px;right:8px;display:grid;width:38px;height:38px;place-items:center;border:1px solid var(--store-border);border-radius:50%;color:#59645d;background:rgba(255,255,255,.94);font-size:22px;cursor:pointer}.heart.saved{color:#cf3154}.product-image{position:relative;display:block;overflow:hidden;aspect-ratio:1.18/1;background:#f4f6f2}.product-image img{width:100%;height:100%;object-fit:contain;padding:8px}.placeholder{display:grid;height:100%;place-items:center;color:var(--store-muted);font-size:9px}.badge{position:absolute;top:8px;left:8px;border-radius:99px;padding:4px 7px;background:var(--store-accent-soft);font-size:8px;font-weight:800}.info{display:flex;flex:1;min-width:0;flex-direction:column;padding:10px 11px 11px}.name{display:-webkit-box;min-height:35px;overflow:hidden;color:var(--store-text);font-size:12px;line-height:1.45;font-weight:750;text-decoration:none;-webkit-box-orient:vertical;-webkit-line-clamp:2}.pack{margin-top:3px;color:var(--store-muted);font-size:10px}.rating{margin-top:5px;color:#9a6810;font-size:10px;font-weight:800}.price{display:flex;align-items:baseline;gap:6px;margin-top:7px}.price strong{font:800 14px Manrope,sans-serif;white-space:nowrap}.price del{color:#9ba39d;font-size:9px}.tax{color:var(--store-muted);font-size:8px}.purchase-actions{display:flex;align-items:stretch;gap:7px;margin-top:auto;padding-top:11px}.purchase-actions button{flex:1 1 0;min-width:0;min-height:36px;border-radius:9px;padding:6px 4px;font-size:10px;font-weight:800;line-height:1.2;cursor:pointer;transition:background .16s,box-shadow .16s,transform .16s}.purchase-actions button:hover{transform:translateY(-1px);box-shadow:0 3px 10px rgba(20,92,67,.14)}.purchase-actions button:focus-visible{outline:2px solid var(--store-primary);outline-offset:2px}.add{border:1px solid var(--store-primary);color:var(--store-primary-dark);background:#fff}.add:hover{background:var(--store-primary-soft)}.buy{border:1px solid var(--store-primary);color:#fff;background:var(--store-primary)}.buy:hover{background:var(--store-primary-dark)}.stock{margin-top:auto;padding-top:12px;color:var(--store-danger);font-size:9px;font-weight:850}.unavailable img{opacity:.65;filter:saturate(.55)}@media(max-width:390px){.product-card{border-radius:14px}.product-image{aspect-ratio:1.05/1}.info{padding:9px 8px}.purchase-actions{gap:4px}.purchase-actions button{font-size:9px}.heart{width:36px;height:36px}}@media(max-width:360px){.name{font-size:11px}.price strong{font-size:12px}.purchase-actions button{min-height:34px}}`]
})
export class ProductCardComponent implements OnInit{
 readonly product=input.required<Product>();readonly storeKey=input.required<string>();readonly imageUnavailable=signal(false);
 readonly quantity=computed(()=>this.cart.lines().find(x=>x.product.id===this.product().id)?.quantity??0);
 constructor(private readonly cart:CartService,readonly wishlist:WishlistService,private readonly router:Router){}
 ngOnInit():void{this.wishlist.initialize(this.storeKey());}
 add():void{this.cart.add(this.product());}
 buyNow():void{if(this.product().available){this.cart.add(this.product());void this.router.navigate(['/',this.storeKey(),'cart']);}}
 toggleWishlist(event:Event):void{event.preventDefault();event.stopPropagation();void this.wishlist.toggle(this.product());}
}
