import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Product } from '../models/storefront.models';
import { ProductCardComponent } from '../shared/product-card.component';
import { WishlistService } from '../wishlist.service';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontDataService } from '../data/storefront-data.service';
@Component({selector:'shop-wishlist-page',standalone:true,imports:[RouterLink,ProductCardComponent],
template:`<section><span class="eyebrow">SAVED FOR LATER</span><h1>Your wishlist</h1>
@if(loading()){<div class="state">Loading wishlist...</div>}
@else if(!session.active()&&!products().length){<div class="state"><h2>Wishlist saved on this device</h2><p>Complete checkout to create a secure customer session and sync it to your account.</p><a [routerLink]="['/',storeKey]">Browse products</a></div>}
@else if(!products().length){<div class="state"><h2>Your wishlist is empty</h2><a [routerLink]="['/',storeKey]">Browse products</a></div>}
@else{<div class="grid">@for(product of products();track product.id){<shop-product-card [product]="product" [storeKey]="storeKey"/>}</div>}</section>`,
styles:[`.eyebrow{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}h1{margin:4px 0 18px;font:800 25px Manrope,sans-serif}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:13px}.state{display:grid;min-height:280px;align-content:center;justify-items:center;border:1px solid var(--store-border);border-radius:18px;padding:24px;background:#fff;text-align:center}.state p{color:var(--store-muted);font-size:11px}.state a{margin-top:12px;color:var(--store-primary-dark);font-weight:800}@media(max-width:850px){.grid{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:560px){.grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}}`]})
export class WishlistPage implements OnInit{readonly products=signal<Product[]>([]);readonly loading=signal(true);storeKey='';constructor(private readonly route:ActivatedRoute,readonly wishlist:WishlistService,readonly session:CustomerSessionService,private readonly data:StorefrontDataService){}ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';this.session.restore(this.storeKey);this.wishlist.initialize(this.storeKey);void this.load();}private async load():Promise<void>{if(this.session.active())this.products.set(await this.wishlist.products());else{const ids=this.wishlist.ids();this.products.set((await this.data.getProducts(this.storeKey)).filter(product=>ids.has(product.id)));}this.loading.set(false);}}
