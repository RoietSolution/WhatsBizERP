import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { CollectionDetail, CollectionInput, CollectionPage, CollectionProduct, CollectionSendResult } from './collection.models';
@Injectable({ providedIn: 'root' })
export class CollectionApiService {
  private readonly root = '/api/commerce/collections';
  constructor(private readonly http: HttpClient) {}
  list(search: string, isActive: boolean | undefined, pageNumber: number, pageSize: number) { let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize); if (search) params = params.set('search', search); if (isActive !== undefined) params = params.set('isActive', isActive); return this.http.get<CollectionPage>(this.root, { params }); }
  get(id: string) { return this.http.get<CollectionDetail>(`${this.root}/${id}`); }
  create(input: CollectionInput) { return this.http.post<CollectionDetail>(this.root, input); }
  update(id: string, input: CollectionInput) { return this.http.put<CollectionDetail>(`${this.root}/${id}`, input); }
  delete(id: string) { return this.http.delete<void>(`${this.root}/${id}`); }
  products(id: string) { return this.http.get<CollectionProduct[]>(`${this.root}/${id}/products`); }
  addProducts(id: string, productIds: string[]) { return this.http.post<CollectionProduct[]>(`${this.root}/${id}/products`, { productIds }); }
  removeProduct(id: string, productId: string) { return this.http.delete<void>(`${this.root}/${id}/products/${productId}`); }
  send(id: string, customerId: string) { return this.http.post<CollectionSendResult>(`${this.root}/${id}/send-whatsapp`, { customerId }); }
}
