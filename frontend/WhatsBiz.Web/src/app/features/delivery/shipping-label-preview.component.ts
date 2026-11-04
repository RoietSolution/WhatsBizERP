import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ShippingLabelFormat, StorefrontShippingLabel, shippingLabelPaymentIndicator } from './shipping-label.models';

@Component({
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, FormsModule],
  selector: 'app-shipping-label-preview',
  template: `
    @if (open) {
      <div class="label-overlay" role="dialog" aria-modal="true" aria-label="Shipping label preview">
        <div class="label-dialog">
          <header><div><small>STOREFRONT SHIPPING LABEL</small><h2>Print preview</h2></div><button type="button" (click)="close.emit()">Close</button></header>
          <div class="label-settings">
            <label>Print format<select [(ngModel)]="format"><option value="A5">A5 label</option><option value="A4_SINGLE">Single A5 on A4</option><option value="A4_DOUBLE">Two A5 labels on A4</option><option value="THERMAL_80">80 mm thermal</option><option value="THERMAL_58">58 mm thermal</option></select></label>
            @if (format==='A4_DOUBLE' && orders.length===1) { <p class="notice">One order will print on the first A4 sheet; an odd final order is printed alone without duplication.</p> }
            @if ((format==='THERMAL_58'||format==='THERMAL_80') && !thermalReady()) { <p class="notice">This format uses the browser printer dialog and the configured printer's printable width. Select a matching 58 mm or 80 mm driver.</p> }
          </div>
          <p class="preview-summary">{{orders.length}} selected order(s) · {{pageCount()}} page(s)/label sheet(s) in {{format}} format.</p>
          <div class="preview-scroll"><div class="shipping-print-root" [class]="formatClass()">@for (order of printableOrders(); track order.id) { <article class="shipping-label"><div class="label-head"><div><strong>{{order.storeName || 'Store'}}</strong><small>{{order.senderAddress || 'Sender address not configured'}}</small>@if(order.senderPhone||order.senderEmail){<small>{{order.senderPhone||''}} {{order.senderEmail||''}}</small>}</div><span>SHIP TO</span></div><div class="label-order"><strong>{{order.orderNumber}}</strong><span>{{order.orderDate|date:'mediumDate'}}</span></div><div class="recipient"><strong>{{order.customerName}}</strong>@if(order.customerMobile){<span>{{order.customerMobile}}</span>}<p>{{order.deliveryAddress}}</p></div><div class="pin"><small>DELIVERY PIN</small><strong>{{order.deliveryPincode || 'PIN NOT PROVIDED'}}</strong></div><div class="label-payment"><strong>{{paymentIndicator(order)}}</strong>@if(order.paymentDue){<span>Collect {{order.dueAmount|currency:'INR':'symbol':'1.0-2'}}</span>}@else{<span>{{order.paymentMethod || 'Payment'}} · {{order.paymentStatus || 'PAID'}}</span>}</div></article> }</div></div>
          <footer><span>Review the address before printing. Printing does not change order or payment status.</span><button type="button" class="primary" [disabled]="!canPrint()" (click)="print()">Print labels</button></footer>
        </div>
      </div>
    }
  `,
  styles: [`
    :host{display:block}.label-overlay{position:fixed;inset:0;z-index:1000;display:grid;place-items:center;padding:18px;background:rgba(15,23,42,.55)}.label-dialog{width:min(1000px,100%);max-height:calc(100vh - 36px);display:grid;grid-template-rows:auto auto 1fr auto;overflow:hidden;border-radius:14px;background:#fff;box-shadow:0 20px 60px rgba(0,0,0,.25);color:#172026}.label-dialog header,.label-dialog footer{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:14px 18px;border-bottom:1px solid #dce3df}.label-dialog footer{border-top:1px solid #dce3df;border-bottom:0;font-size:11px;color:#667085}.label-dialog h2{margin:3px 0 0}.label-dialog small{display:block;color:#667085}.label-dialog button{min-height:36px;border:1px solid #cbd5e1;border-radius:8px;padding:0 12px;background:#fff;cursor:pointer}.label-dialog button.primary{color:#fff;background:#146c43;border-color:#146c43}.label-settings{display:flex;align-items:end;gap:16px;padding:12px 18px;border-bottom:1px solid #dce3df}.label-settings label{display:grid;gap:5px;font-size:11px;font-weight:700}.label-settings select{min-height:36px;border:1px solid #cbd5e1;border-radius:8px;padding:0 9px}.notice{margin:0;color:#9a3412;font-size:11px}.preview-scroll{overflow:auto;padding:24px;background:#f1f5f3}.shipping-print-root{display:grid;gap:18px;justify-content:center}.shipping-label{box-sizing:border-box;display:flex;flex-direction:column;gap:9px;overflow-wrap:anywhere;border:1px solid #111;background:#fff;color:#111;padding:12mm;width:148mm;min-height:210mm;font-family:Arial,sans-serif}.label-head{display:flex;justify-content:space-between;gap:12px;border-bottom:2px solid #111;padding-bottom:5mm}.label-head strong{font-size:18px}.label-head span{font-size:12px;font-weight:700}.label-order{display:flex;justify-content:space-between;border-bottom:1px solid #999;padding-bottom:3mm}.recipient{display:grid;gap:3px}.recipient strong{font-size:17px}.recipient p{white-space:pre-line;margin:6px 0;line-height:1.45}.pin{display:grid;gap:2px;border:2px solid #111;padding:5mm;text-align:center}.pin small{font-size:10px;letter-spacing:1px}.pin strong{font-size:30px;letter-spacing:3px}.label-payment{display:flex;justify-content:space-between;gap:8px;margin-top:auto;border-top:2px solid #111;padding-top:4mm;font-size:13px}.shipping-print-root.a4-single .shipping-label{justify-self:center}.shipping-print-root.a4-double{display:grid;grid-template-columns:1fr 1fr;gap:0;width:296mm;background:#fff}.shipping-print-root.a4-double .shipping-label{width:148mm;min-height:210mm;border:1px solid #777;padding:8mm}.shipping-print-root.thermal-80 .shipping-label{width:72mm;min-height:0;padding:4mm;font-size:11px;border:0}.shipping-print-root.thermal-58 .shipping-label{width:50mm;min-height:0;padding:3mm;font-size:9px;border:0}.thermal-80 .label-head strong,.thermal-58 .label-head strong{font-size:14px}.thermal-80 .label-head,.thermal-58 .label-head{padding-bottom:2mm}.thermal-80 .recipient strong,.thermal-58 .recipient strong{font-size:13px}.thermal-80 .pin,.thermal-58 .pin{padding:2mm}.thermal-80 .pin strong{font-size:24px}.thermal-58 .pin strong{font-size:20px}.thermal-80 .label-payment,.thermal-58 .label-payment{font-size:10px;padding-top:2mm}.thermal-58 .label-payment{display:grid}.shipping-print-root{color-adjust:exact;-webkit-print-color-adjust:exact}@media print{ @page{margin:0}.no-print{display:none!important}body>*:not(app-root){display:none!important}app-root>*:not(app-shipping-label-preview){display:none!important}.shipping-print-root{display:grid!important;background:#fff!important;gap:0}.shipping-label{break-inside:avoid;page-break-inside:avoid}.shipping-print-root.a5{width:148mm}.shipping-print-root.a4-single{width:210mm;min-height:297mm;align-content:start}.shipping-print-root.a4-double{width:296mm;grid-template-columns:148mm 148mm}.shipping-print-root.thermal-80{width:72mm}.shipping-print-root.thermal-58{width:50mm}.preview-scroll{padding:0;background:#fff;overflow:visible}}
  `],
  styleUrl: './shipping-label-preview.component.css'
})
export class ShippingLabelPreviewComponent {
  @Input() open = false;
  @Input() orders: StorefrontShippingLabel[] = [];
  @Output() readonly close = new EventEmitter<void>();
  format: ShippingLabelFormat = (localStorage.getItem('whatsbiz.storefront.shipping-label-format') as ShippingLabelFormat) || 'A5';
  printableOrders(): StorefrontShippingLabel[] { return this.orders; }
  formatClass(): string { return this.format.toLowerCase().replace('_', '-'); }
  thermalReady(): boolean { return this.format !== 'THERMAL_58' && this.format !== 'THERMAL_80'; }
  canPrint(): boolean { return this.orders.length > 0; }
  pageCount(): number { return this.format === 'A4_DOUBLE' ? Math.ceil(this.orders.length / 2) : this.orders.length; }
  paymentIndicator(order: StorefrontShippingLabel): string { return shippingLabelPaymentIndicator(order); }
  print(): void { if (!this.canPrint()) return; localStorage.setItem('whatsbiz.storefront.shipping-label-format', this.format); window.print(); }
}
