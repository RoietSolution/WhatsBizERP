import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { Outstanding, ReceivablesApiService } from './receivables-api.service';
@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageContainerComponent,
    PageHeaderComponent,
    StatusChipComponent,
  ],
  templateUrl: './receivable-payable-entry.component.html',
  styleUrl: './receivable-payable-entry.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceivablePayableEntryComponent {
  kind = 'receipt';
  partyId = '';
  entryType = 'INVOICE_WISE';
  reference = '';
  remarks = '';
  readonly parties = signal<any[]>([]);
  readonly invoices = signal<Outstanding[]>([]);
  readonly saving = signal(false);
  readonly modes = [
    { code: 'CASH', name: 'Cash' },
    { code: 'UPI', name: 'UPI' },
    { code: 'CCARD', name: 'Credit Card' },
    { code: 'DCARD', name: 'Debit Card' },
    { code: 'BANK', name: 'Bank Transfer' },
    { code: 'CHEQUE', name: 'Cheque' },
    { code: 'WALLET', name: 'Wallet' },
  ];
  splits = [{ paymentMode: 'CASH', amount: 0, referenceNumber: '' }];
  constructor(
    private api: ReceivablesApiService,
    route: ActivatedRoute,
    private snack: MatSnackBar,
  ) {
    this.kind = route.snapshot.data['kind'];
    (this.kind === 'receipt' ? api.customers() : api.suppliers()).subscribe((x) =>
      this.parties.set(x),
    );
  }
  id(x: any) {
    return this.kind === 'receipt' ? x.customerId : x.supplierId;
  }
  code(x: any) {
    return this.kind === 'receipt' ? x.customerCode : x.supplierCode;
  }
  name(x: any) {
    return this.kind === 'receipt' ? x.customerName : x.supplierName;
  }
  loadOutstanding() {
    this.api
      .outstanding(this.kind === 'receipt' ? 'customer' : 'supplier', this.partyId)
      .subscribe((x) => this.invoices.set(x.map((i) => ({ ...i, allocate: 0 }))));
  }
  addSplit() {
    this.splits.push({ paymentMode: 'UPI', amount: 0, referenceNumber: '' });
  }
  removeSplit(i: number) {
    if (this.splits.length > 1) this.splits.splice(i, 1);
  }
  quick(value: number) {
    this.splits[0].amount = value;
  }
  total() {
    return this.splits.reduce((n, x) => n + (+x.amount || 0), 0);
  }
  allocated() {
    return this.invoices().reduce((n, x) => n + (+x.allocate! || 0), 0);
  }
  outstanding() {
    return this.invoices().reduce((n, x) => n + x.outstandingAmount, 0);
  }
  save() {
    if (!this.partyId || this.total() <= 0 || this.allocated() > this.total()) {
      this.snack.open('Select a party and enter valid totals.', 'Close', { duration: 3000 });
      return;
    }
    const allocations = this.invoices()
      .filter((x) => +x.allocate! > 0)
      .map((x) =>
        this.kind === 'receipt'
          ? { invoiceId: x.invoiceId, amount: +x.allocate! }
          : { purchaseInvoiceId: x.invoiceId, amount: +x.allocate! },
      );
    const common = {
      referenceNumber: this.reference,
      remarks: this.remarks,
      items: this.splits.map((x) => ({ ...x, amount: +x.amount })),
      allocations,
    };
    const body =
      this.kind === 'receipt'
        ? {
            ...common,
            customerId: this.partyId,
            receiptType: this.entryType,
            receiptDate: new Date().toISOString(),
          }
        : {
            ...common,
            supplierId: this.partyId,
            paymentType: this.entryType,
            paymentDate: new Date().toISOString(),
          };
    this.saving.set(true);
    (this.kind === 'receipt' ? this.api.receipt(body) : this.api.payment(body)).subscribe({
      next: (x) => {
        this.saving.set(false);
        this.snack.open(`${x.documentNumber} posted.`, undefined, { duration: 3000 });
        this.loadOutstanding();
      },
      error: () => this.saving.set(false),
    });
  }
}
