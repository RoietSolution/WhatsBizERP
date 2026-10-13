import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';
import { CustomerSessionService } from './customer-session.service';

export interface CustomerCoinTransaction { coinTransactionId:string; transactionType:string; coins:number; sourceType:string; sourceId?:string; rupeeValue?:number; description?:string; createdOn:string; expiresAt?:string; }
export interface CustomerWallet { customerId:string; availableCoins:number; totalEarned:number; totalRedeemed:number; transactions:CustomerCoinTransaction[]; }
export interface CustomerReferral { referralId:string; referrerCustomerId:string; referredCustomerId:string; status:string; qualificationType:string; qualifyingOrderId?:string; qualifiedAt?:string; rewardedAt?:string; reversedAt?:string; rejectionReason?:string; createdAt:string; referredCustomerDisplay:string; }
export interface CustomerReferralCode { referralCodeId:string; customerId:string; code:string; referralUrl:string; isActive:boolean; createdAt:string; }
export interface CustomerRewards { enabled:boolean; wallet?:CustomerWallet; referralCode?:CustomerReferralCode; referrals:CustomerReferral[]; terms:string; coinValidityDays:number; }
export interface ReferralResolution { code:string; tenantKey:string; retailerName:string; isEnabled:boolean; }

@Injectable({providedIn:'root'})
export class CustomerRewardsService {
  readonly enabled=signal(false);
  private readonly prefix='khatadhari.customer-pending-referral.';
  private readonly cache=new Map<string,CustomerRewards>();
  private readonly inflight=new Map<string,Promise<CustomerRewards>>();
  constructor(private readonly http:HttpClient, private readonly session:CustomerSessionService) {}
  get(storeKey:string):Promise<CustomerRewards>{const key=this.cacheKey(storeKey);const cached=this.cache.get(key);if(cached){this.enabled.set(cached.enabled);return Promise.resolve(cached);}const pending=this.inflight.get(key);if(pending)return pending;const request=firstValueFrom(this.http.get<CustomerRewards>(this.url(storeKey)+'/rewards',{headers:{'X-Customer-Session':this.session.token(storeKey)}})).then(value=>{this.cache.set(key,value);this.enabled.set(value.enabled);return value;}).catch(error=>{this.enabled.set(false);throw error;}).finally(()=>this.inflight.delete(key));this.inflight.set(key,request);return request;}
  resolve(code:string):Promise<ReferralResolution>{return firstValueFrom(this.http.get<ReferralResolution>(environment.apiBaseUrl+'/api/customer-referrals/resolve/'+encodeURIComponent(code)));}
  async capture(storeKey:string,code:string):Promise<void>{await firstValueFrom(this.http.post<void>(this.url(storeKey)+'/customer-referral',{code,source:'STOREFRONT'},{headers:{'X-Customer-Session':this.session.token(storeKey)}}));}
  rememberPending(storeKey:string,code:string):void{if(code.trim())localStorage.setItem(this.prefix+storeKey.trim().toLowerCase(),code.trim());}
  async capturePending(storeKey:string):Promise<void>{const key=this.prefix+storeKey.trim().toLowerCase();const code=localStorage.getItem(key);if(!code||!this.session.token(storeKey))return;try{await this.capture(storeKey,code);localStorage.removeItem(key);}catch(error){if(error instanceof HttpErrorResponse&&error.status>=400&&error.status<500)localStorage.removeItem(key);}}
  private cacheKey(storeKey:string):string{return storeKey.trim().toLowerCase()+':'+this.session.customerId();}
  private url(storeKey:string):string{return environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey);}
}