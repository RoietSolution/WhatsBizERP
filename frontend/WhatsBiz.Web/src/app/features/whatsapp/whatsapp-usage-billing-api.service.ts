import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface WhatsAppUsageCurrencyAmount { currency:string; estimatedMetaCost:number; }
export interface WhatsAppUsageCategorySummary {
  category:string; deliveredMessages:number; estimatedMetaCost:number|null; currency:string|null;
  rateConfigured:boolean; currencyBreakdown:WhatsAppUsageCurrencyAmount[];
}
export interface WhatsAppUsageSummary {
  period:string; currency:string|null; categories:WhatsAppUsageCategorySummary[];
  estimatedMetaCharges:number|null; rateConfigurationComplete:boolean;
  currencyBreakdown:WhatsAppUsageCurrencyAmount[];
  khataDhariSubscription:{planName:string|null;whatsAppCommerceEntitled:boolean};
}

@Injectable({providedIn:'root'})
export class WhatsAppUsageBillingApiService {
  constructor(private readonly http:HttpClient){}
  summary(year:number,month:number){
    const params=new HttpParams().set('year',year).set('month',month);
    return this.http.get<WhatsAppUsageSummary>('/api/whatsapp/usage/summary',{params});
  }
}
