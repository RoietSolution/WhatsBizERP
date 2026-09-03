import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, Inject, OnDestroy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { POSApiService } from './pos-api.service';
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
export class PaymentDialogComponent implements OnDestroy {
  readonly payments = signal<Payment[]>([]);
  readonly paid = signal(0);
  readonly upiQrUrl = signal('');
  readonly upiQrLoading = signal(false);
  readonly upiQrError = signal('');
  readonly visibleMethods: PaymentMethod[];
  readonly splitPayment: boolean;
  method: string;
  amount: number;
  reference = '';
  private qrRefreshTimer?: ReturnType<typeof setTimeout>;
  private qrRequest = 0;
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
    private api: POSApiService,
  ) {
    this.visibleMethods = data.methods.filter((method) => ['CASH', 'UPI'].includes(method.methodCode));
    this.splitPayment = data.preferredMethod === 'SPLIT';
    const requestedMethod = data.preferredMethod === 'UPI' ? 'UPI' : 'CASH';
    this.method = this.visibleMethods.some((method) => method.methodCode === requestedMethod)
      ? requestedMethod
      : this.visibleMethods[0]?.methodCode || 'CASH';
    this.amount = data.quickAmount || data.total;
    if (this.method === 'UPI') this.scheduleUpiQrRefresh();
  }
  icon(code: string) {
    return (
      (
        {
          CASH: 'payments',
          UPI: 'qr_code_2',
        } as Record<string, string>
      )[code] || 'account_balance'
    );
  }
  balance() {
    return Math.max(0, this.data.total - this.paid());
  }
  add() {
    if (this.amount <= 0 || this.amount > this.balance() || (this.requiresReference() && !this.reference.trim())) return;
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
    this.scheduleUpiQrRefresh();
  }
  remove(index: number) {
    this.payments.update((x) => x.filter((_, i) => i !== index));
    this.recalculate();
    this.amount = this.balance();
    this.scheduleUpiQrRefresh();
  }
  complete() {
    this.ref.close({ payments: this.payments(), isCreditSale: false } satisfies PaymentResult);
  }
  selectMethod(code: string) {
    if (!this.visibleMethods.some((method) => method.methodCode === code)) return;
    this.method = code;
    this.reference = '';
    this.amount = this.balance();
    this.scheduleUpiQrRefresh();
  }
  paymentAmountChanged() { this.scheduleUpiQrRefresh(); }
  selectedMethod() { return this.visibleMethods.find((x) => x.methodCode === this.method); }
  requiresReference() { return !!this.selectedMethod()?.requiresReference; }
  canComplete() { return this.paid() <= this.data.total && this.balance() === 0; }
  ngOnDestroy() {
    if (this.qrRefreshTimer) clearTimeout(this.qrRefreshTimer);
    this.qrRequest++;
  }
  private recalculate() {
    this.paid.set(this.payments().reduce((a, b) => a + b.amount, 0));
  }
  private scheduleUpiQrRefresh() {
    if (this.qrRefreshTimer) clearTimeout(this.qrRefreshTimer);
    if (this.method !== 'UPI' || this.amount <= 0) {
      this.qrRequest++;
      this.upiQrUrl.set('');
      this.upiQrError.set('');
      this.upiQrLoading.set(false);
      return;
    }
    this.qrRefreshTimer = setTimeout(() => this.loadUpiQr(), 200);
  }
  private loadUpiQr() {
    const amount = Number(this.amount);
    if (this.method !== 'UPI' || !Number.isFinite(amount) || amount <= 0) return;
    const request = ++this.qrRequest;
    this.upiQrLoading.set(true);
    this.upiQrError.set('');
    this.api.upiQr(amount).subscribe({
      next: (result) => {
        if (request !== this.qrRequest) return;
        this.upiQrUrl.set(result.qrCodeDataUrl);
        this.upiQrLoading.set(false);
      },
      error: () => {
        if (request !== this.qrRequest) return;
        this.upiQrUrl.set('');
        this.upiQrError.set('UPI QR is not configured. Ask an administrator to set the POS UPI ID in Application Settings.');
        this.upiQrLoading.set(false);
      },
    });
  }
}
