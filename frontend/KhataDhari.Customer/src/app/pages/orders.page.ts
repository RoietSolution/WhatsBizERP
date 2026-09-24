import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CartService } from '../cart/cart.service';

@Component({
  standalone: true,
  imports: [RouterLink],
  template: `<section class="orders"><span class="icon"><svg viewBox="0 0 24 24"><path d="M7 3h10v3H7zM6 5H5a1 1 0 0 0-1 1v14h16V6a1 1 0 0 0-1-1h-1M8 11h8M8 15h6"/></svg></span><span class="eyebrow">YOUR SHOPPING</span><h1>No orders to show</h1><p>Completed storefront orders will appear here when customer order tracking is available.</p><a [routerLink]="['/', cart.storeKey()]">Continue shopping</a></section>`,
  styles: [`:host{display:grid;min-height:55vh;place-items:center}.orders{display:grid;max-width:410px;justify-items:center;padding:30px;text-align:center}.icon{display:grid;width:58px;height:58px;place-items:center;margin-bottom:15px;border-radius:18px;color:var(--store-primary-dark);background:var(--store-primary-soft)}.icon svg{width:28px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.6}.eyebrow{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.orders h1{margin:7px 0 5px;font:800 23px Manrope,sans-serif}.orders p{margin:0;color:var(--store-muted);font-size:12px;line-height:1.6}.orders a{margin-top:17px;border-radius:11px;padding:11px 16px;color:#fff;background:var(--store-primary);font-size:11px;font-weight:800;text-decoration:none}`],
})
export class OrdersPage { constructor(readonly cart: CartService) {} }
