import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

export type QualificationType='CUSTOMER_REGISTERED'|'FIRST_ORDER_PLACED'|'FIRST_PAID_ORDER'|'FIRST_COMPLETED_ORDER'|'FIRST_COMPLETED_ORDER_MIN_AMOUNT'|'MANUAL_APPROVAL';
export interface ReferralConfiguration {isEnabled:boolean;qualificationType:QualificationType;minimumQualifyingAmount:number;referrerRewardCoins:number;referredRewardCoins:number;coinValidityDays:number;maximumRewardedReferralsPerCustomerMonth:number;maximumCoinsPerCustomerMonth:number;reverseOnRefund:boolean;redemptionCoins:number;redemptionValue:number;minimumRedemptionCoins:number;maximumOrderPercentage:number;allowWithCoupons:boolean;allowDiscountedProducts:boolean;allowTax:boolean;allowDelivery:boolean;}
export interface ReferralCode {referralCodeId:string;customerId:string;code:string;referralUrl:string;isActive:boolean;createdAt:string;}
export interface ReferralResolution {code:string;tenantKey:string;retailerName:string;isEnabled:boolean;}
export interface Referral {referralId:string;referrerCustomerId:string;referredCustomerId:string;status:string;qualificationType:string;qualifyingOrderId?:string;qualifiedAt?:string;rewardedAt?:string;reversedAt?:string;rejectionReason?:string;createdAt:string;referredCustomerDisplay:string;}
export interface ReferralMetrics {totalReferrals:number;pendingReferrals:number;successfulReferrals:number;reversedReferrals:number;coinsIssued:number;coinsRedeemed:number;outstandingCoins:number;conversionPercentage:number;referralRevenue:number;topReferrers:{customerId:string;customerName:string;successfulReferrals:number;coinsEarned:number}[];}
@Injectable({providedIn:'root'})
export class ReferralRewardsApiService {
  constructor(private readonly http:HttpClient){}
  configuration(){return this.http.get<ReferralConfiguration>('/api/customer-referrals/configuration');}
  resolve(code:string){return this.http.get<ReferralResolution>(`/api/customer-referrals/resolve/${encodeURIComponent(code)}`);}
  save(input:ReferralConfiguration){return this.http.put<ReferralConfiguration>('/api/customer-referrals/configuration',input);}
  metrics(){return this.http.get<ReferralMetrics>('/api/customer-referrals/metrics');}
  history(customerId?:string){return this.http.get<Referral[]>('/api/customer-referrals/history',{params:customerId?{customerId}:{}});}
  code(customerId:string){return this.http.get<ReferralCode>(`/api/customer-referrals/customers/${customerId}/code`);}
  setCodeActive(customerId:string,isActive:boolean){return this.http.put<void>(`/api/customer-referrals/customers/${customerId}/code/active`,{isActive});}
  approve(referralId:string){return this.http.post<void>(`/api/customer-referrals/${referralId}/approve`,{});}
  adjust(customerId:string,coins:number,reason:string){return this.http.post<void>('/api/customer-referrals/adjustments',{customerId,coins,reason});}
}
