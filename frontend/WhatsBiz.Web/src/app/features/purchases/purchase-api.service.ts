import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { PagedPurchases, Purchase, PurchaseDashboard } from './purchase.models';
@Injectable({ providedIn: 'root' })
export class PurchaseApiService {
  constructor(private readonly http: HttpClient) {}
  list(search = '', status = '') {
    let p = new HttpParams().set('search', search);
    if (status) p = p.set('status', status);
    return this.http.get<PagedPurchases>('/api/purchases', { params: p });
  }
  get(id: string) {
    return this.http.get<Purchase>(`/api/purchases/${id}`);
  }
  create(x: object) {
    return this.http.post<{ purchaseInvoiceId: string }>('/api/purchases', x);
  }
  update(id: string, x: object) {
    return this.http.put<Purchase>(`/api/purchases/${id}`, x);
  }
  delete(id: string) {
    return this.http.delete(`/api/purchases/${id}`);
  }
  pay(x: object) {
    return this.http.post('/api/purchases/payment', x);
  }
  return(x: object) {
    return this.http.post('/api/purchases/return', x);
  }
  dashboard() {
    return this.http.get<PurchaseDashboard>('/api/purchases/today');
  }
  suppliers(search = '') {
    return this.http.get<{ supplierId: string; supplierCode: string; supplierName: string }[]>(
      '/api/suppliers/dropdown',
      { params: { search } },
    );
  }
  products(search = '', barcode = '') {
    let p = new HttpParams();
    if (search) p = p.set('search', search);
    if (barcode) p = p.set('barcode', barcode);
    return this.http.get<any[]>('/api/pos/products', { params: p });
  }
  warehouses() {
    return this.http.get<any[]>('/api/warehouses/dropdown');
  }
  uploadAttachment(purchaseId: string, file: File) {
    const data = new FormData();
    data.append('file', file);
    return this.http.post(`/api/purchases/${purchaseId}/attachments`, data);
  }
  export() {
    return this.http.get('/api/purchases/export', { responseType: 'blob' });
  }
  template() {
    return this.http.get('/api/purchases/template', { responseType: 'blob' });
  }
}
