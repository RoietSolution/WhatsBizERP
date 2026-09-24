import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CartService } from '../cart/cart.service';

@Component({
  standalone: true,
  imports: [RouterLink],
  template: `<section class="orders"><span class="icon">&#128203;</span><span class="eyebrow">YOUR SHOPPING</span><h1>Orders</h1><p>Your orders will show up here once online ordering is available.</p><a [routerLink]="['/', cart.storeKey()]">Back to the store</a></section>`,
  styles: [`:host{display:grid;min-height:55vh;place-items:center}.orders{max-width:390px;padding:30px;text-align:center}.icon{display:block;margin-bottom:17px;font-size:42px}.eyebrow{color:#859080;font-size:9px;font-weight:800;letter-spacing:1.7px}.orders h1{margin:7px 0;font:800 27px Manrope,sans-serif;color:#28372b}.orders p{color:#788077;font-size:13px;line-height:1.6}.orders a{display:inline-block;margin-top:9px;color:var(--green);font-size:12px;font-weight:700;text-decoration:none}`],
})
export class OrdersPage { constructor(readonly cart: CartService) {} }
