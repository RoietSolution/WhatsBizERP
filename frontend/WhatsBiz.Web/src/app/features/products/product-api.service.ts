import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Brand,
  Category,
  ImportResult,
  PagedResult,
  Product,
  ProductInput,
  ProductListItem,
  ProductHistory,
  ProductImage,
  UnitOfMeasure,
} from './product.models';

@Injectable({ providedIn: 'root' })
export class ProductApiService {
  constructor(private readonly http: HttpClient) {}
  search(query: {
    search?: string;
    isActive?: boolean;
    sortBy: string;
    descending: boolean;
    pageNumber: number;
    pageSize: number;
  }): Observable<PagedResult<ProductListItem>> {
    let params = new HttpParams()
      .set('sortBy', query.sortBy)
      .set('descending', query.descending)
      .set('pageNumber', query.pageNumber)
      .set('pageSize', query.pageSize);
    if (query.search) params = params.set('search', query.search);
    if (query.isActive !== undefined) params = params.set('isActive', query.isActive);
    return this.http.get<PagedResult<ProductListItem>>('/api/products', { params });
  }
  get(id: string): Observable<Product> {
    return this.http.get<Product>(`/api/products/${id}`);
  }
  history(id: string): Observable<ProductHistory[]> {
    return this.http.get<ProductHistory[]>(`/api/products/${id}/history`);
  }
  create(input: ProductInput): Observable<Product> {
    return this.http.post<Product>('/api/products', input);
  }
  update(id: string, input: ProductInput): Observable<Product> {
    return this.http.put<Product>(`/api/products/${id}`, input);
  }
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/products/${id}`);
  }
  export(search?: string, isActive?: boolean): Observable<Blob> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (isActive !== undefined) params = params.set('isActive', isActive);
    return this.http.get('/api/products/export', { params, responseType: 'blob' });
  }
  template(): Observable<Blob> {
    return this.http.get('/api/products/import-template', { responseType: 'blob' });
  }
  import(file: File): Observable<ImportResult> {
    const data = new FormData();
    data.append('file', file);
    return this.http.post<ImportResult>('/api/products/import', data);
  }
  uploadImage(id: string, file: File): Observable<unknown> {
    const data = new FormData();
    data.append('file', file);
    return this.http.post(`/api/products/${id}/image`, data);
  }
  image(id: string): Observable<Blob> {
    return this.http.get(`/api/products/${id}/image`, { responseType: 'blob' });
  }
  deleteImage(id: string): Observable<void> {
    return this.http.delete<void>(`/api/products/${id}/image`);
  }
  images(id: string): Observable<ProductImage[]> { return this.http.get<ProductImage[]>(`/api/products/${id}/images`); }
  imageByUrl(url: string, thumbnail = false): Observable<Blob> {
    const imageUrl = thumbnail ? `${url}${url.includes('?') ? '&' : '?'}thumbnail=true` : url;
    return this.http.get(imageUrl, { responseType: 'blob' });
  }
  deleteProductImage(productId: string, imageId: string): Observable<void> { return this.http.delete<void>(`/api/products/${productId}/images/${imageId}`); }
  categories(): Observable<Category[]> {
    return this.http.get<Category[]>('/api/productcategories');
  }
  createCategory(input: Omit<Category, 'productCategoryId' | 'children'>): Observable<Category> {
    return this.http.post<Category>('/api/productcategories', input);
  }
  updateCategory(
    id: string,
    input: Omit<Category, 'productCategoryId' | 'children'>,
  ): Observable<Category> {
    return this.http.put<Category>(`/api/productcategories/${id}`, input);
  }
  deleteCategory(id: string): Observable<void> {
    return this.http.delete<void>(`/api/productcategories/${id}`);
  }
  exportCategories(): Observable<Blob> { return this.http.get('/api/productcategories/export', { responseType: 'blob' }); }
  categoryTemplate(): Observable<Blob> { return this.http.get('/api/productcategories/import-template', { responseType: 'blob' }); }
  importCategories(file: File): Observable<ImportResult> { return this.importMaster('/api/productcategories/import', file); }
  brands(): Observable<Brand[]> {
    return this.http.get<Brand[]>('/api/brands');
  }
  createBrand(input: Omit<Brand, 'brandId'>): Observable<Brand> {
    return this.http.post<Brand>('/api/brands', input);
  }
  updateBrand(id: string, input: Omit<Brand, 'brandId'>): Observable<Brand> {
    return this.http.put<Brand>(`/api/brands/${id}`, input);
  }
  deleteBrand(id: string): Observable<void> {
    return this.http.delete<void>(`/api/brands/${id}`);
  }
  exportBrands(): Observable<Blob> { return this.http.get('/api/brands/export', { responseType: 'blob' }); }
  brandTemplate(): Observable<Blob> { return this.http.get('/api/brands/import-template', { responseType: 'blob' }); }
  importBrands(file: File): Observable<ImportResult> { return this.importMaster('/api/brands/import', file); }
  units(): Observable<UnitOfMeasure[]> {
    return this.http.get<UnitOfMeasure[]>('/api/uom');
  }
  createUnit(input: Omit<UnitOfMeasure, 'unitId'>): Observable<UnitOfMeasure> {
    return this.http.post<UnitOfMeasure>('/api/uom', input);
  }
  updateUnit(id: string, input: Omit<UnitOfMeasure, 'unitId'>): Observable<UnitOfMeasure> {
    return this.http.put<UnitOfMeasure>(`/api/uom/${id}`, input);
  }
  deleteUnit(id: string): Observable<void> {
    return this.http.delete<void>(`/api/uom/${id}`);
  }
  exportUnits(): Observable<Blob> { return this.http.get('/api/uom/export', { responseType: 'blob' }); }
  unitTemplate(): Observable<Blob> { return this.http.get('/api/uom/import-template', { responseType: 'blob' }); }
  importUnits(file: File): Observable<ImportResult> { return this.importMaster('/api/uom/import', file); }
  private importMaster(url: string, file: File): Observable<ImportResult> { const data = new FormData(); data.append('file', file); return this.http.post<ImportResult>(url, data); }
}
