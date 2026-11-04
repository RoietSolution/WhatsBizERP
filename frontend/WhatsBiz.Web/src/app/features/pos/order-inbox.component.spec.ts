import { HttpErrorResponse } from '@angular/common/http';
import { of, Subject, throwError } from 'rxjs';
import { IntakeDetail, IntakeQuote, IntakeSummary, OrderIntakeApiService } from './order-intake-api.service';
import { OrderInboxComponent } from './order-inbox.component';

describe('OrderInboxComponent lifecycle loading', () => {
  const request = (id: string): IntakeDetail => ({
    id, reference: `REQ-${id}`, sourceChannel: 'PHONE_CALL', status: 'UNDER_REVIEW', customerName: 'Test', customerMobile: null,
    createdAtUtc: '2026-10-28T00:00:00Z', itemCount: 0, unmatchedItemCount: 0, originalRequest: 'List', fulfilmentPreference: 'PICKUP',
    version: 'AQIDBAUGBwg=', items: [], history: [],
  });
  const row = (id: string): IntakeSummary => ({
    id, reference: `REQ-${id}`, sourceChannel: 'PHONE_CALL', status: 'UNDER_REVIEW', customerName: 'Test', customerMobile: null,
    createdAtUtc: '2026-10-28T00:00:00Z', itemCount: 0, unmatchedItemCount: 0,
  });
  const quote: IntakeQuote = {
    id: 'q1', revision: 1, status: 'CURRENT', fulfilmentPreference: 'PICKUP', createdAtUtc: '2026-10-28T00:00:00Z',
    expiresAtUtc: '2026-10-29T00:00:00Z', pricing: { merchandiseSubtotal: 0, merchandiseAmount: 0, merchandiseTaxAmount: 0,
      includedTaxAmount: 0, additionalTaxAmount: 0, productSavings: 0, promotionDiscount: 0, deliveryCharge: 0, finalPayableAmount: 0 }, items: [],
  };
  function setup() {
    const api = jasmine.createSpyObj<OrderIntakeApiService>('OrderIntakeApiService', [
      'list', 'get', 'savedAddresses', 'getQuote', 'getSalesOrder', 'saveDeliveryDetails',
    ]);
    api.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 25 }));
    api.get.and.callFake((id: string) => of(request(id)));
    api.savedAddresses.and.returnValue(of([]));
    api.getQuote.and.returnValue(of(quote));
    api.getSalesOrder.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    return { api, component: new OrderInboxComponent(api) };
  }

  it('treats absent quote and Sales Order as normal not-yet-created states', () => {
    const { api, component } = setup();
    api.getQuote.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    component.open(row('a'));
    expect(component.quoteState()).toBe('not-generated');
    expect(component.salesOrderState()).toBe('not-created');
    expect(component.quoteError()).toBe('');
    expect(component.salesOrderError()).toBe('');
  });

  it('keeps auth, forbidden, server and network failures distinct', () => {
    for (const status of [401, 403, 500, 0]) {
      const { api, component } = setup();
      api.getQuote.and.returnValue(throwError(() => new HttpErrorResponse({ status })));
      component.open(row(`e${status}`));
      expect(component.quoteState()).toBe('error');
      expect(component.quoteError()).toContain(status === 0 ? 'HTTP 0' : `HTTP ${status}`);
      if (status === 401 || status === 403) {
        expect(component.salesOrderState()).toBe('not-checked');
        expect(api.getSalesOrder).not.toHaveBeenCalled();
      } else {
        expect(component.salesOrderState()).toBe('not-created');
      }
    }
  });

  it('ignores lifecycle responses for a request that is no longer selected', () => {
    const { api, component } = setup();
    const stale = new Subject<IntakeQuote>();
    api.getQuote.and.callFake((id: string) => id === 'a' ? stale : throwError(() => new HttpErrorResponse({ status: 404 })));
    component.open(row('a'));
    component.open(row('b'));
    stale.next(quote);
    expect(component.selected()?.id).toBe('b');
    expect(component.quote()).toBeNull();
    expect(component.quoteState()).toBe('not-generated');
    expect(api.getSalesOrder).toHaveBeenCalledOnceWith('b');
  });

  it('reflects saved delivery details and marks the prior quotation invalid without refetching lifecycle', () => {
    const { api, component } = setup();
    const current = request('a');
    const updated = { ...current, fulfilmentPreference: 'DELIVERY', deliveryAddress: {
      recipientName: 'Customer', mobile: '9876543210', houseStreet: '12 Main Road', locality: 'Market', city: 'City',
      state: 'State', pincode: '123456', savedAddressId: 'address-1',
    } };
    component.selected.set(current);
    component.deliveryAddress = { fulfilmentPreference: 'DELIVERY', ...updated.deliveryAddress };
    component.quote.set(quote); component.quoteState.set('available');
    api.saveDeliveryDetails.and.returnValue(of(updated));
    component.saveDeliveryDetails();
    expect(component.selected()?.deliveryAddress?.savedAddressId).toBe('address-1');
    expect(component.deliveryAddress.pincode).toBe('123456');
    expect(component.quote()).toBeNull();
    expect(component.quoteState()).toBe('invalidated');
    expect(api.getQuote).not.toHaveBeenCalled();
    expect(api.getSalesOrder).not.toHaveBeenCalled();
  });
});
