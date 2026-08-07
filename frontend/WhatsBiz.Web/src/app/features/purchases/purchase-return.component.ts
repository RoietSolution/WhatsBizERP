import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PurchaseApiService } from './purchase-api.service';
import { Purchase } from './purchase.models';
@Component({
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './purchase-return.component.html',
  styles: [
    `
      table {
        width: 100%;
        margin-bottom: 1rem;
      }
      th,
      td {
        text-align: left;
        padding: 0.6rem;
      }
      mat-form-field {
        width: 100%;
      }
    `,
  ],
})
export class PurchaseReturnComponent {
  p = signal<Purchase | null>(null);
  qty: Record<string, number> = {};
  reason = '';
  constructor(
    private api: PurchaseApiService,
    route: ActivatedRoute,
    private router: Router,
    private snack: MatSnackBar,
  ) {
    api.get(route.snapshot.paramMap.get('id')!).subscribe((x) => this.p.set(x));
  }
  submit() {
    const x = this.p();
    if (!x) return;
    const items = x.items
      .filter((i) => this.qty[i.purchaseItemId] > 0)
      .map((i) => ({ purchaseItemId: i.purchaseItemId, quantity: this.qty[i.purchaseItemId] }));
    if (!items.length || !this.reason) {
      this.snack.open('Return quantity and reason are required', 'Close', { duration: 3000 });
      return;
    }
    this.api
      .return({ purchaseInvoiceId: x.purchaseInvoiceId, items, reason: this.reason })
      .subscribe(() => this.router.navigate(['/purchases', x.purchaseInvoiceId]));
  }
}
