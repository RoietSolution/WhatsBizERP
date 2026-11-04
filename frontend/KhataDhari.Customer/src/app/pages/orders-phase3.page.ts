import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { CustomerOrder } from '../models/storefront.models';
import { orderStatusLabel, paymentLabel } from '../order-status-presentation';

@Component({
  selector: 'shop-orders-page',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, RouterLink],
  template: `
    <section class="page orders-page">
      <span class="eyebrow">YOUR SHOPPING</span>
      <h1>Your orders</h1>
      @if (loading()) { <p class="state" role="status">Loading your orders...</p> }
      @else if (!session.active()) {
        <div class="state"><h2>Sign in to view your orders</h2><p>Only orders belonging to your verified customer account are shown here.</p><a class="primary-link" [routerLink]="['/', storeKey, 'auth']">Sign in</a></div>
      } @else if (!orders().length) {
        <div class="state"><span class="empty-icon" aria-hidden="true">⌁</span><h2>No orders yet</h2><p>Your storefront orders will appear here after your first order.</p><a class="primary-link" [routerLink]="['/', storeKey]">Continue shopping</a></div>
      } @else {
        <div class="orders" aria-label="Your orders">
          @for (order of orders(); track order.id) {
            <article class="order-card">
              <div class="order-card-header"><div class="order-reference"><span class="order-label">ORDER</span><strong>{{ order.orderNumber }}</strong><time [attr.datetime]="order.placedAt">{{ order.placedAt | date:'medium' }}</time></div><strong class="order-total">{{ order.total | currency:'INR' }}</strong></div>
              <div class="order-badges"><span class="badge" [class]="statusClass(order.status)">{{ orderStatusLabel(order.status) }}</span><span class="badge badge-muted">{{ order.itemCount }} {{ order.itemCount === 1 ? 'item' : 'items' }}</span></div>
              <div class="order-meta"><span><b>Payment</b> {{ paymentLabel(order.paymentStatus, order.paymentMethod) }}</span>@if (order.deliveryStatus) { <span><b>Delivery</b> {{ orderStatusLabel(order.deliveryStatus) }}</span> }</div>
              <a class="details-link" [routerLink]="['/', storeKey, 'orders', order.id]">View order details <span aria-hidden="true">→</span></a>
            </article>
          }
        </div>
      }
    </section>
  `,
  styles: [`
    :host{display:block}.page{max-width:820px;margin:auto;min-width:0}.eyebrow,.order-label{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.page>h1{margin:4px 0 18px;font:800 25px Manrope,sans-serif}.state{display:grid;min-height:260px;align-content:center;justify-items:center;gap:8px;border:1px solid var(--store-border);border-radius:18px;padding:25px;background:#fff;text-align:center}.state h2{margin:2px 0;font:800 17px Manrope,sans-serif}.state p{max-width:480px;margin:0;color:var(--store-muted);font-size:12px;line-height:1.6}.empty-icon{display:grid;place-items:center;width:42px;height:42px;border-radius:50%;color:var(--store-primary-dark);background:var(--store-primary-soft);font-size:24px}.primary-link,.details-link{color:var(--store-primary-dark);font-weight:800;text-decoration:none}.primary-link{margin-top:10px;padding:11px 15px;border-radius:10px;background:var(--store-primary);color:#fff;font-size:12px}.orders{display:grid;gap:12px}.order-card{display:grid;gap:13px;border:1px solid var(--store-border);border-radius:16px;padding:16px;background:#fff;box-shadow:0 8px 22px rgba(15,23,42,.04);min-width:0}.order-card-header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.order-reference{display:grid;gap:3px;min-width:0}.order-reference strong{font-size:14px;overflow-wrap:anywhere}.order-reference time{color:var(--store-muted);font-size:11px}.order-total{white-space:nowrap;font:800 16px Manrope,sans-serif}.order-badges{display:flex;flex-wrap:wrap;gap:7px}.badge{display:inline-flex;align-items:center;min-height:25px;padding:4px 9px;border-radius:999px;background:var(--store-primary-soft);color:var(--store-primary-dark);font-size:10px;font-weight:800}.badge-muted{background:var(--store-surface-muted);color:var(--store-muted)}.badge-warning{background:#fff7d6;color:#80651a}.badge-danger{background:#fff0ed;color:var(--store-danger)}.badge-neutral{background:#eef2f1;color:var(--store-muted)}.order-meta{display:flex;flex-wrap:wrap;gap:8px 18px;padding-top:12px;border-top:1px solid var(--store-border);color:var(--store-muted);font-size:11px;line-height:1.5}.order-meta b{color:var(--store-text);font-weight:700}.details-link{width:max-content;max-width:100%;min-height:40px;display:inline-flex;align-items:center;font-size:12px}.details-link span{margin-left:4px;font-size:16px}@media(max-width:620px){.page>h1{font-size:22px;margin-bottom:14px}.order-card{padding:14px}.order-card-header{gap:9px}.order-total{font-size:14px}.order-meta{display:grid;gap:5px}}
  `],
})
export class OrdersPhase3Page implements OnInit {
  readonly orders = signal<CustomerOrder[]>([]);
  readonly loading = signal(true);
  storeKey = '';
  constructor(private readonly route: ActivatedRoute, private readonly data: StorefrontDataService, readonly session: CustomerSessionService) {}
  ngOnInit(): void { this.storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? this.route.parent?.parent?.snapshot.paramMap.get('storeKey') ?? ''; this.session.restore(this.storeKey); void this.load(); }
  orderStatusLabel = orderStatusLabel;
  paymentLabel = paymentLabel;
  statusClass(status: string | null | undefined): string { const value = (status ?? '').toUpperCase(); if (value.includes('FAIL') || value.includes('CANCEL')) return 'badge-danger'; if (value.includes('PENDING') || value.includes('HELD')) return 'badge-warning'; if (value === 'DELIVERED' || value === 'COMPLETED' || value === 'CONFIRMED') return ''; return 'badge-neutral'; }
  private async load(): Promise<void> { try { await this.session.refresh(this.storeKey); if (this.session.active()) this.orders.set(await this.data.getOrders(this.storeKey)); } finally { this.loading.set(false); } }
}
