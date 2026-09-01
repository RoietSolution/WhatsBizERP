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

  it('requires a reference before adding UPI, card, or wallet payments', () => {
    for (const method of ['UPI', 'CARD', 'WALLET']) {
      const { component } = create(method, true);
      component.add();
      expect(component.payments()).withContext(method).toEqual([]);
      component.reference = `${method}-TXN`;
      component.add();
      expect(component.payments()[0].methodCode).withContext(method).toBe(method);
    }
  });

  it('records credit as an outstanding balance rather than a payment', () => {
    const { component, ref } = create('CREDIT', true);

    component.complete();

    const result = ref.close.calls.mostRecent().args[0] as PaymentResult;
    expect(result.isCreditSale).toBeTrue();
    expect(result.payments).toEqual([]);
  });

  it('does not allow credit without a selected customer', () => {
    const { component, ref } = create('CREDIT', false);

    component.complete();

    expect(ref.close).not.toHaveBeenCalled();
    expect(component.canComplete()).toBeFalse();
  });

  function create(preferredMethod: string, hasCustomer: boolean) {
    const ref = jasmine.createSpyObj('MatDialogRef', ['close']);
    const component = new PaymentDialogComponent(
      { total: 118, methods, preferredMethod, hasCustomer },
      ref,
    );
    return { component, ref };
  }
});
