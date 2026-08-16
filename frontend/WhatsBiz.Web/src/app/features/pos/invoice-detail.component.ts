import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { POSApiService } from './pos-api.service';
import { Invoice } from './pos.models';

@Component({
  selector: 'app-invoice-detail',
  imports: [CurrencyPipe, DatePipe, MatButtonModule, RouterLink],
  template: `
    <main class="invoice-detail">
      <a mat-button routerLink="/pos/history"><span class="material-symbols-rounded">arrow_back</span>Invoice history</a>
      @if (invoice(); as item) {
        <header><div><small>Sales invoice</small><h1>{{ item.invoiceNumber }}</h1><p>{{ item.invoiceDate | date: 'medium' }} · {{ item.status }}</p></div><button mat-flat-button color="primary" (click)="print()"><span class="material-symbols-rounded">print</span>Print invoice</button></header>
        <section class="invoice-card"><div><strong>Customer</strong><span>{{ item.customerName || 'Walk-in customer' }}</span></div><div><strong>Total</strong><span>{{ item.grandTotal | currency: 'INR' }}</span></div><div><strong>Paid</strong><span>{{ item.paidAmount | currency: 'INR' }}</span></div><div><strong>Balance</strong><span>{{ item.balanceAmount | currency: 'INR' }}</span></div></section>
        <section class="items"><h2>Items</h2>@for (line of item.items; track line.invoiceItemId) {<div><span><strong>{{ line.productName }}</strong><small>{{ line.productCode }} · {{ line.quantity }} × {{ line.unitPrice | currency: 'INR' }}</small></span><b>{{ line.lineTotal | currency: 'INR' }}</b></div>}</section>
      } @else if (loading()) { <p>Loading invoice details…</p> } @else { <p>Invoice details could not be loaded.</p> }
    </main>
  `,
  styles: [`:host{display:block}.invoice-detail{max-width:960px;margin:24px auto;padding:0 20px}.invoice-detail header{display:flex;justify-content:space-between;align-items:flex-start;gap:16px;margin:20px 0}.invoice-detail h1{margin:4px 0;font-size:1.8rem}.invoice-detail p,.invoice-detail small{color:var(--wb-text-secondary)}.invoice-card,.items{padding:20px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:14px}.invoice-card{display:grid;grid-template-columns:repeat(4,1fr);gap:18px}.invoice-card strong,.invoice-card span,.items span,.items small{display:block}.invoice-card span{margin-top:5px;font-size:1.05rem;font-weight:700}.items{margin-top:18px}.items h2{margin-top:0}.items>div{display:flex;justify-content:space-between;gap:16px;padding:14px 0;border-top:1px solid var(--wb-border)}@media(max-width:650px){.invoice-detail header{flex-direction:column}.invoice-card{grid-template-columns:1fr 1fr}}`],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InvoiceDetailComponent {
  readonly invoice = signal<Invoice | null>(null);
  readonly loading = signal(true);
  private readonly api = inject(POSApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  constructor() { this.api.get(this.id).subscribe({ next: (item) => { this.invoice.set(item); this.loading.set(false); }, error: () => this.loading.set(false) }); }
  print(): void { if (this.invoice()) this.api.print(this.invoice()!.invoiceId); }
}
