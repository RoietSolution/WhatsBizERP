import { Component, signal } from '@angular/core';
import { catchError, of, switchMap } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { POSApiService } from './pos-api.service';
import { Invoice } from './pos.models';
@Component({
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './return-screen.component.html',
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
export class ReturnScreenComponent {
  readonly invoice = signal<Invoice | null>(null);
  invoiceId = '';
  reason = 'Customer return';
  quantities: Record<string, number> = {};
  constructor(
    private readonly api: POSApiService,
    private readonly snack: MatSnackBar,
  ) {}
  load() {
    const value = this.invoiceId.trim();
    if (!value) {
      this.snack.open('Enter an invoice number or invoice ID.', undefined, { duration: 5000 });
      return;
    }

    const isId = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
      value,
    );
    const request = isId
      ? this.api.get(value)
      : this.api.invoices(undefined, 1, value).pipe(
          switchMap((result) => {
            const match = result.items[0];
            return match ? this.api.get(match.invoiceId) : of(null);
          }),
        );

    request.pipe(catchError(() => of(null))).subscribe((x) => {
      if (!x) {
        this.invoice.set(null);
        this.snack.open('Invoice not found. Check the invoice number or ID.', undefined, {
          duration: 6000,
        });
        return;
      }
      this.invoiceId = x.invoiceId;
      this.invoice.set(x);
      this.quantities = {};
    });
  }
  submit() {
    const items = Object.entries(this.quantities)
      .filter(([, q]) => q > 0)
      .map(([invoiceItemId, quantity]) => ({ invoiceItemId, quantity }));
    this.api.return({ invoiceId: this.invoiceId, items, reason: this.reason }).subscribe(() => {
      this.snack.open('Return completed.', undefined, { duration: 3000 });
      this.load();
    });
  }
}
