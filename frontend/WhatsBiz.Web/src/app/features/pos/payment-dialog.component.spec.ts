import { fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { POSApiService } from './pos-api.service';
import { PaymentDialogComponent, PaymentResult } from './payment-dialog.component';
import { PaymentMethod } from './pos.models';

describe('PaymentDialogComponent', () => {
  const methods: PaymentMethod[] = [
    { paymentMethodId: 'cash', methodCode: 'CASH', methodName: 'Cash', requiresReference: false },
    { paymentMethodId: 'upi', methodCode: 'UPI', methodName: 'UPI', requiresReference: true },
    { paymentMethodId: 'card', methodCode: 'CARD', methodName: 'Card', requiresReference: true },
    { paymentMethodId: 'wallet', methodCode: 'WALLET', methodName: 'Wallet', requiresReference: true },
    { paymentMethodId: 'credit', methodCode: 'CREDIT', methodName: 'Credit', requiresReference: false },
  ];

  it('keeps only Cash and UPI as tender lines and marks Split as a workflow', () => {
    const { component } = create('SPLIT');

    expect(component.visibleMethods.map((method) => method.methodCode)).toEqual(['CASH', 'UPI']);
    expect(component.splitPayment).toBeTrue();
    expect(component.method).toBe('CASH');
  });

  it('requires a reference before adding a UPI payment', fakeAsync(() => {
    const { component } = create('UPI');
    tick(200);

    component.add();
    expect(component.payments()).toEqual([]);

    component.reference = 'UPI-TXN-1';
    component.add();
    expect(component.payments()).toEqual([
      { methodCode: 'UPI', amount: 118, referenceNumber: 'UPI-TXN-1' },
    ]);
  }));

  it('loads a payable QR for the selected UPI amount', fakeAsync(() => {
    const { component, api } = create('UPI');

    tick(200);

    expect(api.upiQr).toHaveBeenCalledWith(118);
    expect(component.upiQrUrl()).toBe('data:image/svg+xml;base64,PHN2Zy8+');
    expect(component.upiQrError()).toBe('');
  }));

  it('shows a friendly message when retailer UPI configuration is missing', fakeAsync(() => {
    const { component, api } = create('UPI');
    api.upiQr.and.returnValue(throwError(() => new Error('not configured')));
    component.paymentAmountChanged();

    tick(200);

    expect(component.upiQrUrl()).toBe('');
    expect(component.upiQrError()).toContain('POS UPI ID');
  }));

  it('completes only after Cash and/or UPI lines cover the bill', () => {
    const { component, ref } = create('CASH');
    expect(component.canComplete()).toBeFalse();

    component.add();
    expect(component.canComplete()).toBeTrue();
    component.complete();

    const result = ref.close.calls.mostRecent().args[0] as PaymentResult;
    expect(result.isCreditSale).toBeFalse();
    expect(result.payments).toEqual([{ methodCode: 'CASH', amount: 118, referenceNumber: undefined }]);
  });

  function create(preferredMethod: string) {
    const ref = jasmine.createSpyObj('MatDialogRef', ['close']);
    const api = jasmine.createSpyObj<POSApiService>('POSApiService', ['upiQr']);
    api.upiQr.and.returnValue(
      of({
        qrCodeDataUrl: 'data:image/svg+xml;base64,PHN2Zy8+',
        upiId: 'shop@upi',
        payeeName: 'Demo Shop',
        amount: 118,
      }),
    );
    const component = new PaymentDialogComponent(
      { total: 118, methods, preferredMethod, hasCustomer: true },
      ref,
      api,
    );
    return { component, ref, api };
  }
});
