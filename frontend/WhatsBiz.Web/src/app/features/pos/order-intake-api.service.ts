import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface IntakeItem {
  id: string; requestedText: string; requestedQuantity?: number | null; requestedUnit?: string | null;
  matchedProductId?: string | null; matchStatus: string; productCode?: string | null; productName?: string | null;
  currentSellingPrice?: number | null; currentStock?: number | null; notes?: string | null;
  customerApprovedSubstitution: boolean; proposedSubstitution: boolean;
}
export interface IntakeSummary {
  id: string; reference: string; sourceChannel: string; status: string; customerName?: string | null;
  customerMobile?: string | null; createdAtUtc: string; lastInteractionAtUtc?: string | null;
  assignedStaffId?: string | null; assignedStaffName?: string | null; itemCount: number; unmatchedItemCount: number;
}
export interface IntakeDetail extends IntakeSummary {
  originalRequest: string; notes?: string | null; fulfilmentPreference?: string | null; requestedAtUtc?: string | null;
  deliveryAddress?: IntakeDeliveryAddress | null;
  confirmationChannel?: string | null; customerConfirmedAtUtc?: string | null; version: string; items: IntakeItem[];
  history: { eventType: string; previousStatus?: string; newStatus?: string; actor?: string; note?: string; createdAtUtc: string }[];
}
export interface IntakeDeliveryAddress {
  recipientName: string; mobile: string; houseStreet: string; locality: string; city: string; state: string;
  pincode: string; landmark?: string | null; deliveryInstructions?: string | null; savedAddressId?: string | null;
}
export interface IntakeAddressForm extends IntakeDeliveryAddress { fulfilmentPreference: string; }
export interface IntakeSavedAddress {
  addressId: string; recipientName: string; mobile: string; addressLine1: string; addressLine2?: string | null;
  landmark?: string | null; city: string; state: string; pincode: string; addressType: string; isDefault: boolean;
}
export interface IntakeCustomerOption { customerId: string; customerCode: string; customerName: string; }
export interface IntakePage { items: IntakeSummary[]; totalCount: number; page: number; pageSize: number; }
export interface IntakeProductMatch { productId: string; productCode: string; productName: string; sellingPrice: number; currentStock: number; }
export interface IntakeQuote {
  id: string; revision: number; status: string; fulfilmentPreference: string; pincode?: string | null;
  deliveryAddress?: IntakeDeliveryAddress | null;
  createdAtUtc: string; expiresAtUtc: string;
  pricing: { merchandiseSubtotal: number; merchandiseAmount: number; merchandiseTaxAmount: number; includedTaxAmount: number;
    additionalTaxAmount: number; productSavings: number; promotionDiscount: number; promotionName?: string | null;
    deliveryCharge: number; finalPayableAmount: number; };
  items: { productId: string; productCode: string; productName: string; requestedText: string; isSubstitution: boolean;
    unitName: string; unitShortName?: string | null; quantity: number; requestedUnit?: string | null; unitPrice: number;
    taxPercentage: number; gstPricingType: string; }[];
}
export interface IntakeSalesOrder {
  id: string; reference: string; requestId: string; quoteId: string; quoteRevision: number; status: string;
  fulfilmentPreference: string; payableAmount: number; createdAtUtc: string; version: string;
  deliveryAddress?: IntakeDeliveryAddress | null;
  history: { eventType: string; previousStatus?: string | null; newStatus?: string | null; actor?: string | null; note?: string | null; createdAtUtc: string }[];
}

@Injectable({ providedIn: 'root' })
export class OrderIntakeApiService {
  private readonly base = '/api/order-intake';
  constructor(private readonly http: HttpClient) {}
  list(filters: { search?: string; channel?: string; status?: string; from?: string; to?: string; page?: number; pageSize?: number }): Observable<IntakePage> {
    let params = new HttpParams();
    Object.entries(filters).forEach(([key, value]) => { if (value !== undefined && value !== '') params = params.set(key, String(value)); });
    return this.http.get<IntakePage>(this.base, { params });
  }
  get(id: string): Observable<IntakeDetail> { return this.http.get<IntakeDetail>(`${this.base}/${id}`); }
  create(input: { sourceChannel: string; customerName?: string; customerMobile?: string; originalRequest: string; notes?: string; fulfilmentPreference?: string; deliveryAddress?: IntakeDeliveryAddress | null }): Observable<IntakeDetail> {
    return this.http.post<IntakeDetail>(this.base, input);
  }
  assign(id: string, version: string): Observable<IntakeDetail> { return this.http.post<IntakeDetail>(`${this.base}/${id}/assign-to-me`, JSON.stringify(version)); }
  matches(query: string): Observable<IntakeProductMatch[]> { return this.http.get<IntakeProductMatch[]>(`${this.base}/matches`, { params: { query } }); }
  updateItem(id: string, itemId: string, input: object): Observable<IntakeDetail> { return this.http.put<IntakeDetail>(`${this.base}/${id}/items/${itemId}`, input); }
  setStatus(id: string, input: object): Observable<IntakeDetail> { return this.http.put<IntakeDetail>(`${this.base}/${id}/status`, input); }
  saveDeliveryDetails(id: string, input: { details: IntakeAddressForm; version: string }): Observable<IntakeDetail> { return this.http.put<IntakeDetail>(`${this.base}/${id}/delivery-details`, input); }
  savedAddresses(id: string): Observable<IntakeSavedAddress[]> { return this.http.get<IntakeSavedAddress[]>(`${this.base}/${id}/saved-addresses`); }
  searchCustomers(query: string): Observable<IntakeCustomerOption[]> { return this.http.get<IntakeCustomerOption[]>(`${this.base}/customers/search`, { params: { query } }); }
  linkVerifiedCustomer(id: string, input: { customerId: string; verificationMethod: string; staffAcknowledged: boolean; version: string }): Observable<IntakeDetail> {
    return this.http.put<IntakeDetail>(`${this.base}/${id}/verified-customer`, input);
  }
  createQuote(id: string, input: object): Observable<IntakeQuote> { return this.http.post<IntakeQuote>(`${this.base}/${id}/quote`, input); }
  getQuote(id: string): Observable<IntakeQuote> { return this.http.get<IntakeQuote>(`${this.base}/${id}/quote`); }
  markConfirmationRequested(id: string, input: object): Observable<IntakeDetail> { return this.http.post<IntakeDetail>(`${this.base}/${id}/confirmation-requested`, input); }
  recordConfirmation(id: string, input: object): Observable<IntakeDetail> { return this.http.post<IntakeDetail>(`${this.base}/${id}/confirmation`, input); }
  convertToSalesOrder(id: string, input: object): Observable<IntakeSalesOrder> { return this.http.post<IntakeSalesOrder>(`${this.base}/${id}/sales-order`, input); }
  getSalesOrder(id: string): Observable<IntakeSalesOrder> { return this.http.get<IntakeSalesOrder>(`${this.base}/${id}/sales-order`); }
  transitionSalesOrder(id: string, input: object): Observable<IntakeSalesOrder> { return this.http.put<IntakeSalesOrder>(`${this.base}/${id}/sales-order/status`, input); }
}
