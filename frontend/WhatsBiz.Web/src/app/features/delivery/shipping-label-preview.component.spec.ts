import { ShippingLabelPreviewComponent } from './shipping-label-preview.component';
import { StorefrontShippingLabel } from './shipping-label.models';

describe('ShippingLabelPreviewComponent', () => {
  const label = (id: string): StorefrontShippingLabel => ({
    id, orderNumber: `ORD-${id}`, orderDate: '2026-01-01T00:00:00Z', customerName: 'Customer',
    deliveryAddress: 'Address', deliveryPincode: '560001', paymentMethod: 'COD', paymentStatus: 'PENDING',
    paymentDue: true, dueAmount: 100, storeName: 'Store', senderAddress: 'Sender'
  });

  it('keeps all selected labels and supports A4 pairing counts', () => {
    const component = new ShippingLabelPreviewComponent();
    component.orders = [label('1'), label('2'), label('3'), label('4'), label('5')];
    component.format = 'A4_DOUBLE';

    expect(component.printableOrders().length).toBe(5);
    expect(component.canPrint()).toBeTrue();
  });

  it('supports individual formats and payment indicators', () => {
    const component = new ShippingLabelPreviewComponent();
    component.orders = [label('1')];
    component.format = 'THERMAL_58';

    expect(component.formatClass()).toBe('thermal-58');
    expect(component.paymentIndicator(component.orders[0])).toBe('CASH ON DELIVERY');
    component.orders[0].paymentDue = false;
    expect(component.paymentIndicator(component.orders[0])).toBe('PREPAID / PAID');
  });
});
