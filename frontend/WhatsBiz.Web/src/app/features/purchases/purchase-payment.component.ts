import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PurchaseApiService } from './purchase-api.service';
import { Purchase } from './purchase.models';
@Component({
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './purchase-payment.component.html',
  styles: [
    `
      mat-form-field {
        display: block;
        max-width: 420px;
      }
    `,
  ],
})
export class PurchasePaymentComponent {
  p = signal<Purchase | null>(null);
  method = 'CASH';
  amount = 0;
  reference = '';
  constructor(
    private api: PurchaseApiService,
    route: ActivatedRoute,
    private router: Router,
    private snack: MatSnackBar,
  ) {
    api.get(route.snapshot.paramMap.get('id')!).subscribe((x) => {
      this.p.set(x);
      this.amount = x.balanceAmount;
    });
  }
  pay() {
    const x = this.p();
    if (x)
      this.api
        .pay({
          purchaseInvoiceId: x.purchaseInvoiceId,
          methodCode: this.method,
          amount: this.amount,
          referenceNumber: this.reference,
        })
        .subscribe({next: () => this.router.navigate(['/purchases', x.purchaseInvoiceId]), error: e => this.snack.open(e?.error?.detail || 'Unable to record payment.', 'Close', {duration:5000})});
  }
}
