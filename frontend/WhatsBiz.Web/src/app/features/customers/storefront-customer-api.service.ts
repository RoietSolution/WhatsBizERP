import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface StorefrontCustomerRow {
  customerId: string; name: string; mobile?: string; registeredOn: string;
  lastSuccessfulLogin?: string; lastVisit?: string; totalOrders: number; totalSpent: number; isActive: boolean;
}
export interface StorefrontCustomerPage { items: StorefrontCustomerRow[]; totalCount: number; pageNumber: number; pageSize: number; }
export interface StorefrontCustomerDetail {
  customer: StorefrontCustomerRow;
  orders: { orderNumber: string; placedOn: string; status: string; total: number }[];
  recentActivity: { eventType: string; occurredOn: string; productId?: string; productName?: string }[];
  returnRequests: { orderNumber: string; productName: string; quantity: number; status: string; requestedAtUtc: string }[];
  searchHistory?: { keyword: string; outcome: string; createdAtUtc: string }[];
  wishlist?: { productId: string; productName: string; sellingPrice: number; savedAt: string }[];
}
export interface StorefrontBlacklistEntry { blacklistId: string; normalizedPhone: string; reason: string; isActive: boolean; blockedBy?: string; blockedAtUtc: string; unblockedBy?: string; unblockedAtUtc?: string; }

@Injectable({ providedIn: 'root' })
export class StorefrontCustomerApiService {
  constructor(private readonly http: HttpClient) {}
  search(search: string, sortBy: string, descending: boolean, pageNumber: number, pageSize: number) {
    const params = new HttpParams().set('sortBy', sortBy).set('descending', descending).set('pageNumber', pageNumber).set('pageSize', pageSize).set('search', search);
    return this.http.get<StorefrontCustomerPage>('/api/storefront-customers', { params });
  }
  get(id: string) { return this.http.get<StorefrontCustomerDetail>(`/api/storefront-customers/${id}`); }
  blacklistForPhone(phone: string) { return this.http.get<{ items: StorefrontBlacklistEntry[] }>('/api/storefront-customer-blacklist', { params: new HttpParams().set('activeOnly', false).set('search', phone).set('pageNumber', 1).set('pageSize', 20) }); }
  block(phone: string, reason: string) { return this.http.post<StorefrontBlacklistEntry>('/api/storefront-customer-blacklist', { phone, reason }); }
  unblock(id: string) { return this.http.post<void>(`/api/storefront-customer-blacklist/${id}/unblock`, {}); }
}
