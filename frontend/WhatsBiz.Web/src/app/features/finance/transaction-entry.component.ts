import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { FinanceApiService } from './finance-api.service';
@Component({
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './transaction-entry.component.html',
  styles: [
    `
      mat-form-field {
        display: block;
        max-width: 480px;
      }
    `,
  ],
})
export class FinanceTransactionEntryComponent {
  type = 'receipt';
  partyType = 'CUSTOMER';
  partyId = '';
  mode = 'CASH';
  amount = 0;
  reference = '';
  narration = '';
  parties = signal<any[]>([]);
  constructor(
    private api: FinanceApiService,
    route: ActivatedRoute,
    private router: Router,
    private snack: MatSnackBar,
  ) {
    this.type = route.snapshot.data['type'];
    this.partyType = this.type === 'receipt' ? 'CUSTOMER' : 'SUPPLIER';
    this.loadParties();
  }
  loadParties() {
    (this.partyType === 'CUSTOMER' ? this.api.customers() : this.api.suppliers()).subscribe((x) =>
      this.parties.set(x),
    );
  }
  save() {
    if (!this.partyId || this.amount <= 0) {
      this.snack.open('Party and positive amount are required', 'Close', { duration: 3000 });
      return;
    }
    const body = {
      partyType: this.partyType,
      partyId: this.partyId,
      paymentMode: this.mode,
      amount: this.amount,
      entryDate: new Date().toISOString(),
      referenceNumber: this.reference,
      narration: this.narration,
    };
    (this.type === 'receipt' ? this.api.receipt(body) : this.api.payment(body)).subscribe(() => {
      this.snack.open('Financial transaction posted', 'Close', { duration: 2000 });
      this.router.navigateByUrl(
        this.type === 'receipt' ? '/finance/customer-ledger' : '/finance/supplier-ledger',
      );
    });
  }
}
