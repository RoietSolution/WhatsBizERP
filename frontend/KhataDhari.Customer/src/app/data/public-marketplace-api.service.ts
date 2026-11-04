import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface PublicRouteResolution { slug:string; type:'CITY'|'RETAILER'; storeKey?:string; cityId?:string; canonicalPath:string; }
export interface CityInfo { cityId:string; cityName:string; stateName:string; marketplaceName:string; description?:string|null; bannerHeadline?:string|null; bannerSubtitle?:string|null; slug:string; seoTitle?:string|null; seoDescription?:string|null; logoUrl?:string|null; bannerUrl?:string|null; }
export interface CityStore { storeKey:string; storeName:string; businessType:string; shortDescription:string; locality?:string|null; logoUrl?:string|null; }
export interface CityProduct { id:string; operatingTenantId:string; operatingStoreKey:string; operatingStoreName:string; categoryId:string; categoryName:string; brandId:string; brandName:string; name:string; description:string; imageUrl?:string|null; sellingPrice:number; compareAtPrice?:number|null; gstPercentage:number; gstPricingType:string; availability:string; canOrderNow:boolean; availabilityMessage?:string|null; isPurchasable:boolean; notPurchasableReason?:string|null; partnerSellerId?:string|null; sellerName?:string|null; }
export interface CityCategoryFacet { categoryKey:string; categoryName:string; productCount:number; imageUrl?:string|null; hasCityImageOverride:boolean; }
export interface CityProductPage { items:CityProduct[]; pageNumber:number; pageSize:number; totalCount:number; hasMore:boolean; categories:CityCategoryFacet[]; brands:Array<{brandKey:string;brandName:string}>; }

@Injectable({providedIn:'root'})
export class PublicMarketplaceApiService {
  constructor(private readonly http:HttpClient){}
  resolve(slug:string):Promise<PublicRouteResolution|null>{return firstValueFrom(this.http.get<PublicRouteResolution>(`${environment.apiBaseUrl}/api/public/routes/${encodeURIComponent(slug)}`,{observe:'response'})).then(r=>r.body).catch((e)=>e?.status===404?null:Promise.reject(e));}
  city(slug:string):Promise<{city:CityInfo;stores:CityStore[]}>{return firstValueFrom(this.http.get<{city:CityInfo;stores:CityStore[]}>(`${environment.apiBaseUrl}/api/public/cities/${encodeURIComponent(slug)}`));}
  products(slug:string,options:{page:number;pageSize:number;search?:string;searchEventId?:string;category?:string;brand?:string}):Promise<CityProductPage>{return firstValueFrom(this.http.get<CityProductPage>(`${environment.apiBaseUrl}/api/public/cities/${encodeURIComponent(slug)}/products`,{params:{page:options.page,pageSize:options.pageSize,...(options.search?{search:options.search}:{}),...(options.searchEventId?{searchEventId:options.searchEventId}:{}),...(options.category?{category:options.category}:{}),...(options.brand?{brand:options.brand}:{})}}));}
  asset(url?:string|null):string{return !url?'':url.startsWith('http')?url:environment.apiBaseUrl+url;}
}
