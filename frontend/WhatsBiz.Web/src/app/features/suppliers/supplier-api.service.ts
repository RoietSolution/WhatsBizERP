import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedSuppliers, PaymentTerm, Supplier, SupplierInput } from './supplier.models';
@Injectable({ providedIn: 'root' })
export class SupplierApiService {
  constructor(private readonly http: HttpClient) {}
  search(q: {
    search?: string;
    isActive?: boolean;
    sortBy: string;
    descending: boolean;
    pageNumber: number;
    pageSize: number;
  }): Observable<PagedSuppliers> {
    let p = new HttpParams()
      .set('sortBy', q.sortBy)
      .set('descending', q.descending)
      .set('pageNumber', q.pageNumber)
      .set('pageSize', q.pageSize);
    if (q.search) p = p.set('search', q.search);
    if (q.isActive !== undefined) p = p.set('isActive', q.isActive);
    return this.http.get<PagedSuppliers>('/api/suppliers', { params: p });
  }
  get(id: string) {
    return this.http.get<Supplier>(`/api/suppliers/${id}`);
  }
  create(x: SupplierInput) {
    return this.http.post<Supplier>('/api/suppliers', x);
  }
  update(id: string, x: SupplierInput) {
    return this.http.put<Supplier>(`/api/suppliers/${id}`, x);
  }
  delete(id: string) {
    return this.http.delete<void>(`/api/suppliers/${id}`);
  }
  terms() {
    return this.http.get<PaymentTerm[]>('/api/suppliers/payment-terms');
  }
  export() {
    return this.http.get('/api/suppliers/export', { responseType: 'blob' });
  }
  template() {
    return this.http.get('/api/suppliers/import-template', { responseType: 'blob' });
  }
  import(file: File) {
    const d = new FormData();
    d.append('file', file);
    return this.http.post<{ importedCount: number; errors: string[] }>('/api/suppliers/import', d);
  }
  upload(id: string, type: string, file: File) {
    const d = new FormData();
    d.append('documentType', type);
    d.append('file', file);
    return this.http.post(`/api/suppliers/${id}/documents`, d);
  }
  document(id: string, did: string) {
    return this.http.get(`/api/suppliers/${id}/documents/${did}`, { responseType: 'blob' });
  }
  deleteDocument(id: string, did: string) {
    return this.http.delete<void>(`/api/suppliers/${id}/documents/${did}`);
  }
}
