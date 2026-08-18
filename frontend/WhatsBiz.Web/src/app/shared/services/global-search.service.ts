import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { catchError, forkJoin, map, of } from 'rxjs';

export interface GlobalSearchResult {
  id: string;
  label: string;
  detail: string;
  type: 'Product' | 'Customer' | 'Supplier' | 'Invoice';
  route: string;
}

interface Paged<T> { items: T[]; }
interface Product { productId: string; productCode: string; productName: string; categoryName: string; }
interface Party { customerId?: string; supplierId?: string; customerCode?: string; supplierCode?: string; customerName?: string; supplierName?: string; }
interface Invoice { invoiceId?: string; purchaseInvoiceId?: string; invoiceNumber: string; supplierName?: string; customerName?: string; }

@Injectable({ providedIn: 'root' })
export class GlobalSearchService {
  readonly results = signal<GlobalSearchResult[]>([]);
  readonly loading = signal(false);
  private requestId = 0;

  constructor(private readonly http: HttpClient) {}

  search(value: string): void {
    const query = value.trim();
    const requestId = ++this.requestId;
    if (query.length < 2) { this.results.set([]); this.loading.set(false); return; }
    this.loading.set(true);
    const params = new HttpParams().set('search', query).set('sortBy', 'name').set('descending', false).set('pageNumber', 1).set('pageSize', 5);
    forkJoin({
      products: this.http.get<Paged<Product>>('/api/products', { params }).pipe(catchError(() => of({ items: [] }))),
      customers: this.http.get<Paged<Party>>('/api/customers', { params }).pipe(catchError(() => of({ items: [] }))),
      suppliers: this.http.get<Paged<Party>>('/api/suppliers', { params }).pipe(catchError(() => of({ items: [] }))),
      salesInvoices: this.http.get<Paged<Invoice>>('/api/pos/invoices', { params: new HttpParams().set('search', query).set('pageNumber', 1).set('pageSize', 5) }).pipe(catchError(() => of({ items: [] }))),
      purchaseInvoices: this.http.get<Paged<Invoice>>('/api/purchases', { params: new HttpParams().set('search', query).set('pageNumber', 1).set('pageSize', 5) }).pipe(catchError(() => of({ items: [] }))),
    }).pipe(map(({ products, customers, suppliers, salesInvoices, purchaseInvoices }) => [
      ...products.items.map((x) => ({ id: x.productId, label: x.productName, detail: `${x.productCode} · ${x.categoryName}`, type: 'Product' as const, route: `/products/${x.productId}` })),
      ...customers.items.map((x) => ({ id: x.customerId!, label: x.customerName!, detail: x.customerCode!, type: 'Customer' as const, route: `/customers/${x.customerId}` })),
      ...suppliers.items.map((x) => ({ id: x.supplierId!, label: x.supplierName!, detail: x.supplierCode!, type: 'Supplier' as const, route: `/suppliers/${x.supplierId}` })),
      ...salesInvoices.items.map((x) => ({ id: x.invoiceId!, label: x.invoiceNumber, detail: `Sales invoice · ${x.customerName ?? 'Walk-in customer'}`, type: 'Invoice' as const, route: `/pos/invoice/${x.invoiceId}` })),
      ...purchaseInvoices.items.map((x) => ({ id: x.purchaseInvoiceId!, label: x.invoiceNumber, detail: `Purchase invoice · ${x.supplierName ?? 'Supplier'}`, type: 'Invoice' as const, route: `/purchases/${x.purchaseInvoiceId}` })),
    ].slice(0, 12))).subscribe({ next: (items) => { if (requestId === this.requestId) this.results.set(items); }, error: () => { if (requestId === this.requestId) this.results.set([]); }, complete: () => { if (requestId === this.requestId) this.loading.set(false); } });
  }

  clear(): void { this.requestId++; this.results.set([]); this.loading.set(false); }
}
