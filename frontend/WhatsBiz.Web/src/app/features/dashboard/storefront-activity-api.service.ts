import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
export interface StorefrontActivityReport { summary:{visitorsToday:number;uniqueVisitors:number;signedInCustomers:number;newCustomers:number;returningCustomers:number;productViews:number;addToCarts:number;checkouts:number;orders:number}; recentActivity:{visitorLabel:string;mobile?:string;lastVisit:string;lastSignIn?:string;sessions:number;orders:number;lastActivity:string}[]; }
export interface StorefrontBehaviorEvent { eventType:string; visitorLabel:string; customerId?:string; productId?:string; productName?:string; occurredOn:string; }
export interface StorefrontWishlistActivity { customerId:string; customerName:string; productId:string; productName:string; sellingPrice:number; availability:string; savedAt:string; }
export interface StorefrontBehaviorReport { events:StorefrontBehaviorEvent[]; wishlist:StorefrontWishlistActivity[]; }
@Injectable({providedIn:'root'}) export class StorefrontActivityApiService { constructor(private readonly http:HttpClient){} report(from?:string,to?:string):Observable<StorefrontActivityReport>{return this.http.get<StorefrontActivityReport>('/api/storefront-analytics',{params:{...(from?{from}:{}),...(to?{to}:{})}});} behavior(from?:string,to?:string):Observable<StorefrontBehaviorReport>{return this.http.get<StorefrontBehaviorReport>('/api/storefront-analytics/behavior',{params:{...(from?{from}:{}),...(to?{to}:{})}});} }
