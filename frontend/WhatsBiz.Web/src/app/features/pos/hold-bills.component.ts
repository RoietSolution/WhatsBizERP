import { Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router } from '@angular/router';
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
    `,
  ],
})
export class HoldBillsComponent {
  readonly bills = signal<InvoiceList[]>([]);
  constructor(
    private readonly api: POSApiService,
    private readonly router: Router,
  ) {
    api.invoices('HELD').subscribe((x) => this.bills.set(x.items));
  }
  resume(x: InvoiceList) {
    this.api
      .resume(x.invoiceId)
      .subscribe(
        () => void this.router.navigate(['/pos'], { queryParams: { resume: x.invoiceId } }),
      );
  }
}
