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
}
