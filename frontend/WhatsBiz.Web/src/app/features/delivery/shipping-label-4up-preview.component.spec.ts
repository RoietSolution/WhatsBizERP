import { ShippingLabel4UpPreviewComponent } from './shipping-label-4up-preview.component';
import { StorefrontShippingLabel } from './shipping-label.models';

describe('ShippingLabel4UpPreviewComponent', () => {
  const label = (id: string): StorefrontShippingLabel => ({
    id, orderNumber: `POS-${id}`, orderDate: '2026-10-14T00:00:00Z', customerName: 'Customer',
    deliveryAddress: 'Address', paymentMethod: 'COD', paymentStatus: 'COD_PENDING', paymentDue: true,
    dueAmount: 359, storeName: 'Store', senderAddress: 'Sender', fulfillmentMethod: 'RETAILER_DELIVERY',
  });

  it('paginates A4 four-up as 4, 4, and 1 labels', () => {
    const component = new ShippingLabel4UpPreviewComponent();
    component.format = 'A4_4UP';
    component.orders = [label('1'), label('2'), label('3'), label('4'), label('5'), label('6'), label('7'), label('8'), label('9')];
    expect(component.labelsPerPage()).toBe(4);
    expect(component.pageCount()).toBe(3);
  });

  it('keeps pickup and courier labels distinct', () => {
    const component = new ShippingLabel4UpPreviewComponent();
    const pickup = label('pickup'); pickup.fulfillmentMethod = 'WALK_IN';
    const courier = label('courier'); courier.fulfillmentMethod = 'COURIER'; courier.trackingNumber = 'AWB-1';
    expect(component.deliveryMethod(pickup)).toBe('Store Pickup');
    expect(component.deliveryMethod(courier)).toBe('Courier Delivery');
    expect(component.paymentIndicator(label('cod'))).toContain('COD TO COLLECT');
    const paid = label('paid'); paid.paymentDue = false; paid.paymentCollected = true; paid.collectedAmount = 359;
    expect(component.paymentIndicator(paid)).toContain('COD PAID');
  });
});
