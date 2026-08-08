import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface Point {
  label: string;
  value: number;
}
export interface RecentDocument {
  id: string;
  number: string;
  date: string;
  amount: number;
  status: string;
}
export interface Summary {
  todaySales: number;
  todayPurchase: number;
  cashCollection: number;
  upiCollection: number;
  cardCollection: number;
  todayProfit: number;
  todayExpense: number;
  netCollection: number;
  recentSales: RecentDocument[];
  recentPurchases: RecentDocument[];
}
export interface Inventory {
  totalInventoryValue: number;
  lowStockItems: number;
  outOfStockItems: number;
  negativeStock: number;
  expiringProducts: number;
  expiredProducts: number;
  reorderRequired: number;
  attentionItems: Point[];
}
export interface Finance {
  cashBalance: number;
  bankBalance: number;
  receivables: number;
  payables: number;
  profitToday: number;
}
export interface CustomerAnalytics {
  newCustomers: number;
  customerOutstanding: number;
  inactiveCustomers: number;
  topCustomers: Point[];
}
export interface SupplierAnalytics {
  supplierOutstanding: number;
  pendingPayments: number;
  topSuppliers: Point[];
}
export interface SalesAnalytics {
  hourly: Point[];
  daily: Point[];
  monthly: Point[];
  yearly: Point[];
  byCategory: Point[];
  byBrand: Point[];
  byPaymentMode: Point[];
  topProducts: Point[];
  leastProducts: Point[];
  highestMarginProducts: Point[];
}
export interface PurchaseAnalytics {
  daily: Point[];
  monthly: Point[];
  bySupplier: Point[];
}
export interface Notification {
  id: string;
  type: string;
  severity: string;
  title: string;
  message?: string;
  referenceType?: string;
  referenceId?: string;
  isRead: boolean;
  generatedOn: string;
}

@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  constructor(private readonly http: HttpClient) {}
  summary(query: object) {
    return this.http.get<Summary>('/api/dashboard/summary', { params: this.params(query) });
  }
  sales(query: object) {
    return this.http.get<SalesAnalytics>('/api/dashboard/sales', { params: this.params(query) });
  }
  purchase(query: object) {
    return this.http.get<PurchaseAnalytics>('/api/dashboard/purchase', {
      params: this.params(query),
    });
  }
  inventory(refresh = false) {
    return this.http.get<Inventory>('/api/dashboard/inventory', { params: { refresh } });
  }
  customers(query: object) {
    return this.http.get<CustomerAnalytics>('/api/dashboard/customers', {
      params: this.params(query),
    });
  }
  suppliers(query: object) {
    return this.http.get<SupplierAnalytics>('/api/dashboard/suppliers', {
      params: this.params(query),
    });
  }
  finance(query: object) {
    return this.http.get<Finance>('/api/dashboard/finance', { params: this.params(query) });
  }
  notifications(refresh = false) {
    return this.http.get<Notification[]>('/api/dashboard/notifications', { params: { refresh } });
  }
  private params(values: object): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(values))
      if (value !== undefined && value !== null && value !== '')
        params = params.set(key, String(value));
    return params;
  }
}
