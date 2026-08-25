import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface ProductCoinRule { productId:string; productCode?:string; productName?:string; isEnabled:boolean; coinsPerUnit:number; }
export interface CategoryCoinRule { productCategoryId:string; categoryCode?:string; categoryName?:string; isEnabled:boolean; coinsPerUnit:number; }
export interface CoinConfiguration { isEnabled:boolean; purchaseAmount:number; purchaseCoins:number; purchaseCoinValidityDays:number; earningPriority:'PRODUCT_FIRST'|'PURCHASE_FIRST'; awardOrderStatus:'COMPLETED'|'DELIVERED'; redemptionCoins:number; redemptionValue:number; minimumRedemptionCoins:number; maximumRedemptionCoins?:number|null; allowWithOtherDiscounts:boolean; restoreRedeemedOnCancel:boolean; restoreRedeemedOnRefund:boolean; productRules:ProductCoinRule[]; categoryRules:CategoryCoinRule[]; }
export interface CoinTransaction { coinTransactionId:string; transactionType:string; coins:number; sourceType:string; sourceId?:string; rupeeValue?:number; description?:string; createdOn:string; }
export interface CoinWallet { customerId:string; availableCoins:number; totalEarned:number; totalRedeemed:number; transactions:CoinTransaction[]; }
@Injectable({providedIn:'root'})
export class LoyaltyApiService {
  constructor(private readonly http:HttpClient){}
  configuration(){return this.http.get<CoinConfiguration>('/api/loyalty/configuration');}
  save(value:CoinConfiguration){return this.http.put<CoinConfiguration>('/api/loyalty/configuration',value);}
  wallet(customerId:string,take=100){return this.http.get<CoinWallet>(`/api/loyalty/customers/${customerId}/wallet`,{params:{take}});}
}
