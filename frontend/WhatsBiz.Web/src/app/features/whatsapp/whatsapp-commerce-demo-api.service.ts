import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
export interface DemoMessage { sender:string; kind:string; text:string; }
export interface DemoProduct { productId:string; productCode:string; barcode?:string; productName:string; description?:string; imageUrl?:string; imageUrls?:string[]; sellingPrice:number; mrp:number; taxPercentage:number; availableQuantity:number; categoryId:string; categoryName:string; }
export interface DemoCategory { categoryId:string; categoryName:string; description?:string; productCount:number; imageProductId?:string; imageUrl?:string; }
export interface DemoCustomer { customerId:string; customerCode:string; customerName:string; mobile?:string; }
export interface DemoWarehouse { warehouseId:string; warehouseCode:string; warehouseName:string; }
export interface DemoSetup { providerMode:string; storeName:string; customers:DemoCustomer[]; warehouses:DemoWarehouse[]; categories:DemoCategory[]; products:DemoProduct[]; messages:DemoMessage[]; }
export interface CartLine extends DemoProduct { quantity:number; unitPrice:number; taxAmount:number; lineTotal:number; }
export interface DemoCart { warehouseId:string; items:CartLine[]; subtotal:number; taxAmount:number; grandTotal:number; }
export interface DemoOrder { orderId:string; orderNumber:string; erpStatus:string; grandTotal:number; messages:DemoMessage[]; }
export interface ReadinessCheck { key:string; label:string; ready:boolean; setupRoute?:string; detail?:string; }
export interface DemoReadiness { ready:boolean; checks:ReadinessCheck[]; }
export interface OrderSummary { orderId:string; orderNumber:string; orderDate:string; grandTotal:number; erpStatus:string; displayStatus:string; sourceChannel:string; providerMode:string; }
export interface OrderDetails { order:OrderSummary; items:CartLine[]; }
@Injectable({providedIn:'root'})
export class WhatsAppCommerceDemoApiService {
  private readonly root='/api/whatsapp-commerce/demo';
  constructor(private readonly http:HttpClient){}
  setup(warehouseId?:string){return this.http.get<DemoSetup>(`${this.root}/setup`,{params:warehouseId?new HttpParams().set('warehouseId',warehouseId):undefined});}
  readiness(){return this.http.get<DemoReadiness>(`${this.root}/readiness`);}
  cart(warehouseId:string,items:{productId:string;quantity:number}[]){return this.http.post<DemoCart>(`${this.root}/cart`,{warehouseId,items});}
  order(customerId:string,warehouseId:string,items:{productId:string;quantity:number}[]){return this.http.post<DemoOrder>(`${this.root}/orders`,{customerId,warehouseId,items});}
  orders(customerId:string){return this.http.get<OrderSummary[]>(`${this.root}/orders`,{params:new HttpParams().set('customerId',customerId)});}
  orderDetails(orderId:string,customerId:string){return this.http.get<OrderDetails>(`${this.root}/orders/${orderId}`,{params:new HttpParams().set('customerId',customerId)});}
  notifications(customerId:string){return this.http.post<DemoMessage[]>(`${this.root}/status-notifications`,null,{params:new HttpParams().set('customerId',customerId)});}
  productImage(productId:string){return this.http.get(`/api/products/${productId}/image`,{responseType:'blob'});}
  productImageUrl(url:string){return this.http.get(url,{responseType:'blob'});}
}
