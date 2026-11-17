import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PermissionService } from '../../core/services/permission.service';

interface SettlementEvent { fromStatus?: string; toStatus: string; actor: string; reason?: string; occurredAt: string; }
interface SettlementRow {
  settlementEntryId: string; orderId: string; orderNumber: string; invoiceStatus: string;
  allocationId: string; fulfilmentGroupId: string; groupNumber: number; partnerSellerId: string;
  sellerName: string; productId: string; productName: string; allocatedQuantity: number;
  agreementId?: string; agreementVersion?: number; calculationBasis?: string;
  grossAmount: number; commissionAmount: number; sellerPayable: number; entryType: string;
  settlementStatus: string; adjustmentDirection?: string; reviewReason?: string;
  calculationSnapshotJson?: string; createdAt: string; approvedAt?: string; settledAt?: string;
  paymentReference?: string; warehouseId: string; warehouseName: string; events: SettlementEvent[];
}
interface ReviewForm { reason: string; gross: number | null; commission: number | null; payable: number | null; }

@Component({
  standalone: true,
  selector: 'app-partner-settlements',
  imports: [CommonModule, FormsModule],
  template: `
    <main class="settlements-page">
      <header class="page-head"><div><p class="eyebrow">Marketplace finance</p><h1>Partner supplier settlements</h1><p>Review supplier liabilities recorded against GuturGo customer orders.</p></div><button class="button secondary" type="button" (click)="load()" [disabled]="loading()">Refresh</button></header>
      @if(error()){<div class="alert error" role="alert">{{error()}}</div>}
      @if(success()){<div class="alert success" role="status">{{success()}}</div>}
      @if(loading()){<div class="state">Loading settlement ledger…</div>}
      @else if(rows().length===0){<div class="state">No marketplace supplier settlements are available.</div>}
      @else {
        <section class="settlement-list" aria-label="Marketplace supplier settlements">
          @for(row of rows(); track row.settlementEntryId){
            <article class="settlement-card">
              <div class="card-head"><div><div class="order-line"><strong>{{row.orderNumber}}</strong><span class="group">Delivery {{row.groupNumber}}</span></div><h2>{{row.sellerName}}</h2><p>{{row.productName}} · {{row.allocatedQuantity | number:'1.0-3'}} allocated</p></div><span class="status" [class.review]="row.settlementStatus==='REVIEW_REQUIRED'" [class.approved]="row.settlementStatus==='APPROVED'" [class.paid]="row.settlementStatus==='PAID'">{{statusLabel(row.settlementStatus)}}</span></div>
              <div class="facts"><div><span>Warehouse</span><strong>{{row.warehouseName}}</strong></div><div><span>Agreement</span><strong>{{row.agreementVersion ? 'Version '+row.agreementVersion : 'Missing terms'}}</strong></div><div><span>Basis</span><strong>{{row.calculationBasis || 'Requires review'}}</strong></div><div><span>Order</span><strong>{{row.invoiceStatus}}</strong></div></div>
              <div class="amounts"><div><span>Gross basis</span><strong>{{row.grossAmount | currency:'INR':'symbol':'1.2-2'}}</strong></div><div><span>Commission</span><strong>{{row.commissionAmount | currency:'INR':'symbol':'1.2-2'}}</strong></div><div class="payable"><span>Supplier payable</span><strong>{{row.sellerPayable | currency:'INR':'symbol':'1.2-2'}}</strong></div></div>
              @if(row.reviewReason){<div class="review-reason"><strong>Review required</strong><p>{{row.reviewReason}}</p></div>}
              @if(row.calculationSnapshotJson){<details class="inputs"><summary>Calculation inputs and attribution</summary><pre>{{pretty(row.calculationSnapshotJson)}}</pre></details>}
              @if(row.paymentReference){<p class="payment-reference">Independent payment reference: <strong>{{row.paymentReference}}</strong> · {{row.settledAt | date:'medium'}}</p>}
              @if(canReview() && (row.settlementStatus==='DRAFT' || row.settlementStatus==='REVIEW_REQUIRED')){
                <div class="review-form"><label>Review note<textarea [(ngModel)]="form(row).reason" rows="2" maxlength="1000" placeholder="Record the decision and supporting basis"></textarea></label>
                  @if(row.settlementStatus==='REVIEW_REQUIRED'){
                    <div class="amount-inputs"><label>Reviewed gross<input type="number" min="0" step="0.01" [(ngModel)]="form(row).gross"></label><label>Commission<input type="number" min="0" step="0.01" [(ngModel)]="form(row).commission"></label><label>Supplier payable<input type="number" min="0" step="0.01" [(ngModel)]="form(row).payable"></label></div>
                  }
                  <div class="actions"><button class="button primary" type="button" (click)="review(row,true)" [disabled]="saving()">Approve</button><button class="button danger" type="button" (click)="review(row,false)" [disabled]="saving()">Reject</button></div>
                </div>
              }
              @if(canReview() && row.settlementStatus==='APPROVED'){
                <div class="payment-confirm"><p>Record settlement only after payment is independently confirmed. This does not initiate a transfer.</p><label>Payment reference<input #reference maxlength="160" placeholder="Bank/reference number"></label><button class="button primary" type="button" (click)="markPaid(row,reference.value)" [disabled]="saving() || !reference.value.trim()">Record paid</button></div>
              }
              @if(row.events.length){<details class="events"><summary>Audit history ({{row.events.length}})</summary><ol>@for(event of row.events;track $index){<li><strong>{{statusLabel(event.toStatus)}}</strong><span>{{event.occurredAt | date:'medium'}} · {{event.actor}}</span>@if(event.reason){<p>{{event.reason}}</p>}</li>}</ol></details>}
            </article>
          }
        </section>
      }
    </main>`,
  styles: [`
    :host{display:block;color:var(--wb-text,#182230)}.settlements-page{max-width:1240px;margin:0 auto;padding:clamp(16px,3vw,32px)}.page-head,.card-head,.order-line,.actions{display:flex;align-items:center;justify-content:space-between;gap:16px}.page-head{margin-bottom:22px}.page-head h1{font-size:clamp(1.45rem,3vw,2rem);margin:.15rem 0}.page-head p{margin:.35rem 0;color:var(--wb-text-secondary,#667085)}.eyebrow{text-transform:uppercase;font-size:.75rem;letter-spacing:.08em;font-weight:700;color:var(--wb-primary,#2859a5)!important}.settlement-list{display:grid;gap:16px}.settlement-card{min-width:0;border:1px solid var(--wb-border,#d9e0e8);border-radius:14px;padding:clamp(14px,2vw,22px);background:var(--wb-surface,#fff);box-shadow:var(--wb-shadow-sm,0 2px 8px #1525360a)}.card-head{align-items:flex-start}.card-head h2{margin:8px 0 3px;font-size:1.1rem}.card-head p{margin:0;color:var(--wb-text-secondary,#667085)}.order-line{justify-content:flex-start;font-size:.9rem}.group{padding:3px 8px;border-radius:999px;background:#eef3fb;color:#294b78;font-size:.78rem}.status{white-space:nowrap;padding:5px 10px;border-radius:999px;background:#edf0f4;color:#344054;font-size:.78rem;font-weight:700}.status.review{background:#fff2d6;color:#805600}.status.approved{background:#e9f2ff;color:#174a88}.status.paid{background:#e5f6ed;color:#166534}.facts,.amounts{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin-top:18px}.facts>div,.amounts>div{min-width:0}.facts span,.amounts span{display:block;color:var(--wb-text-secondary,#667085);font-size:.78rem;margin-bottom:4px}.facts strong,.amounts strong{overflow-wrap:anywhere}.amounts{grid-template-columns:repeat(3,minmax(0,1fr));padding:14px;border-radius:10px;background:#f7f9fc}.payable{color:var(--wb-primary,#2859a5)}.review-reason{margin-top:14px;padding:12px;border-left:3px solid #d89920;background:#fff9ea}.review-reason p{margin:5px 0 0}.inputs,.events{margin-top:14px}.inputs summary,.events summary{cursor:pointer;font-weight:650}.inputs pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:260px;overflow:auto;padding:12px;background:#f5f7fa;border-radius:8px;font-size:.78rem}.review-form,.payment-confirm{display:grid;gap:12px;margin-top:16px;padding-top:16px;border-top:1px solid var(--wb-border,#d9e0e8)}label{display:grid;gap:6px;font-size:.85rem;font-weight:600}input,textarea{width:100%;box-sizing:border-box;border:1px solid var(--wb-border,#cbd5e1);border-radius:8px;padding:10px 11px;font:inherit;color:inherit;background:var(--wb-surface,#fff)}input:focus,textarea:focus{outline:2px solid #326bb8;outline-offset:1px}.amount-inputs{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.actions{justify-content:flex-start}.button{min-height:40px;border:1px solid transparent;border-radius:8px;padding:8px 14px;font:inherit;font-weight:650;cursor:pointer}.button:focus-visible{outline:3px solid #326bb8;outline-offset:2px}.button:disabled{opacity:.55;cursor:not-allowed}.primary{background:var(--wb-primary,#2859a5);color:#fff}.secondary{background:#fff;color:#2859a5;border-color:var(--wb-border,#cbd5e1)}.danger{background:#fff;color:#a52929;border-color:#e3aaaa}.payment-confirm p{margin:0;color:var(--wb-text-secondary,#667085)}.payment-reference{margin:14px 0 0;font-size:.88rem}.events ol{margin:10px 0;padding-left:20px}.events li{margin:10px 0}.events li span{display:block;color:var(--wb-text-secondary,#667085);font-size:.82rem}.events li p{margin:4px 0}.alert,.state{padding:14px;border-radius:10px;margin:12px 0}.alert.error{background:#fff0ef;color:#9d2424}.alert.success{background:#e9f8ef;color:#17653a}.state{text-align:center;color:var(--wb-text-secondary,#667085);background:var(--wb-surface,#fff);border:1px solid var(--wb-border,#d9e0e8)}
    @media(max-width:700px){.page-head,.card-head{align-items:flex-start;flex-direction:column}.page-head .button{align-self:stretch}.facts{grid-template-columns:repeat(2,minmax(0,1fr))}.status{align-self:flex-start}.amount-inputs{grid-template-columns:1fr}.amounts{gap:8px;padding:11px}.amounts span{font-size:.72rem}.amounts strong{font-size:.9rem}}
    @media(max-width:380px){.settlements-page{padding:12px}.amounts{grid-template-columns:1fr}.amounts>div{display:flex;justify-content:space-between;gap:8px}.amounts span{margin:0}.actions{flex-wrap:wrap}.actions .button{flex:1}}
  `],
})
export class PartnerSettlementsComponent {
  private readonly http = inject(HttpClient);
  private readonly permissions = inject(PermissionService);
  readonly rows = signal<SettlementRow[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly success = signal('');
  readonly canReview = () => this.permissions.has('payment.create');
  private readonly forms = new Map<string, ReviewForm>();

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true); this.error.set(''); this.success.set('');
    this.http.get<SettlementRow[]>('/api/marketplace/partner-settlements').subscribe({
      next: rows => { this.rows.set(rows); this.loading.set(false); },
      error: error => { this.error.set(this.message(error)); this.loading.set(false); },
    });
  }

  form(row: SettlementRow): ReviewForm {
    let value = this.forms.get(row.settlementEntryId);
    if (!value) { value = { reason: '', gross: null, commission: null, payable: null }; this.forms.set(row.settlementEntryId, value); }
    return value;
  }

  review(row: SettlementRow, approve: boolean): void {
    const form = this.form(row);
    if (!form.reason.trim()) { this.error.set('Enter a review note before submitting.'); return; }
    if (approve && row.settlementStatus === 'REVIEW_REQUIRED'
      && (form.gross == null || form.commission == null || form.payable == null)) {
      this.error.set('Enter all reviewed amounts before approving this entry.'); return;
    }
    if (!confirm(`${approve ? 'Approve' : 'Reject'} settlement for ${row.orderNumber} / ${row.productName}?`)) return;
    this.saving.set(true); this.error.set(''); this.success.set('');
    this.http.post<SettlementRow>(`/api/marketplace/partner-settlements/${row.settlementEntryId}/review`, {
      approve, reason: form.reason.trim(), grossAmount: form.gross, commissionAmount: form.commission, sellerPayable: form.payable,
    }).subscribe({
      next: () => { this.saving.set(false); this.success.set('Settlement review saved.'); this.load(); },
      error: error => { this.saving.set(false); this.error.set(this.message(error)); },
    });
  }

  markPaid(row: SettlementRow, reference: string): void {
    if (!confirm(`Record ${row.sellerPayable.toFixed(2)} as independently settled for ${row.sellerName}? No transfer will be initiated.`)) return;
    this.saving.set(true); this.error.set(''); this.success.set('');
    this.http.post<SettlementRow>(`/api/marketplace/partner-settlements/${row.settlementEntryId}/paid`, {
      paymentReference: reference.trim(), settledAt: new Date().toISOString(),
    }).subscribe({
      next: () => { this.saving.set(false); this.success.set('Independent settlement confirmation recorded.'); this.load(); },
      error: error => { this.saving.set(false); this.error.set(this.message(error)); },
    });
  }

  statusLabel(status: string): string { return status.replaceAll('_', ' ').toLowerCase().replace(/\b\w/g, x => x.toUpperCase()); }
  pretty(json?: string): string { try { return JSON.stringify(JSON.parse(json ?? '{}'), null, 2); } catch { return json ?? ''; } }
  private message(error: any): string { return error?.error?.message ?? error?.error?.title ?? error?.message ?? 'The settlement request could not be completed.'; }
}
