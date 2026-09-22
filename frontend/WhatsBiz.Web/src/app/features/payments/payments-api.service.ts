import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';

export type PaymentProvider='RAZORPAY'|'DIRECT_UPI'|'COD';
export interface PaymentProviderSetting {provider:PaymentProvider;isEnabled:boolean;isDefault:boolean;isConfigured:boolean;maskedKeyId:string|null;hasKeySecret:boolean;hasWebhookSecret:boolean;isTestMode:boolean;upiVpa:string|null;payeeName:string|null;}
export interface PaymentSettings {onlinePaymentEnabled:boolean;providers:PaymentProviderSetting[];}
export interface CommercePayment {paymentId:string;orderId:string;orderNumber:string;customerName:string|null;provider:PaymentProvider;amount:number;currency:string;status:string;providerOrderId:string|null;providerPaymentId:string|null;paymentLink:string|null;transactionReference:string|null;createdAt:string;paidAt:string|null;verifiedAt:string|null;verifiedBy:string|null;}

@Injectable({providedIn:'root'})
export class PaymentsApiService {
  constructor(private readonly http:HttpClient){}
  settings(){return this.http.get<PaymentSettings>('/api/payments/settings');}
  saveRazorpay(value:{keyId:string;keySecret:string|null;webhookSecret:string|null;isEnabled:boolean;isDefault:boolean;isTestMode:boolean}){return this.http.put<PaymentSettings>('/api/payments/settings/razorpay',value);}
  saveDirectUpi(value:{upiVpa:string;payeeName:string;isEnabled:boolean;isDefault:boolean}){return this.http.put<PaymentSettings>('/api/payments/settings/direct-upi',value);}
  saveCod(value:{isEnabled:boolean;isDefault:boolean}){return this.http.put<PaymentSettings>('/api/payments/settings/cod',value);}
  saveOptions(value:{onlinePaymentEnabled:boolean}){return this.http.put<PaymentSettings>('/api/payments/settings/options',value);}
  list(status?:string){let params=new HttpParams();if(status)params=params.set('status',status);return this.http.get<CommercePayment[]>('/api/payments',{params});}
  verify(paymentId:string,reference:string|null){return this.http.post<CommercePayment>(`/api/payments/${paymentId}/verify-direct-upi`,{reference});}
}
