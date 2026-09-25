import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, Inject, Injectable } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_SNACK_BAR_DATA,
  MatSnackBar,
  MatSnackBarRef,
} from '@angular/material/snack-bar';
import { map } from 'rxjs';
import { PaperSize } from '../printing/paper-size';
import { RuntimeConfigurationService } from '../../core/services/runtime-configuration.service';
import {
  Invoice,
  PagedInvoices,
  PaymentMethod,
  POSBrand,
  POSCategory,
  POSCustomer,
  POSProduct,
  TodaySales,
} from './pos.models';

export interface POSWarehouse {
  warehouseId: string;
  warehouseName: string;
  isDefault: boolean;
}

export interface POSUpiQr {
  qrCodeDataUrl: string;
  upiId: string;
  payeeName: string;
  amount: number;
}

interface PrintBridgeFallbackData {
  retry: () => void;
  browserPrint: () => void;
}

@Component({
  selector: 'app-print-bridge-fallback',
  imports: [MatButtonModule],
  template: `
    <div class='message'>If KhataDhari Print Bridge did not open, choose an option.</div>
    <div class='actions'>
      <button mat-button type='button' (click)='retry()'>Try Print Bridge Again</button>
      <button mat-button type='button' (click)='browserPrint()'>Use Browser Print</button>
    </div>
  `,
  styles: `
    :host { display: block; }
    .message { margin-bottom: 4px; }
    .actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 4px; }
  `,
})
export class PrintBridgeFallbackComponent {
  constructor(
    @Inject(MAT_SNACK_BAR_DATA) private readonly data: PrintBridgeFallbackData,
    private readonly ref: MatSnackBarRef<PrintBridgeFallbackComponent>,
  ) {}

  retry() {
    this.ref.dismiss();
    this.data.retry();
  }

  browserPrint() {
    this.ref.dismiss();
    this.data.browserPrint();
  }
}

@Injectable({ providedIn: 'root' })
export class POSApiService {
  private printBridgeRequests = new Set<string>();
  private printFallbackNotice?: MatSnackBarRef<PrintBridgeFallbackComponent>;
  constructor(
    private readonly http: HttpClient,
    private readonly runtime: RuntimeConfigurationService,
    private readonly snack: MatSnackBar,
  ) {}
  products(search?: string, barcode?: string, warehouseId?: string, size?: number, categoryId?: string, brandId?: string) {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (barcode) params = params.set('barcode', barcode);
    if (warehouseId) params = params.set('warehouseId', warehouseId);
    if (size) params = params.set('size', size);
    if (categoryId) params = params.set('categoryId', categoryId);
    if (brandId) params = params.set('brandId', brandId);
    return this.http.get<POSProduct[]>('/api/pos/products', { params });
  }
  categories() {
    return this.http.get<POSCategory[]>('/api/pos/categories');
  }
  brands() {
    return this.http.get<POSBrand[]>('/api/pos/brands');
  }
  productImage(productId: string) {
    return this.http.get(`/api/pos/products/${productId}/image`, { responseType: 'blob' });
  }
  customers(search?: string) {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    return this.http.get<POSCustomer[]>('/api/pos/customers', { params });
  }
  quickCustomer(value: object) {
    return this.http.post<POSCustomer>('/api/pos/customers/quick', value);
  }
  invoice(value: object) {
    return this.http.post<{ invoiceId: string; invoiceNumber: string }>('/api/pos/invoice', value);
  }
  hold(value: object) {
    return this.http.post<{ invoiceId: string; invoiceNumber: string }>('/api/pos/hold', value);
  }
  resume(id: string) {
    return this.http.post<Invoice>('/api/pos/resume', JSON.stringify(id), {
      headers: { 'Content-Type': 'application/json' },
    });
  }
  completeHeld(id:string){return this.http.post<void>(`/api/pos/invoice/${id}/complete-held`,null);}
  cancelHeld(id:string){return this.http.post<void>(`/api/pos/invoice/${id}/cancel-held`,null);}
  get(id: string) {
    return this.http.get<Invoice>(`/api/pos/invoice/${id}`);
  }
  invoices(status?: string, pageNumber = 1, search?: string) {
    let params = new HttpParams().set('pageNumber', pageNumber);
    if (status) params = params.set('status', status);
    if (search) params = params.set('search', search);
    return this.http.get<PagedInvoices>('/api/pos/invoices', { params });
  }
  payment(value: object) {
    return this.http.post('/api/pos/payment', value);
  }
  return(value: object) {
    return this.http.post('/api/pos/return', value);
  }
  methods() {
    return this.http.get<PaymentMethod[]>('/api/pos/payment-methods');
  }
  upiQr(amount: number) {
    return this.http.get<POSUpiQr>('/api/pos/upi-qr', {
      params: new HttpParams().set('amount', amount.toFixed(2)),
    });
  }
  today() {
    return this.http.get<TodaySales>('/api/pos/today-sales');
  }
  print(id: string, paper?: PaperSize) {
    let params = new HttpParams();
    if (paper) params = params.set('paper', paper);
    this.http
      .get(`/api/pos/invoice/${id}/print`, { params, responseType: 'blob' })
      .subscribe((receipt) => {
        const url = URL.createObjectURL(receipt);
        const popup = window.open(url, '_blank');
        if (popup) popup.addEventListener('load', () => URL.revokeObjectURL(url), { once: true });
      });
  }
  printBridge(id: string) {
    if (!/Android/i.test(navigator.userAgent)) {
      this.print(id);
      return;
    }
    if (this.printBridgeRequests.has(id)) return;
    this.printBridgeRequests.add(id);
    this.http.post<{ reference: string; expiresAtUtc: string }>(`/api/pos/invoice/${id}/print-bridge-reference`, {}).subscribe({
      next: ({ reference }) => {
        this.printBridgeRequests.delete(id);
        if (!reference) {
          this.showPrintBridgeFallback(id);
          return;
        }
        const intent = new URL('khatadhari-print://receipt');
        intent.searchParams.set('api', this.runtime.apiBaseUrl() || window.location.origin);
        intent.searchParams.set('reference', reference);
        // Chrome cannot reliably report whether a custom-scheme navigation opened an app.
        // Keep fallback as an explicit user choice; never infer failure and call browser print.
        this.showPrintBridgeFallback(id);
        window.location.assign(intent.toString());
      },
      error: () => {
        this.printBridgeRequests.delete(id);
        this.showPrintBridgeFallback(id);
      },
    });
  }
  private showPrintBridgeFallback(id: string) {
    this.printFallbackNotice?.dismiss();
    this.printFallbackNotice = this.snack.openFromComponent(PrintBridgeFallbackComponent, {
      duration: 0,
      data: {
        retry: () => this.printBridge(id),
        browserPrint: () => this.print(id),
      } satisfies PrintBridgeFallbackData,
    });
  }
  export() {
    return this.http.get('/api/pos/export', { responseType: 'blob' });
  }
  warehouses() {
    return this.http
      .get<{ items: POSWarehouse[] }>('/api/pos/warehouses', {
        params: { isActive: true, pageSize: 100, sortBy: 'warehouseName' },
      })
      .pipe(map((result) => result.items));
  }
}
