import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { CustomerSessionService } from '../customer-session.service';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CartService } from '../cart/cart.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { discountPercent, Product, ProductReviewSummary, Store } from '../models/storefront.models';
import { StorefrontNotificationService } from '../storefront-notification.service';

@Component({
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe, RouterLink],
  template: `
    @if (loading()) {
      <div class="detail-skeleton" role="status" aria-label="Loading product"><i></i><div><span></span><span></span><b></b><button></button></div></div>
    } @else if (loadFailed()) {
      <section class="state" role="alert"><span>!</span><h1>We couldn't load this product</h1><p>Check your connection and try again.</p><button type="button" (click)="load()">Retry</button></section>
    } @else if (product(); as item) {
      <a class="back-link" [routerLink]="['/', storeKey]"><span aria-hidden="true">←</span> Back to products</a>
      <article class="detail-card">
        <div class="photo">
          @if (!imageUnavailable()) { <img [src]="item.imageUrl" [alt]="item.name" (error)="imageUnavailable.set(true)" /> }
          @else { <div class="image-placeholder"><svg viewBox="0 0 24 24"><path d="M4 6.5A1.5 1.5 0 0 1 5.5 5h13A1.5 1.5 0 0 1 20 6.5v11a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5zM7 16l3.2-3.4 2.3 2.2 1.8-1.7L18 16M9 9h.01"/></svg><span>Image coming soon</span></div> }
        </div>
        <div class="info">
          @if (item.packSize) { <span class="unit">{{ item.packSize }}</span> }<h1>{{ item.name }}</h1>
          @if (item.description) { <p class="description">{{ item.description }}</p> }
          @if (store()?.showProductRatings && item.ratingCount) { <p class="detail-rating">★ {{ item.averageRating | number:'1.1-1' }} ({{ item.ratingCount }})</p> }
          <div class="price-line"><strong>{{ item.sellingPrice | currency:'INR':'symbol':'1.0-2' }}</strong>@if (item.compareAtPrice) { <del>{{ item.compareAtPrice | currency:'INR':'symbol':'1.0-2' }}</del> }@if(this.discountPercent(item); as discount){<span class="discount">{{discount}}% OFF</span>}</div>
          @if(item.isReturnable === false){<p class="return-policy">Non-returnable</p>} @else if(item.returnWindowDays){<p class="return-policy">{{item.returnWindowDays}}-day return policy</p>}
          <p class="availability" [class.unavailable]="!item.available"><i></i>{{ item.available ? 'In stock and ready to order' : 'Currently out of stock' }}</p>
          @if (item.available && quantity() > 0) {
            <div class="detail-actions"><div class="quantity"><button type="button" (click)="adjust(-1)" aria-label="Decrease quantity">−</button><b>{{ quantity() }}</b><button type="button" (click)="adjust(1)" aria-label="Increase quantity">+</button></div><a [routerLink]="['/', storeKey, 'cart']">View cart</a></div>
          } @else {
            <button class="add-button" type="button" [disabled]="!item.available" (click)="add(item)">{{ item.available ? 'Add to cart' : 'Out of stock' }}</button>
          }
          <div class="service-note"><svg viewBox="0 0 24 24"><path d="M20 7 10 17l-5-5"/></svg><span>Price and availability are verified again at checkout.</span></div>
        </div>
      </article>
      @if (store()?.showProductReviews) {
        <section class="reviews"><header><h2>Ratings & reviews</h2>@if(reviewSummary()?.ratingCount){<p>★ {{reviewSummary()?.averageRating | number:'1.1-1'}} from {{reviewSummary()?.ratingCount}} rating(s)</p>}</header>
          @if(session.active()){
            <form (submit)="saveReview($event)"><label>Your rating<select [value]="reviewRating" (change)="reviewRating=+$any($event.target).value" required><option value="5">5 stars</option><option value="4">4 stars</option><option value="3">3 stars</option><option value="2">2 stars</option><option value="1">1 star</option></select></label><label>Your review<textarea [value]="reviewText" (input)="reviewText=$any($event.target).value" minlength="2" maxlength="1000" required></textarea></label><button type="submit" [disabled]="savingReview()">{{savingReview()?'Saving...':'Save review'}}</button><span>{{reviewMessage()}}</span></form>
          } @else { <p class="review-signin"><a [routerLink]="['/',storeKey,'auth']">Sign in</a> after purchasing to write a review.</p> }
          <div class="review-list">@for(review of reviewSummary()?.reviews ?? [];track review.reviewId){<article><strong>{{review.reviewerName}}</strong><b>{{stars(review.rating)}}</b><p>{{review.reviewText}}</p><time>{{review.createdAt.slice(0,10)}}</time></article>}@empty{<p>No reviews yet.</p>}</div>
        </section>
      }
    } @else {
      <section class="state"><span>?</span><h1>Product unavailable</h1><p>This product may have been removed from the store.</p><a [routerLink]="['/', storeKey]">Continue shopping</a></section>
    }
  `,
  styles: [`
    :host{display:block}.back-link{display:inline-flex;align-items:center;gap:7px;margin-bottom:16px;color:var(--store-muted);font-size:11px;font-weight:700;text-decoration:none}.back-link:hover{color:var(--store-primary)}.detail-card{display:grid;grid-template-columns:minmax(0,1.05fr) minmax(330px,.8fr);gap:36px;border:1px solid var(--store-border);border-radius:22px;padding:24px;background:var(--store-surface)}.photo{display:grid;min-height:430px;overflow:hidden;place-items:center;border-radius:17px;background:#f4f6f2}.photo img{width:100%;height:100%;max-height:520px;object-fit:contain;padding:18px}.image-placeholder{display:grid;justify-items:center;color:#8f9a92}.image-placeholder svg{width:58px;fill:none;stroke:#a6b0a8;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.2}.image-placeholder span{margin-top:10px;font-size:11px}.info{align-self:center;padding:20px 12px}.unit{display:inline-flex;border-radius:7px;padding:5px 8px;color:var(--store-muted);background:var(--store-surface-muted);font-size:10px;font-weight:700}.info h1{margin:12px 0 9px;color:var(--store-text);font:800 30px/1.2 Manrope,sans-serif;letter-spacing:-1px}.description{margin:0;color:var(--store-muted);font-size:13px;line-height:1.65}.discount{color:var(--store-danger);font-size:11px;font-weight:850}.price-line{display:flex;align-items:baseline;gap:10px;margin-top:22px}.price-line strong{font:800 27px Manrope,sans-serif}.price-line del{color:#98a099;font-size:13px}.availability{display:flex;align-items:center;gap:8px;margin:18px 0;color:var(--store-success);font-size:11px;font-weight:700}.availability i{width:8px;height:8px;border-radius:50%;background:currentColor}.availability.unavailable{color:var(--store-danger)}.add-button{width:100%;min-height:48px;border:0;border-radius:12px;color:#fff;background:var(--store-primary);font-size:12px;font-weight:800;cursor:pointer}.add-button:disabled{background:#a9b0aa;cursor:not-allowed}.detail-actions{display:grid;grid-template-columns:140px 1fr;gap:10px}.quantity{display:grid;grid-template-columns:42px 1fr 42px;min-height:48px;overflow:hidden;border-radius:12px;color:#fff;background:var(--store-primary)}.quantity button{border:0;color:inherit;background:transparent;font-size:21px;cursor:pointer}.quantity b{display:grid;place-items:center}.detail-actions>a{display:grid;place-items:center;border:1px solid var(--store-primary);border-radius:12px;color:var(--store-primary-dark);font-size:12px;font-weight:800;text-decoration:none}.service-note{display:flex;align-items:center;gap:8px;margin-top:18px;color:var(--store-muted);font-size:10px}.detail-rating{margin:10px 0 0;color:#a56500;font-size:13px;font-weight:800}.reviews{margin-top:20px;border:1px solid var(--store-border);border-radius:18px;padding:20px;background:var(--store-surface)}.reviews h2{margin:0;font:800 20px Manrope,sans-serif}.reviews header p,.review-signin{color:var(--store-muted);font-size:12px}.reviews form{display:grid;grid-template-columns:160px minmax(0,1fr) auto;gap:10px;align-items:end;margin:16px 0}.reviews label{display:grid;gap:5px;font-size:11px;font-weight:700}.reviews select,.reviews textarea{border:1px solid var(--store-border);border-radius:9px;padding:9px;background:#fff}.reviews textarea{min-height:70px;resize:vertical}.reviews form button{min-height:42px;border:0;border-radius:9px;padding:0 15px;color:#fff;background:var(--store-primary);font-weight:800}.review-list article{padding:14px 0;border-top:1px solid var(--store-border)}.review-list b{margin-left:10px;color:#a56500;letter-spacing:1px}.review-list p{margin:7px 0;font-size:13px}.review-list time{color:var(--store-muted);font-size:10px}@media(max-width:650px){.reviews form{grid-template-columns:1fr}}.service-note svg{width:17px;fill:none;stroke:var(--store-primary);stroke-linecap:round;stroke-linejoin:round;stroke-width:2}.state{display:grid;min-height:55vh;align-content:center;justify-items:center;text-align:center}.state>span{display:grid;width:52px;height:52px;place-items:center;border-radius:16px;color:var(--store-primary-dark);background:var(--store-primary-soft);font:800 22px Manrope,sans-serif}.state h1{margin:14px 0 5px;font:800 21px Manrope,sans-serif}.state p{margin:0;color:var(--store-muted);font-size:12px}.state button,.state a{margin-top:16px;border:0;border-radius:10px;padding:10px 16px;color:#fff;background:var(--store-primary);font-size:11px;font-weight:800;text-decoration:none;cursor:pointer}.detail-skeleton{display:grid;grid-template-columns:1.05fr .8fr;gap:36px}.detail-skeleton i,.detail-skeleton span,.detail-skeleton b,.detail-skeleton button{display:block;border:0;border-radius:12px;background:#e9ede8}.detail-skeleton>i{height:470px;border-radius:18px}.detail-skeleton>div{align-self:center}.detail-skeleton span{width:70%;height:18px;margin:12px 0}.detail-skeleton span+span{width:90%;height:36px}.detail-skeleton b{width:110px;height:28px;margin-top:28px}.detail-skeleton button{width:100%;height:48px;margin-top:35px}
    @media(max-width:720px){.detail-card{grid-template-columns:1fr;gap:8px;padding:12px;border-radius:18px}.photo{min-height:300px}.photo img{height:300px;padding:12px}.info{padding:15px 7px 8px}.info h1{font-size:24px}.detail-skeleton{grid-template-columns:1fr;gap:15px}.detail-skeleton>i{height:300px}}
    @media(max-width:390px){.photo{min-height:255px}.photo img{height:255px}.detail-actions{grid-template-columns:125px 1fr}}
  `],
})
export class ProductDetailsPage implements OnInit { readonly discountPercent = discountPercent;
  readonly product=signal<Product|null>(null);readonly store=signal<Store|null>(null);readonly reviewSummary=signal<ProductReviewSummary|null>(null);readonly savingReview=signal(false);readonly reviewMessage=signal('');reviewRating=5;reviewText='';readonly loading=signal(true);readonly loadFailed=signal(false);readonly imageUnavailable=signal(false);storeKey='';private productId='';
  readonly quantity=computed(()=>this.cart.lines().find(line=>line.product.id===this.product()?.id)?.quantity??0);
  constructor(private readonly route:ActivatedRoute,private readonly data:StorefrontDataService,private readonly cart:CartService,readonly session:CustomerSessionService,private readonly notify:StorefrontNotificationService){}
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';this.productId=this.route.snapshot.paramMap.get('productId')??'';void this.load();}
  add(product:Product):void{this.cart.add(product);}adjust(change:number):void{const id=this.product()?.id;if(id)this.cart.adjust(id,change);}
  stars(value:number):string{return '★'.repeat(value)+'☆'.repeat(5-value);}
  async saveReview(event:Event):Promise<void>{event.preventDefault();this.savingReview.set(true);this.reviewMessage.set('');try{await this.data.saveReview(this.storeKey,this.productId,this.reviewRating,this.reviewText.trim());this.reviewSummary.set(await this.data.getReviews(this.storeKey,this.productId));this.reviewMessage.set('Review saved.');}catch(error:any){this.reviewMessage.set(error?.error?.message??'Unable to save review.');}finally{this.savingReview.set(false);}}
  async load():Promise<void>{this.loading.set(true);this.loadFailed.set(false);this.imageUnavailable.set(false);try{const [product,store,reviews]=await Promise.all([this.data.getProduct(this.storeKey,this.productId),this.data.getStore(this.storeKey),this.data.getReviews(this.storeKey,this.productId)]);this.product.set(product);this.store.set(store);this.reviewSummary.set(reviews);const own=reviews.reviews.find(x=>x.isOwn);if(own){this.reviewRating=own.rating;this.reviewText=own.reviewText;}}catch{this.loadFailed.set(true);}finally{this.loading.set(false);}}
}



