import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map } from 'rxjs';
import {
  Invoice,
  PagedInvoices,
  PaymentMethod,
  POSCustomer,
  POSProduct,
  TodaySales,
} from './pos.models';

export interface POSWarehouse {
  warehouseId: string;
  warehouseName: string;
  isDefault: boolean;
}

@Injectable({ providedIn: 'root' })
export class POSApiService {
  constructor(private readonly http: HttpClient) {}
  products(search?: string, barcode?: string) {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (barcode) params = params.set('barcode', barcode);
    return this.http.get<POSProduct[]>('/api/pos/products', { params });
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
  get(id: string) {
    return this.http.get<Invoice>(`/api/pos/invoice/${id}`);
  }
  invoices(status?: string, pageNumber = 1) {
    let params = new HttpParams().set('pageNumber', pageNumber);
    if (status) params = params.set('status', status);
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
  today() {
    return this.http.get<TodaySales>('/api/pos/today-sales');
  }
  print(id: string, paper = '80mm') {
    this.http
      .get(`/api/pos/invoice/${id}/print`, { params: { paper }, responseType: 'blob' })
      .subscribe((receipt) => {
        const url = URL.createObjectURL(receipt);
        const popup = window.open(url, '_blank');
        if (popup) popup.addEventListener('load', () => URL.revokeObjectURL(url), { once: true });
      });
  }
  export() {
    return this.http.get('/api/pos/export', { responseType: 'blob' });
  }
  warehouses() {
    return this.http
      .get<{ items: POSWarehouse[] }>('/api/warehouses', {
        params: { isActive: true, pageSize: 100, sortBy: 'warehouseName' },
      })
      .pipe(map((result) => result.items));
  }
}
