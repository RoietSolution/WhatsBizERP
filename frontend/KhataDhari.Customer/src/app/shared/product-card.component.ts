import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Product } from '../models/storefront.models';
import { CartService } from '../cart/cart.service';

@Component({
  selector: 'shop-product-card',
  standalone: true,
  imports: [RouterLink],
  template: `
    <article class="product-card">
      <a class="product-image" [routerLink]="['/', storeKey(), 'products', product().id]" [attr.aria-label]="'View ' + product().name">
        <img [src]="product().imageUrl" [alt]="product().name" loading="lazy" (error)="imageFailed($event)" />
        @if (product().badge) { <span class="product-badge">{{ product().badge }}</span> }
      </a>
      <div class="product-info">
        <span class="unit-label">{{ product().unitLabel }}</span>
        <a class="product-name" [routerLink]="['/', storeKey(), 'products', product().id]">{{ product().name }}</a>
        <div class="product-buy-row">
          <div>
            <strong class="price">₹{{ product().sellingPrice }}</strong>
            @if (product().compareAtPrice) { <del>₹{{ product().compareAtPrice }}</del> }
          </div>
          @if (product().available) {
            <button class="add-button" type="button" (click)="add()" aria-label="Add to cart">+</button>
          } @else {
            <span class="stock-out">Out of stock</span>
          }
        </div>
      </div>
    </article>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .product-card { overflow: hidden; height: 100%; border: 1px solid #eeeee6; border-radius: 18px; background: #fff; }
    .product-image { position: relative; display: block; overflow: hidden; aspect-ratio: 1 / .88; background: #f3f2eb; }
    img { width: 100%; height: 100%; object-fit: cover; transition: transform .25s ease; }
    .product-card:hover img { transform: scale(1.04); }
    .product-badge { position: absolute; top: 10px; left: 10px; border-radius: 999px; padding: 5px 9px; color: #164d39; background: #fff8e6; font-size: 10px; font-weight: 700; }
    .product-info { padding: 11px 12px 13px; }
    .unit-label { color: #888b81; font-size: 11px; }
    .product-name { display: block; min-height: 38px; margin-top: 3px; color: #202b25; font-size: 13px; line-height: 1.4; font-weight: 700; text-decoration: none; }
    .product-buy-row { display: flex; justify-content: space-between; align-items: center; gap: 6px; margin-top: 9px; }
    .price { color: #172c22; font-size: 15px; }
    del { margin-left: 5px; color: #a1a197; font-size: 10px; }
    .add-button { width: 36px; height: 32px; border: 1px solid #c9e0d1; border-radius: 9px; color: #145c43; background: #f4fbf5; font-size: 22px; line-height: 1; font-weight: 500; cursor: pointer; }
    .add-button:hover { color: #fff; background: #145c43; }
    .stock-out { color: #b44a37; font-size: 10px; font-weight: 700; }
  `],
})
export class ProductCardComponent {
  readonly product = input.required<Product>();
  readonly storeKey = input.required<string>();
  constructor(private readonly cart: CartService) {}
  add(): void { this.cart.add(this.product()); }
  imageFailed(event: Event): void { (event.target as HTMLImageElement).src = '/images/product-placeholder.svg'; }
}
