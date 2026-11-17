import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Delivery, DeliveryAgent, DeliveryApiService, DeliveryDashboard, DeliverySettings } from './delivery-api.service';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule],
  template: `
    <section class="page">
      <header><div><small>Orders</small><h1>Delivery management</h1><p>Assign deliveries and advance them through the authorized workflow.</p></div><div class="header-actions"><button mat-stroked-button (click)="load()">Refresh</button></div></header>
      <div class="metrics" *ngIf="data() as x"><article *ngFor="let m of metrics(x)"><b>{{m[1]}}</b><span>{{m[0]}}</span></article></div>
      <div class="filters"><input [(ngModel)]="search" placeholder="Order or customer" aria-label="Search deliveries"><select [(ngModel)]="status" aria-label="Delivery status"><option value="">All statuses</option><option *ngFor="let s of statuses" [value]="s">{{label(s)}}</option></select><select [(ngModel)]="agent" aria-label="Delivery agent"><option value="">All agents</option><option *ngFor="let a of agents()" [value]="a.deliveryAgentId">{{a.displayName}}</option></select><button mat-flat-button color="primary" (click)="load()">Apply</button></div>
      <p class="error" *ngIf="error()" role="alert">{{error()}}</p>
      <p class="proof-note" *ngIf="settings() as s">{{s.requireDeliveryOtp ? 'OTP proof is required before delivery can be completed.' : 'OTP proof is disabled; authorized staff must confirm delivery with a reason.'}}</p>
      <div class="cards"><article *ngFor="let d of data()?.deliveries">
        <div class="top"><div><strong>Order #{{d.orderNumber}}</strong><small>{{d.sourceChannel}} · Updated {{d.updatedAt | date:'short'}}</small></div><span class="status">{{label(d.status)}}</span></div>
        <div class="details"><div><b>Customer</b><span>{{d.customerName}}</span><small>{{d.address}}</small></div><div><b>Payment</b><span>{{d.codRequired ? (d.codCollected ? 'COD collected' : 'COD pending') : 'Non-COD'}}</span><small>{{d.amount | currency:'INR'}}</small></div></div>
        <details class="collection-details"><summary>Delivery &amp; Collection Details</summary><div class="collection-grid"><div><b>Delivery</b><span>{{label(d.status)}}</span><small>Completed {{d.deliveredAt ? (d.deliveredAt | date:'medium') : 'Not completed'}}</small></div><div><b>Agent</b><span>{{d.deliveryAgentName || 'Unassigned'}}</span><small>{{d.assignmentSource || 'Manual assignment required'}}</small></div><div><b>COD due</b><span>{{d.codRequired ? (d.codAmount | currency:'INR') : 'Not applicable'}}</span><small>{{d.codCollected ? 'Collection recorded' : 'Collection pending'}}</small></div><div><b>COD collected</b><span>{{d.codCollectedAmount != null ? (d.codCollectedAmount | currency:'INR') : (d.codCollected ? 'Recorded, amount unavailable' : 'Not recorded')}}</span><small>{{d.codPaymentMethod || '—'}}{{d.codReference ? ' · '+d.codReference : ''}}</small></div><div><b>Confirmed</b><span>{{d.codCollectedAt ? (d.codCollectedAt | date:'medium') : '—'}}</span><small>{{d.codConfirmedBy || 'Authoritative actor unavailable'}}</small></div><div><b>Delivery proof</b><span>{{d.proofType || '—'}}{{d.otpVerified ? ' · OTP verified' : ''}}</span><small>{{d.notes || 'No delivery notes'}}</small></div></div></details>
        <div class="assignment"><label>Assigned delivery agent<select [ngModel]="d.deliveryAgentId || ''" (ngModelChange)="assign(d,$event)" [disabled]="busy()===d.deliveryId" aria-label="Assigned delivery agent"><option value="">Unassigned</option><option *ngFor="let a of activeAgents()" [value]="a.deliveryAgentId">{{a.displayName}}{{a.isDefault ? ' (Default)' : ''}}</option></select></label><small>{{d.assignmentSource || 'Manual assignment required'}}</small></div>
        <div class="actions" aria-label="Delivery actions">
          <button mat-stroked-button *ngIf="canPickUp(d)" [disabled]="busy()===d.deliveryId" (click)="run(d,'picked-up')">Picked up</button>
          <button mat-stroked-button *ngIf="d.status==='PICKED_UP'" [disabled]="busy()===d.deliveryId" (click)="run(d,'out-for-delivery')">Out for delivery</button>
          <button mat-stroked-button *ngIf="needsCod(d)" [disabled]="busy()===d.deliveryId" (click)="collectCod(d)">Confirm COD</button>
          <button mat-flat-button color="primary" *ngIf="d.status==='OUT_FOR_DELIVERY'" [disabled]="busy()===d.deliveryId" (click)="deliver(d)">Mark delivered</button>
          <button mat-stroked-button *ngIf="canFail(d)" [disabled]="busy()===d.deliveryId" (click)="fail(d)">Delivery failed</button>
          <button mat-stroked-button *ngIf="d.status==='DELIVERY_FAILED'" [disabled]="busy()===d.deliveryId" (click)="reschedule(d)">Reschedule</button>
          <a mat-stroked-button [href]="d.navigationUrl" target="_blank" rel="noopener">Navigate</a>
        </div>
      </article><p *ngIf="!data()?.deliveries?.length" class="empty">No matching deliveries.</p></div>
    </section>
  `,
  styles: [`
    :host{display:block}.page{padding:20px;display:grid;gap:16px;max-width:1500px}header{display:flex;justify-content:space-between;align-items:start;gap:16px}.header-actions{display:flex;align-items:center;gap:8px}header h1{margin:2px 0}header p,small{color:#667085}.header-actions a{display:inline-flex;min-height:38px;align-items:center;padding:0 12px;border:1px solid #cbd5e1;border-radius:8px;color:#146c43;background:#fff;font-weight:700;text-decoration:none}.header-actions a:hover{border-color:#146c43;background:#f3faf6}.metrics{display:grid;grid-template-columns:repeat(8,minmax(95px,1fr));gap:8px;overflow:auto}.metrics article{background:#fff;border:1px solid #dde4eb;border-radius:12px;padding:12px}.metrics b,.metrics span,.cards small{display:block}.metrics b{font-size:1.35rem}.filters{display:flex;gap:8px;flex-wrap:wrap}.filters input,.filters select,.assignment select{min-height:40px;border:1px solid #cbd5e1;border-radius:8px;padding:0 10px;background:#fff}.cards{display:grid;gap:10px}.cards article{background:#fff;border:1px solid #dde4eb;border-radius:12px;padding:16px;display:grid;gap:14px}.top,.details,.assignment{display:flex;justify-content:space-between;gap:16px}.status{background:#e8f3f0;color:#075b4a;border-radius:999px;padding:6px 10px;font-size:.78rem;font-weight:700;white-space:nowrap}.details{justify-content:flex-start}.details>div{min-width:220px}.details b,.details span{display:block}.collection-details{border-top:1px solid #edf0f2;padding-top:12px}.collection-details summary{cursor:pointer;font-weight:700;color:#146c43}.collection-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;padding-top:12px}.collection-grid>div{padding:10px;border:1px solid #edf0f2;border-radius:8px;background:#fafcfb}.collection-grid b,.collection-grid span{display:block}.assignment{align-items:end;border-top:1px solid #edf0f2;padding-top:12px}.assignment label{display:grid;gap:6px;min-width:260px;font-size:.85rem}.actions{display:flex;gap:8px;flex-wrap:wrap}.proof-note{margin:0;padding:10px 12px;border:1px solid #cfe0f5;border-radius:8px;background:#f5f9ff;color:#344054}.error{color:#b42318}.empty{text-align:center;padding:32px;color:#667085}@media(max-width:900px){.metrics{grid-template-columns:repeat(4,1fr)}.details,.assignment{flex-wrap:wrap}.details>div{min-width:45%}.collection-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:520px){.page{padding:12px}header,.top,.header-actions{flex-direction:column;align-items:stretch}.metrics{grid-template-columns:repeat(2,1fr)}.details>div,.assignment label{min-width:100%;width:100%}.collection-grid{grid-template-columns:1fr}.actions>*{flex:1 1 45%}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RetailerDeliveryDashboardComponent {
  private readonly api = inject(DeliveryApiService);
  readonly data = signal<DeliveryDashboard | null>(null);
  readonly agents = signal<DeliveryAgent[]>([]);
  readonly settings = signal<DeliverySettings | null>(null);
  readonly error = signal('');
  readonly busy = signal('');
  search = '';
  status = '';
  agent = '';
  readonly statuses = ['UNASSIGNED','ASSIGNED','READY_FOR_PICKUP','PICKED_UP','OUT_FOR_DELIVERY','DELIVERED','DELIVERY_FAILED','RESCHEDULED','CANCELLED','RETURN_REQUESTED','RETURNED'];

  constructor() { this.api.agents().subscribe({next: x => this.agents.set(x), error: e => this.error.set(e.error?.detail || 'Unable to load delivery agents.')}); this.api.settings().subscribe({next: x => this.settings.set(x)}); this.load(); }
  activeAgents() { return this.agents().filter(x => x.isActive && x.isAvailable); }
  metrics(x: DeliveryDashboard) { return [['Unassigned',x.unassigned],['Assigned',x.assigned],['Ready',x.ready],['Out',x.outForDelivery],['Delivered today',x.deliveredToday],['Failed',x.failed],['COD pending',x.codPending],['Rescheduled',x.rescheduled]] as const; }
  label(value: string) { return value.replaceAll('_',' '); }
  load() { this.error.set(''); this.api.dashboard({search:this.search,status:this.status,agentId:this.agent}).subscribe({next:x=>this.data.set(x),error:e=>this.error.set(e.error?.detail||'Unable to load deliveries.')}); }
  assign(d: Delivery, id: string) { this.api.assign(d.deliveryId,id||undefined,'Retailer manual assignment').subscribe({next:()=>this.load(),error:e=>{this.error.set(e.error?.detail||'Assignment failed.');this.load()}}); }
  canPickUp(d: Delivery) { return ['ASSIGNED','READY_FOR_PICKUP','RESCHEDULED'].includes(d.status); }
  canFail(d: Delivery) { return ['ASSIGNED','READY_FOR_PICKUP','PICKED_UP','OUT_FOR_DELIVERY','RESCHEDULED'].includes(d.status); }
  needsCod(d: Delivery) { return d.codRequired && !d.codCollected && ['READY_FOR_PICKUP','PICKED_UP','OUT_FOR_DELIVERY'].includes(d.status); }
  run(d: Delivery, action: string, body: unknown = {}) { if(this.busy()) return; this.busy.set(d.deliveryId); this.api.action(d.deliveryId,action,body).subscribe({next:()=>{this.busy.set('');this.load()},error:e=>{this.busy.set('');this.error.set(e.error?.detail||'Delivery action could not be completed.')}}); }
  collectCod(d: Delivery) { const method=prompt('Payment method: CASH, UPI, or ALREADY_PAID','CASH'); if(!method)return; const amount=method.toUpperCase()==='ALREADY_PAID'?0:Number(prompt('Amount collected',String(d.codAmount))); if(Number.isFinite(amount))this.run(d,'cod-collected',{paymentMethod:method,amount}); }
  deliver(d: Delivery) { if(this.busy()) return; const settings=this.settings(); if(settings?.requireCodConfirmation&&this.needsCod(d)){this.error.set('Confirm COD collection before marking this delivery delivered.');return;} if(settings&&!settings.requireDeliveryOtp){this.manualDeliver(d);return;} this.busy.set(d.deliveryId); this.api.action(d.deliveryId,'request-otp').subscribe({next:()=>{this.busy.set('');const otp=prompt('Enter the OTP received by the customer');if(otp)this.run(d,'confirm-delivery',{otp})},error:e=>{this.busy.set('');const detail=e.error?.detail||e.error?.message||'';if(detail.toLowerCase().includes('otp proof of delivery is disabled')){this.manualDeliver(d);return;}this.error.set(detail||'Delivery confirmation could not be started.')}}); }
  private manualDeliver(d: Delivery):void { const reason=prompt('Reason for confirming delivery without OTP');if(reason?.trim())this.run(d,'confirm-delivery-without-otp',{reason:reason.trim()}); }
  fail(d: Delivery) { const reason=prompt('Reason for delivery failure'); if(reason?.trim())this.run(d,'failed',{reason:reason.trim()}); }
  reschedule(d: Delivery) { const date=prompt('New delivery date (YYYY-MM-DD)'); if(!date)return; const reason=prompt('Reason for rescheduling'); if(reason)this.run(d,'reschedule',{newDate:new Date(date).toISOString(),timeWindow:null,reason}); }
}
