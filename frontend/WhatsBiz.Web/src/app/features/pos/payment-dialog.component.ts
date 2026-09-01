import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Payment, PaymentMethod } from './pos.models';

export interface PaymentResult { payments: Payment[]; isCreditSale: boolean; }

@Component({
  imports: [
    CurrencyPipe,
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './payment-dialog.component.html',
  styleUrl: './payment-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentDialogComponent {
  readonly payments = signal<Payment[]>([]);
  readonly paid = signal(0);
  method: string;
  amount: number;
  reference = '';
  constructor(
    @Inject(MAT_DIALOG_DATA)
    readonly data: {
      total: number;
      methods: PaymentMethod[];
      preferredMethod?: string;
      quickAmount?: number;
      hasCustomer: boolean;
    },
    private ref: MatDialogRef<PaymentDialogComponent>,
  ) {
    this.method = data.preferredMethod === 'SPLIT' ? 'CASH' : data.preferredMethod || 'CASH';
    this.amount = data.quickAmount || data.total;
  }
  icon(code: string) {
    return (
      (
        {
          CASH: 'payments',
          UPI: 'qr_code_2',
          CARD: 'credit_card',
          WALLET: 'account_balance_wallet',
          CREDIT: 'schedule',
        } as Record<string, string>
      )[code] || 'account_balance'
    );
  }
  balance() {
    return Math.max(0, this.data.total - this.paid());
  }
  add() {
    if (this.method === 'CREDIT' || this.amount <= 0 || this.amount > this.balance() || (this.requiresReference() && !this.reference.trim())) return;
    this.payments.update((x) => [
      ...x,
      {
        methodCode: this.method,
        amount: this.amount,
        referenceNumber: this.reference || undefined,
      },
    ]);
    this.recalculate();
    this.amount = this.balance();
    this.reference = '';
  }
  remove(index: number) {
    this.payments.update((x) => x.filter((_, i) => i !== index));
    this.recalculate();
  }
  complete() {
    const isCreditSale = this.method === 'CREDIT' && this.balance() > 0;
    if (isCreditSale && !this.data.hasCustomer) return;
    this.ref.close({ payments: this.payments(), isCreditSale } satisfies PaymentResult);
  }
  selectMethod(code: string) { this.method = code; this.reference = ''; this.amount = this.balance(); }
  selectedMethod() { return this.data.methods.find((x) => x.methodCode === this.method); }
  requiresReference() { return !!this.selectedMethod()?.requiresReference; }
  canComplete() { return this.paid() <= this.data.total && (this.balance() === 0 || (this.method === 'CREDIT' && this.data.hasCustomer)); }
  private recalculate() {
    this.paid.set(this.payments().reduce((a, b) => a + b.amount, 0));
  }
}
