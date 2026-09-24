import { Component, OnInit, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CartService } from '../cart/cart.service';
import { StorefrontDataService } from '../data/storefront-data.service';

@Component({
  standalone: true,
  imports: [CurrencyPipe, RouterLink],
  template: `
    <div class="heading"><span class="eyebrow">YOUR BAG</span><h1>Your cart <small>{{ cart.itemCount() }} items</small></h1>@if (checking()) { <p class="cart-status" role="status">Checking current availability…</p> } @else if (refreshFailed()) { <p class="cart-status" role="alert">Availability could not be refreshed. Please check again before ordering.</p> }</div>
    @if (cart.lines().length) {
      <div class="cart-layout">
        <section class="lines" aria-label="Cart items">
          @for (line of cart.lines(); track line.product.id) {
            <article class="line">
              <img [src]="line.product.imageUrl" [alt]="line.product.name" (error)="imageFailed($event)" />
              <div class="line-info"><a [routerLink]="['/', cart.storeKey(), 'products', line.product.id]">{{ line.product.name }}</a><small>{{ line.product.unitLabel }}</small><strong>{{ line.product.sellingPrice * line.quantity | currency:'INR':'symbol':'1.0-2' }}</strong>
                @if (!line.product.available) { <em>Now out of stock - remove this item to continue</em> }
              </div>
              <div class="controls"><button type="button" (click)="cart.adjust(line.product.id,-1)" [attr.aria-label]="'Decrease ' + line.product.name">-</button><span>{{ line.quantity }}</span><button type="button" (click)="cart.adjust(line.product.id,1)" [disabled]="!line.product.available" [attr.aria-label]="'Increase ' + line.product.name">+</button>
                <button class="remove" type="button" (click)="cart.remove(line.product.id)">Remove</button>
              </div>
            </article>
          }
        </section>
        <aside class="summary"><h2>Order summary</h2><div><span>Subtotal</span><b>{{ cart.total() | currency:'INR':'symbol':'1.0-2' }}</b></div><div><span>Delivery</span><b class="muted">Calculated later</b></div><hr /><div class="total"><span>Total</span><b>{{ cart.total() | currency:'INR':'symbol':'1.0-2' }}</b></div><button type="button" disabled>Checkout coming soon</button><p>Online ordering will be available in a future update.</p><a [routerLink]="['/', cart.storeKey()]">Continue shopping</a></aside>
      </div>
    } @else {
      <section class="empty"><div class="basket">&#128722;</div><h2>Your cart is taking a little break</h2><p>Explore the store and add something you love.</p><a [routerLink]="['/', cart.storeKey()]">Browse products</a></section>
    }
  `,
  styles: [`
    :host{display:block}.heading{margin-bottom:20px}.eyebrow{color:#83907f;font-size:9px;font-weight:800;letter-spacing:1.7px}.heading h1{display:flex;align-items:center;gap:10px;margin:5px 0;color:#263329;font:800 25px Manrope,sans-serif}.heading h1 small{padding:5px 9px;border-radius:99px;color:#69756b;background:#f0f3ec;font:600 10px Inter,sans-serif}.cart-layout{display:grid;grid-template-columns:minmax(0,1fr) 310px;align-items:start;gap:18px}.lines,.summary{border:1px solid #eceee7;border-radius:18px;background:#fff}.line{display:grid;grid-template-columns:88px minmax(0,1fr) auto;align-items:center;gap:14px;padding:15px;border-bottom:1px solid #f0f1ec}.line:last-child{border-bottom:0}.line>img{width:88px;height:88px;border-radius:13px;object-fit:cover;background:#f4f4ef}.line-info{display:grid;gap:5px}.line-info a{color:#2a352d;font-size:13px;font-weight:750;text-decoration:none}.line-info small{color:#8b9087;font-size:10px}.line-info strong{font-size:13px}.line-info em{color:#b25346;font-size:10px;font-style:normal}.controls{display:flex;align-items:center;gap:8px}.controls button:not(.remove){width:29px;height:29px;border:1px solid #e4e8df;border-radius:9px;color:var(--green);background:#f7faf6;font-size:18px;cursor:pointer}.controls button:disabled{opacity:.45;cursor:not-allowed}.controls span{min-width:12px;text-align:center;font-size:12px;font-weight:700}.controls .remove{display:block;margin-left:3px;border:0;color:#a45e50;background:transparent;font-size:10px;cursor:pointer}.summary{padding:20px}.summary h2{margin:0 0 17px;font:800 16px Manrope,sans-serif}.summary>div{display:flex;justify-content:space-between;margin:12px 0;color:#70776f;font-size:12px}.summary b{color:#303a31}.summary .muted{color:#989d95;font-weight:500}.summary hr{border:0;border-top:1px solid #eceee7}.summary .total{color:#28362a;font-size:14px;font-weight:800}.summary>button{width:100%;min-height:44px;margin-top:10px;border:0;border-radius:12px;color:#fff;background:#aeb7ad;font-size:12px;font-weight:800}.summary p{color:#8c9289;font-size:10px;line-height:1.5}.summary>a{display:block;margin-top:13px;color:var(--green);text-align:center;font-size:11px;font-weight:700;text-decoration:none}.empty{display:grid;justify-items:center;padding:60px 20px;border:1px solid #eceee7;border-radius:20px;background:#fff;text-align:center}.basket{font-size:48px}.empty h2{margin:12px 0 4px;font:800 19px Manrope,sans-serif}.empty p{color:#7e857d;font-size:12px}.empty a{margin-top:12px;padding:12px 17px;border-radius:12px;color:#fff;background:var(--green);font-size:12px;font-weight:700;text-decoration:none}@media(max-width:720px){.cart-layout{grid-template-columns:1fr}.summary{order:-1}.line{grid-template-columns:68px minmax(0,1fr);gap:11px;padding:12px}.line>img{width:68px;height:68px}.controls{grid-column:2;justify-content:flex-start}.controls .remove{margin-left:auto}}
  `],
})
export class CartPage implements OnInit {
  readonly checking = signal(true);
  readonly refreshFailed = signal(false);
  constructor(readonly cart: CartService, private readonly route: ActivatedRoute, private readonly data: StorefrontDataService) {}
  async ngOnInit(): Promise<void> {
    try {
      const storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? '';
      this.cart.refreshProducts(await this.data.getProducts(storeKey));
    } catch { this.refreshFailed.set(true); }
    finally { this.checking.set(false); }
  }
  imageFailed(event: Event): void { (event.target as HTMLImageElement).src = '/images/product-placeholder.svg'; }
}
