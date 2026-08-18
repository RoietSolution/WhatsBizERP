import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
export interface GstRow {
  documentNumber?: string;
  documentDate?: string;
  partyName?: string;
  partyGstin?: string;
  supplyType?: string;
  sourceType?: string;
  hsnCode?: string;
  gstRate: number;
  quantity: number;
  taxableAmount: number;
  cgstAmount: number;
  sgstAmount: number;
  igstAmount: number;
  cessAmount: number;
  totalTax: number;
  netTaxPayable: number;
}
export interface GstSettings {
  companyGstin?: string;
  legalName: string;
  tradeName?: string;
  stateCode: string;
  registrationType: string;
  isCompositionScheme: boolean;
  gstEffectiveDate: string;
}
export interface GstFilter {
  from?: string;
  to?: string;
  financialYear?: string;
  month?: number;
  branchId?: string;
  customerId?: string;
  supplierId?: string;
  gstRate?: number;
  format?: string;
}
@Injectable({ providedIn: 'root' })
export class GstApiService {
  private readonly root = '/api/gst';
  constructor(private http: HttpClient) {}
  report(name: string, f: GstFilter) {
    return this.http.get<GstRow[]>(`${this.root}/${name}`, { params: this.params(f) });
  }
  export(name: string, format: string, f: GstFilter) {
    return this.http.get(`${this.root}/export/${name}`, {
      params: this.params({ ...f, format }),
      responseType: 'blob',
    });
  }
  settings() {
    return this.http.get<GstSettings>(`${this.root}/configuration`);
  }
  saveSettings(v: GstSettings) {
    return this.http.put<GstSettings>(`${this.root}/configuration`, v);
  }
  private params(v: GstFilter) {
    let p = new HttpParams();
    Object.entries(v).forEach(([k, x]) => {
      if (x !== undefined && x !== null && x !== '') p = p.set(k, String(x));
    });
    return p;
  }
}
