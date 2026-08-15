import { Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { POSApiService } from './pos-api.service';
import { InvoiceList } from './pos.models';
@Component({
  imports: [MatButtonModule],
  templateUrl: './hold-bills.component.html',
  styles: [
    `
      section {
        display: flex;
        justify-content: space-between;
        padding: 1rem;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      .source-badge { margin-left:.5rem;padding:.2rem .45rem;border-radius:999px;background:#dcfce7;color:#166534;font-size:.75rem; }
      section>span:last-child { display:flex;gap:.4rem; }
    `,
  ],
})
export class HoldBillsComponent {
  readonly bills = signal<InvoiceList[]>([]);
  constructor(
    private readonly api: POSApiService,
  ) {
    this.load();
  }
  load(){this.api.invoices('HELD').subscribe((x) => this.bills.set(x.items));}
  complete(x:InvoiceList){this.api.completeHeld(x.invoiceId).subscribe(()=>this.load());}
  cancel(x:InvoiceList){this.api.cancelHeld(x.invoiceId).subscribe(()=>this.load());}
}
