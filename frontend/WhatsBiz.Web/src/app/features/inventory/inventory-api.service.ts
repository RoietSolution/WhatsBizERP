import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import {
  InventorySummary,
  InventoryTransaction,
  OperationResult,
  PagedBalances,
  PagedTransactions,
  ProductOption,
  Reservation,
  WarehouseOption,
} from './inventory.models';
@Injectable({ providedIn: 'root' })
export class InventoryApiService {
  constructor(private readonly http: HttpClient) {}
  summary(warehouseId?: string) {
    let params = new HttpParams();
    if (warehouseId) params = params.set('warehouseId', warehouseId);
    return this.http.get<InventorySummary>('/api/inventory/summary', { params });
  }
  balances(q: {
    search?: string;
    warehouseId?: string;
    productId?: string;
    pageNumber: number;
    pageSize: number;
  }) {
    let p = new HttpParams().set('pageNumber', q.pageNumber).set('pageSize', q.pageSize);
    if (q.search) p = p.set('search', q.search);
    if (q.warehouseId) p = p.set('warehouseId', q.warehouseId);
    if (q.productId) p = p.set('productId', q.productId);
    return this.http.get<PagedBalances>('/api/inventory/balance', { params: p });
  }
  transactions(q: {
    search?: string;
    warehouseId?: string;
    transactionType?: string;
    from?: string;
    to?: string;
    pageNumber: number;
    pageSize: number;
  }) {
    let p = new HttpParams().set('pageNumber', q.pageNumber).set('pageSize', q.pageSize);
    for (const [key, value] of Object.entries(q))
      if (value && key !== 'pageNumber' && key !== 'pageSize') p = p.set(key, value);
    return this.http.get<PagedTransactions>('/api/inventory/transactions', { params: p });
  }
  transaction(id: string) {
    return this.http.get<InventoryTransaction>(`/api/inventory/transaction/${id}`);
  }
  reservations(warehouseId?: string, productId?: string) {
    let p = new HttpParams();
    if (warehouseId) p = p.set('warehouseId', warehouseId);
    if (productId) p = p.set('productId', productId);
    return this.http.get<Reservation[]>('/api/inventory/reservations', { params: p });
  }
  adjust(value: object) {
    return this.http.post<OperationResult>('/api/inventory/adjustment', value);
  }
  transfer(value: object) {
    return this.http.post<OperationResult>('/api/inventory/transfer', value);
  }
  reserve(value: object) {
    return this.http.post<OperationResult>('/api/inventory/reserve', value);
  }
  export(search?: string, warehouseId?: string, productId?: string) {
    let p = new HttpParams();
    if (search) p = p.set('search', search);
    if (warehouseId) p = p.set('warehouseId', warehouseId);
    if (productId) p = p.set('productId', productId);
    return this.http.get('/api/inventory/balance/export', { params: p, responseType: 'blob' });
  }
  previewShopifyInventory(file: File, sourceStore: string, warehouseMappings: Record<string, string>) { const data = new FormData(); data.append('file', file); data.append('sourceStore', sourceStore); data.append('warehouseMappingsJson', JSON.stringify(warehouseMappings)); return this.http.post<any>('/api/inventory/shopify-import/preview', data); }
  commitShopifyInventory(file: File, sourceStore: string, warehouseMappings: Record<string, string>, expectedRows: { key: string; current: number }[], applyHsnMetadata: boolean) { const data = new FormData(); data.append('file', file); data.append('sourceStore', sourceStore); data.append('warehouseMappingsJson', JSON.stringify(warehouseMappings)); data.append('expectedRowsJson', JSON.stringify(expectedRows)); data.append('applyHsnMetadata', String(applyHsnMetadata)); return this.http.post<any>('/api/inventory/shopify-import/commit', data); }
  shopifyInventoryHistory() { return this.http.get<any[]>('/api/inventory/shopify-import/history'); }
  products() {
    return this.http.get<{ items: ProductOption[] }>('/api/products', {
      params: new HttpParams().set('pageSize', 200),
    });
  }
  warehouses() {
    return this.http.get<WarehouseOption[]>('/api/warehouses/dropdown');
  }
}
