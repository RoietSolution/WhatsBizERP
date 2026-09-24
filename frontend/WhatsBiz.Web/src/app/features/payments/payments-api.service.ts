import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';

export type PaymentProvider='RAZORPAY'|'DIRECT_UPI'|'COD';
export interface PaymentProviderSetting {provider:PaymentProvider;isEnabled:boolean;isDefault:boolean;isConfigured:boolean;maskedKeyId:string|null;hasKeySecret:boolean;hasWebhookSecret:boolean;isTestMode:boolean;upiVpa:string|null;payeeName:string|null;}
export interface PaymentSettings {onlinePaymentEnabled:boolean;providers:PaymentProviderSetting[];}
export interface CommercePayment {paymentId:string;tenantId:string;tenantName:string;orderId:string;orderNumber:string;customerName:string|null;provider:PaymentProvider;amount:number;currency:string;status:string;providerOrderId:string|null;providerPaymentId:string|null;paymentLink:string|null;transactionReference:string|null;createdAt:string;paidAt:string|null;verifiedAt:string|null;verifiedBy:string|null;}
export interface PaymentPage {items:CommercePayment[];totalCount:number;pageNumber:number;pageSize:number;}
export interface PaymentFilters {tenantId?:string;dateFrom?:string;dateTo?:string;status?:string;provider?:string;pageNumber:number;pageSize:number;}

@Injectable({providedIn:'root'})
export class PaymentsApiService {
  constructor(private readonly http:HttpClient){}
  private settingsRoot(tenantId:string){return `/api/payments/administration/tenants/${tenantId}/settings`;}
  settings(tenantId:string){return this.http.get<PaymentSettings>(this.settingsRoot(tenantId));}
  saveRazorpay(tenantId:string,value:{keyId:string;keySecret:string|null;webhookSecret:string|null;isEnabled:boolean;isDefault:boolean;isTestMode:boolean}){return this.http.put<PaymentSettings>(`${this.settingsRoot(tenantId)}/razorpay`,value);}
  saveDirectUpi(tenantId:string,value:{upiVpa:string;payeeName:string;isEnabled:boolean;isDefault:boolean}){return this.http.put<PaymentSettings>(`${this.settingsRoot(tenantId)}/direct-upi`,value);}
  saveCod(tenantId:string,value:{isEnabled:boolean;isDefault:boolean}){return this.http.put<PaymentSettings>(`${this.settingsRoot(tenantId)}/cod`,value);}
  saveOptions(tenantId:string,value:{onlinePaymentEnabled:boolean}){return this.http.put<PaymentSettings>(`${this.settingsRoot(tenantId)}/options`,value);}
  list(filters:PaymentFilters,applicationOwner=false){let params=new HttpParams().set('pageNumber',filters.pageNumber).set('pageSize',filters.pageSize);for(const [key,value] of Object.entries(filters)){if(value!==undefined&&value!==''&&key!=='pageNumber'&&key!=='pageSize')params=params.set(key,String(value));}return this.http.get<PaymentPage>(applicationOwner?'/api/payments/administration':'/api/payments',{params});}
  get(paymentId:string,applicationOwner=false){return this.http.get<CommercePayment>(applicationOwner?`/api/payments/administration/${paymentId}`:`/api/payments/${paymentId}`);}
  verify(paymentId:string,reference:string|null){return this.http.post<CommercePayment>(`/api/payments/${paymentId}/verify-direct-upi`,{reference});}
}
