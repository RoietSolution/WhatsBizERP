import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
export interface Company {
  companyId: string;
  companyCode: string;
  companyName: string;
  legalName?: string;
  gstin?: string;
  pan?: string;
  cin?: string;
  addressLine1?: string;
  addressLine2?: string;
  city?: string;
  state?: string;
  stateCode?: string;
  country: string;
  postalCode?: string;
  email?: string;
  phone?: string;
  bankName?: string;
  accountNumber?: string;
  ifscCode?: string;
  termsAndConditions?: string;
  invoiceFooter?: string;
}
export interface Branch {
  branchId: string;
  branchCode: string;
  branchName: string;
  email?: string;
  phone?: string;
  address?: string;
  city?: string;
  state?: string;
  postalCode?: string;
  isDefault: boolean;
  isActive: boolean;
}
export interface Setting {
  key: string;
  value?: string;
  dataType: string;
  category: string;
}
export interface FinancialYear {
  id: string;
  code: string;
  startDate: string;
  endDate: string;
  status: string;
  isDefault: boolean;
}
export interface Backup {
  id: string;
  fileName: string;
  filePath: string;
  startedOn: string;
  completedOn?: string;
  fileSizeBytes?: number;
  status: string;
  isVerified: boolean;
}
export interface Audit {
  id: number;
  userName?: string;
  action: string;
  entityType?: string;
  requestPath?: string;
  httpMethod?: string;
  ipAddress?: string;
  succeeded: boolean;
  occurredOn: string;
}
export interface AdminUser { userId: string; userName: string; email: string; isActive: boolean; isDeleted: boolean; }
export interface AdminRole { roleId: string; roleName: string; permissions: string[]; }
export interface CustomerNotificationSettings {
  enabled: boolean; whatsAppEnabled: boolean; smsEnabled: boolean;
  successfulSale: boolean; successfulPayment: boolean;
  whatsAppTemplate: string; smsTemplate: string;
}
export interface CustomerNotificationHistory {
  id: string; createdOn: string; customerName: string; invoiceNumber: string;
  eventType: string; channel: string; recipient: string; status: string;
  attemptCount: number; errorMessage?: string; sentOn?: string; lastAttemptOn?: string;
}
export interface NotificationConfigurationStatus { whatsAppConfigured: boolean; smsConfigured: boolean; message: string; }
export interface DemoRequestSummary {
  id: number; referenceNo: string; name: string; mobile: string; businessName?: string;
  businessType?: string; city?: string; source: string; createdOn: string; status: string;
}
export interface DemoRequestDetail extends DemoRequestSummary {
  email?: string; message?: string; utmSource?: string; utmMedium?: string; utmCampaign?: string;
  utmContent?: string; landingPage?: string; referrer?: string; notificationStatus: string; modifiedOn?: string;
}
export interface PagedDemoRequests { items: DemoRequestSummary[]; totalCount: number; pageNumber: number; pageSize: number; }
@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private root = '/api/admin';
  constructor(private http: HttpClient) {}
  company() {
    return this.http.get<Company>(`${this.root}/company`);
  }
  saveCompany(x: Company) {
    return this.http.put<Company>(`${this.root}/company`, x);
  }
  branches() {
    return this.http.get<Branch[]>(`${this.root}/branches`);
  }
  addBranch(x: Partial<Branch>) {
    return this.http.post<Branch>(`${this.root}/branches`, x);
  }
  settings() {
    return this.http.get<Setting[]>(`${this.root}/settings`);
  }
  saveSettings(x: Setting[]) {
    return this.http.put<void>(`${this.root}/settings`, x);
  }
  years() {
    return this.http.get<FinancialYear[]>(`${this.root}/financial-years`);
  }
  saveYear(x: Partial<FinancialYear>) {
    return this.http.post<void>(`${this.root}/financial-years`, x);
  }
  backups() {
    return this.http.get<Backup[]>(`${this.root}/backup`);
  }
  backup() {
    return this.http.post<Backup>(`${this.root}/backup`, {});
  }
  restore(backupId: string) {
    return this.http.post<{ message: string }>(`${this.root}/restore`, {
      backupId,
      validationOnly: true,
      confirm: false,
    });
  }
  audit(login = false) {
    return this.http.get<any[]>(`${this.root}/${login ? 'login-history' : 'audit'}`, {
      params: new HttpParams().set('take', 500),
    });
  }
  users() { return this.http.get<AdminUser[]>(`${this.root}/users`); }
  roles() { return this.http.get<AdminRole[]>(`${this.root}/roles`); }
  customerNotificationSettings() { return this.http.get<CustomerNotificationSettings>(`${this.root}/customer-notifications/settings`); }
  saveCustomerNotificationSettings(x: CustomerNotificationSettings) { return this.http.put<void>(`${this.root}/customer-notifications/settings`, x); }
  customerNotificationHistory() { return this.http.get<CustomerNotificationHistory[]>(`${this.root}/customer-notifications/history`); }
  retryCustomerNotification(id: string) { return this.http.post<void>(`${this.root}/customer-notifications/history/${id}/retry`, {}); }
  customerNotificationConfigurationStatus() { return this.http.get<NotificationConfigurationStatus>(`${this.root}/customer-notifications/configuration-status`); }
  demoRequests(search = '', status = '', from = '', to = '') {
    let params = new HttpParams().set('pageSize', 100);
    if (search.trim()) params = params.set('search', search.trim());
    if (status) params = params.set('status', status);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<PagedDemoRequests>('/api/demo-requests', { params });
  }
  demoRequest(id: number) { return this.http.get<DemoRequestDetail>(`/api/demo-requests/${id}`); }
  updateDemoRequestStatus(id: number, status: string) {
    return this.http.patch<DemoRequestDetail>(`/api/demo-requests/${id}/status`, { status });
  }
}
