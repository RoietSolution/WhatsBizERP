import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { OrderSummary, WhatsAppCommerceDemoApiService } from './whatsapp-commerce-demo-api.service';

@Component({
  imports: [CurrencyPipe, DatePipe, FormsModule, MatButtonModule, PageContainerComponent, PageHeaderComponent],
  template: `
    <app-page-container>
      <app-page-header eyebrow="WhatsApp Commerce" title="Delivery Management"
        description="Update fulfilment and courier tracking for customer ecommerce orders." />
      <section class="filters" aria-label="Delivery order filters">
        <label>From <input type="date" [(ngModel)]="from" /></label>
        <label>To <input type="date" [(ngModel)]="to" /></label>
        <label>Delivery status
          <select [(ngModel)]="deliveryStatus">
            <option value="">All statuses</option><option value="PENDING">Pending</option>
            <option value="DISPATCHED">Dispatched</option><option value="ON_THE_WAY">On the way</option>
            <option value="DELIVERED">Delivered</option><option value="CANCELLED">Cancelled</option>
          </select>
        </label>
        <label>Tracking / AWB <input [(ngModel)]="trackingFilter" placeholder="Search tracking number" /></label>
        <button mat-flat-button color="primary" (click)="load()">Apply filters</button>
      </section>
      <section class="layout">
        <div class="list">
          @for (order of orders(); track order.orderId) {
            <button type="button" (click)="select(order)" [class.active]="selected()?.orderId === order.orderId">
              <strong>#{{ order.orderNumber }}</strong>
              <span>{{ order.customerName || 'Customer' }} · {{ order.orderDate | date:'medium' }}</span>
              <small>{{ order.deliveryStatus.replaceAll('_', ' ') }} · {{ order.grandTotal | currency:'INR' }}</small>
            </button>
          } @empty { <p>No ecommerce orders match the selected filters.</p> }
        </div>
        @if (selected(); as order) {
          <section class="editor">
            <span class="eyebrow">Customer order</span><h2>{{ order.customerName || 'Customer name not recorded' }}</h2>
            <div class="summary">
              <span>Phone<b>{{ order.customerMobile || 'Not provided' }}</b></span><span>Order total<b>{{ order.grandTotal | currency:'INR' }}</b></span>
              <span>Fulfilment<b>{{ order.fulfillmentMethod?.replaceAll('_', ' ') || 'Not recorded' }}</b></span><span>Payment preference<b>{{ order.paymentType || 'Not recorded' }}</b></span>
              <span class="wide">Delivery / collection address<b>{{ order.deliveryAddress || 'Not recorded for this legacy order' }}</b></span>
            </div>
            <label>Delivery status<select [(ngModel)]="status"><option value="PENDING">Pending</option><option value="DISPATCHED">Dispatched</option><option value="ON_THE_WAY">On the way</option><option value="DELIVERED">Delivered</option><option value="CANCELLED">Cancelled</option></select></label>
            <label>Courier / delivery partner <input [(ngModel)]="courier" placeholder="Retailer delivery or courier name" /></label>
            <label>Tracking / AWB number <input [(ngModel)]="tracking" placeholder="Tracking number" /></label>
            @if (order.dispatchedOn || order.deliveredOn) {<div class="timestamps">@if (order.dispatchedOn) {<span>Dispatched: {{ order.dispatchedOn | date:'medium' }}</span>} @if (order.deliveredOn) {<span>Delivered: {{ order.deliveredOn | date:'medium' }}</span>}</div>}
            <button mat-flat-button color="primary" (click)="save()" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Update delivery status' }}</button>
          </section>
        }
      </section>
    </app-page-container>`,
  styles: [`
    .filters{display:flex;align-items:end;flex-wrap:wrap;gap:12px;margin:0 0 16px;padding:14px 16px;border:1px solid var(--wb-border);border-radius:14px;background:var(--wb-surface)}.filters label,.editor label{display:grid;gap:6px;color:var(--wb-text-secondary);font-size:12px;font-weight:700}.filters input,.filters select,.editor input,.editor select{height:42px;padding:0 12px;border:1px solid #cfe0d7;border-radius:9px;background:#fff;font:inherit}.filters input:focus,.filters select:focus,.editor input:focus,.editor select:focus{border-color:#008069;outline:3px solid #0080691c}.layout{display:grid;grid-template-columns:minmax(270px,.8fr) minmax(360px,1.2fr);align-items:start;gap:16px}.list,.editor{display:grid;gap:10px;padding:16px;border:1px solid var(--wb-border);border-radius:16px;background:var(--wb-surface)}.list{max-height:calc(100vh - 260px);overflow:auto}.list button{display:grid;gap:5px;padding:14px;border:1px solid #e1e9e4;border-radius:12px;background:#fff;text-align:left;cursor:pointer}.list button:hover{border-color:#7ac9a7;background:#f8fdfa}.list button.active{border-color:#008069;background:#eefaf3;box-shadow:0 0 0 2px #0080691c}.list strong{color:#18352a}.list span,.list small,.timestamps{color:var(--wb-text-secondary);font-size:.85rem}.list small{color:#008069;font-weight:700}.editor{gap:16px;padding:24px;box-shadow:0 8px 22px #17352a0a}.editor h2{margin:-8px 0 0;color:#18352a;font-size:24px}.summary{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;padding:14px;border-radius:12px;background:#f5faf7}.summary span{display:flex;flex-direction:column;gap:4px;color:#718078;font-size:11px;text-transform:uppercase;letter-spacing:.04em}.summary b{color:#1d392e;font-size:14px;text-transform:none;letter-spacing:0;overflow-wrap:anywhere}.summary .wide{grid-column:1/-1}.timestamps{display:grid;gap:4px;padding:10px 12px;border-radius:9px;background:#f5faf7}.editor button{min-height:46px;border-radius:10px;font-weight:700}@media(max-width:700px){.filters{align-items:stretch;flex-direction:column}.layout{grid-template-columns:1fr}.summary{grid-template-columns:1fr}.summary .wide{grid-column:auto}.list{max-height:none}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WhatsAppDeliveryManagementComponent {
  readonly orders = signal<OrderSummary[]>([]);
  readonly selected = signal<OrderSummary | null>(null);
  readonly saving = signal(false);
  status = 'PENDING'; courier = ''; tracking = ''; from = ''; to = '';
  deliveryStatus = ''; trackingFilter = '';
  constructor(private readonly api: WhatsAppCommerceDemoApiService) { this.load(); }
  load() { this.selected.set(null); this.api.deliveryOrders(this.from || undefined, this.to || undefined, this.deliveryStatus || undefined, this.trackingFilter || undefined).subscribe(rows => this.orders.set(rows)); }
  select(order: OrderSummary) { this.selected.set(order); this.status = order.deliveryStatus; this.courier = order.courierName ?? ''; this.tracking = order.trackingNumber ?? ''; }
  save() { const order = this.selected(); if (!order) return; this.saving.set(true); this.api.updateDelivery(order.orderId, this.status, this.courier, this.tracking).subscribe({ next: updated => { this.orders.update(rows => rows.map(row => row.orderId === updated.orderId ? updated : row)); this.selected.set(updated); this.saving.set(false); }, error: () => this.saving.set(false) }); }
}
