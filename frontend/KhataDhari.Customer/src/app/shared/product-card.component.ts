import { CurrencyPipe } from '@angular/common';
import { Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Product } from '../models/storefront.models';
import { CartService } from '../cart/cart.service';

@Component({
  selector: 'shop-product-card',
  standalone: true,
  imports: [CurrencyPipe, RouterLink],
  template: `
    <article class="product-card" [class.unavailable]="!product().available">
      <a class="product-image" [routerLink]="['/', storeKey(), 'products', product().id]" [attr.aria-label]="'View ' + product().name">
        @if (!imageUnavailable()) {
          <img [src]="product().imageUrl" [alt]="product().name" loading="lazy" (error)="imageFailed()" />
        } @else {
          <span class="image-placeholder" role="img" [attr.aria-label]="'Image unavailable for ' + product().name">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6.5A1.5 1.5 0 0 1 5.5 5h13A1.5 1.5 0 0 1 20 6.5v11a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5zM7 16l3.2-3.4 2.3 2.2 1.8-1.7L18 16M9 9h.01"/></svg><small>Image coming soon</small>
          </span>
        }
        @if (product().badge) { <span class="product-badge">{{ product().badge }}</span> }
      </a>
      <div class="product-info">
        <a class="product-name" [routerLink]="['/', storeKey(), 'products', product().id]">{{ product().name }}</a>
        @if (product().unitLabel) { <span class="unit-label">{{ product().unitLabel }}</span> }
        <div class="product-buy-row">
          <div class="price-wrap"><strong class="price">{{ product().sellingPrice | currency:'INR':'symbol':'1.0-2' }}</strong>@if (product().compareAtPrice) { <del>{{ product().compareAtPrice | currency:'INR':'symbol':'1.0-0' }}</del> }</div>
          @if (!product().available) {
            <span class="stock-out">OUT OF STOCK</span>
          } @else if (quantity() > 0) {
            <div class="quantity" aria-label="Cart quantity controls"><button type="button" (click)="adjust(-1)" [attr.aria-label]="'Decrease ' + product().name">−</button><b>{{ quantity() }}</b><button type="button" (click)="adjust(1)" [attr.aria-label]="'Increase ' + product().name">+</button></div>
          } @else {
            <button class="add-button" type="button" (click)="add()" [attr.aria-label]="'Add ' + product().name + ' to cart'">ADD</button>
          }
        </div>
      </div>
    </article>
  `,
  styles: [`
    :host{display:block;min-width:0}.product-card{display:flex;height:100%;flex-direction:column;overflow:hidden;border:1px solid var(--store-border);border-radius:17px;background:var(--store-surface);transition:transform .18s,box-shadow .18s,border-color .18s}.product-card:hover{transform:translateY(-2px);border-color:#cfd9d0;box-shadow:var(--store-shadow)}.product-image{position:relative;display:block;overflow:hidden;aspect-ratio:1.18/1;background:#f4f6f2}.product-image img{width:100%;height:100%;object-fit:contain;padding:8px;transition:transform .2s}.product-card:hover img{transform:scale(1.025)}.product-badge{position:absolute;top:8px;left:8px;border:1px solid #f2ddb3;border-radius:999px;padding:4px 7px;color:#84540b;background:var(--store-accent-soft);font-size:8px;font-weight:800;letter-spacing:.2px}.image-placeholder{display:grid;width:100%;height:100%;align-content:center;justify-items:center;color:#95a199}.image-placeholder svg{width:34px;fill:none;stroke:#aab4ac;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.3}.image-placeholder small{margin-top:7px;font-size:9px}.product-info{display:flex;flex:1;flex-direction:column;padding:10px 11px 11px}.product-name{display:-webkit-box;min-height:36px;overflow:hidden;color:var(--store-text);font-size:12px;line-height:1.45;font-weight:750;text-decoration:none;-webkit-box-orient:vertical;-webkit-line-clamp:2}.unit-label{margin-top:3px;color:var(--store-muted);font-size:10px}.product-buy-row{display:flex;min-height:34px;align-items:end;justify-content:space-between;gap:7px;margin-top:auto;padding-top:10px}.price-wrap{display:grid;min-width:0}.price{color:var(--store-text);font:800 14px Manrope,sans-serif;white-space:nowrap}.price-wrap del{color:#9ba39d;font-size:9px}.add-button{min-width:60px;height:32px;border:1px solid var(--store-primary);border-radius:9px;color:var(--store-primary-dark);background:var(--store-primary-soft);font-size:11px;font-weight:800;cursor:pointer}.add-button:hover{color:#fff;background:var(--store-primary)}.quantity{display:grid;grid-template-columns:28px 26px 28px;height:32px;overflow:hidden;border-radius:9px;color:#fff;background:var(--store-primary)}.quantity button{border:0;color:inherit;background:transparent;font-size:18px;line-height:1;cursor:pointer}.quantity b{display:grid;place-items:center;font-size:11px}.stock-out{align-self:center;color:var(--store-danger);font-size:8px;font-weight:800;letter-spacing:.3px;white-space:nowrap}.unavailable .product-image img{opacity:.65;filter:saturate(.55)}
    @media(max-width:560px){.product-card{border-radius:14px}.product-image{aspect-ratio:1.05/1}.product-info{padding:9px}.product-name{min-height:34px;font-size:11.5px}.product-buy-row{gap:4px}.price{font-size:12.5px}.add-button{min-width:52px;height:31px}.quantity{grid-template-columns:25px 23px 25px;height:31px}.stock-out{font-size:7px}}
    @media(max-width:350px){.product-info{padding:8px 7px}.price{font-size:11.5px}.add-button{min-width:48px}.quantity{grid-template-columns:23px 21px 23px}}
  `],
})
export class ProductCardComponent {
  readonly product=input.required<Product>(); readonly storeKey=input.required<string>(); readonly imageUnavailable=signal(false);
  readonly quantity=computed(()=>this.cart.lines().find(line=>line.product.id===this.product().id)?.quantity??0);
  constructor(private readonly cart:CartService){}
  add():void{this.cart.add(this.product());} adjust(change:number):void{this.cart.adjust(this.product().id,change);} imageFailed():void{this.imageUnavailable.set(true);}
}
