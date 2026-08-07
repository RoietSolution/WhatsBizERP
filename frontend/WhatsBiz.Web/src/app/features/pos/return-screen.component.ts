import { Component, signal } from '@angular/core';
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
    this.api.get(this.invoiceId).subscribe((x) => {
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
