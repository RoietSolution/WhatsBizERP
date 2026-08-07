import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
export interface Outstanding {
  id: string;
  partyId: string;
  partyCode: string;
  partyName: string;
  invoiceId: string;
  invoiceNumber: string;
  invoiceDate: string;
  dueDate: string;
  invoiceAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  ageDays: number;
  ageBucket: string;
  allocate?: number;
}
export interface FollowUp {
  id: string;
  customerId: string;
  customerCode: string;
  customerName: string;
  invoiceId?: string;
  invoiceNumber?: string;
  followUpDate: string;
  nextFollowUpDate?: string;
  paymentCommitmentDate?: string;
  communicationMode: string;
  notes?: string;
  outcome?: string;
}
@Injectable({ providedIn: 'root' })
export class ReceivablesApiService {
  constructor(private http: HttpClient) {}
  customers() {
    return this.http.get<any[]>('/api/customers/dropdown');
  }
  suppliers() {
    return this.http.get<any[]>('/api/suppliers/dropdown');
  }
  outstanding(kind: string, partyId?: string, ageBucket?: string) {
    let p = new HttpParams();
    if (partyId) p = p.set(kind === 'customer' ? 'customerId' : 'supplierId', partyId);
    if (ageBucket) p = p.set('ageBucket', ageBucket);
    return this.http.get<Outstanding[]>(
      `/api/${kind}-${ageBucket !== undefined ? 'ageing' : 'outstanding'}`,
      { params: p },
    );
  }
  receipt(x: object) {
    return this.http.post<any>('/api/receipts', x);
  }
  payment(x: object) {
    return this.http.post<any>('/api/payments', x);
  }
  followUps(customerId?: string) {
    return this.http.get<FollowUp[]>('/api/collection-followup', {
      params: customerId ? { customerId } : {},
    });
  }
  saveFollowUp(x: object) {
    return this.http.post<string>('/api/collection-followup', x);
  }
}
