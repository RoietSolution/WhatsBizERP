import { of, throwError } from 'rxjs';
import { DemoProduct, DemoSetup, WhatsAppCommerceDemoApiService } from './whatsapp-commerce-demo-api.service';
import { WhatsAppCommerceDemoComponent } from './whatsapp-commerce-demo.component';

describe('WhatsAppCommerceDemoComponent', () => {
  function product(overrides: Partial<DemoProduct> = {}): DemoProduct {
    return {
      productId: 'product-1', productCode: 'ERP-001', barcode: '890100000001',
      productName: 'Soan Papdi', sellingPrice: 50, mrp: 55, taxPercentage: 5,
      availableQuantity: 8, categoryId: 'category-1', categoryName: 'Grocery',
      brandName: 'KhataDhari Foods', unitName: 'Piece', ...overrides,
    };
  }

  function setupData(mode = 'META_TEST', products = [product()]): DemoSetup {
    return {
      providerMode: mode, storeName: 'GuturGo Grocery', products,
      customers: [{ customerId: 'customer-1', customerCode: 'C1', customerName: 'Customer' }],
      warehouses: [{ warehouseId: 'warehouse-1', warehouseCode: 'W1', warehouseName: 'Main' }],
      categories: [{ categoryId: 'category-1', categoryName: 'Grocery', productCount: products.length }],
      collections: [], messages: [],
    };
  }

  function create(mode = 'META_TEST', products = [product()], imageFails = false) {
    const api = jasmine.createSpyObj<WhatsAppCommerceDemoApiService>('WhatsAppCommerceDemoApiService', [
      'readiness', 'setup', 'productImageUrl', 'cart', 'wallet', 'orders', 'order',
      'notifications', 'orderDetails', 'printReceipt', 'analytics', 'updateDelivery',
    ]);
    api.readiness.and.returnValue(of({ providerMode: mode, ready: true, checks: [] }));
    api.setup.and.returnValue(of(setupData(mode, products)));
    api.productImageUrl.and.returnValue(imageFails ? throwError(() => new Error('missing')) : of(new Blob()));
    api.wallet.and.returnValue(of({ customerId: 'customer-1', availableCoins: 0, totalEarned: 0, totalRedeemed: 0, transactions: [] }));
    api.orders.and.returnValue(of([]));
    api.analytics.and.returnValue(of(void 0));
    const component = new WhatsAppCommerceDemoComponent(api);
    return { api, component };
  }

  it('loads the product grid exclusively from the tenant API response', () => {
    const serverProducts = [product(), product({ productId: 'product-2', productName: 'Tea' })];
    const { api, component } = create('META_TEST', serverProducts);

    expect(api.setup).toHaveBeenCalledTimes(1);
    expect(component.setup()?.products.map(x => x.productName)).toEqual(['Soan Papdi', 'Tea']);
  });

  it('removes an unavailable ERP image URL so the template renders its fallback', () => {
    const { component } = create('META_TEST', [product({ imageUrl: '/api/products/product-1/image' })], true);

    expect(component.setup()?.products[0].imageUrl).toBeUndefined();
  });

  it('does not add an out-of-stock product', () => {
    const unavailable = product({ availableQuantity: 0 });
    const { component } = create('MOCK', [unavailable]);

    component.add(unavailable);

    expect(component.cartCount()).toBe(0);
  });

  it('uses compact quantity actions and never exceeds ERP availability', () => {
    const limited = product({ availableQuantity: 2 });
    const { api, component } = create('MOCK', [limited]);
    api.cart.and.returnValue(of({ warehouseId: 'warehouse-1', items: [], subtotal: 0, taxAmount: 0, grandTotal: 0 }));

    component.add(limited);
    component.quantity(limited.productId, 1);
    component.quantity(limited.productId, 1);

    expect(component.quantityFor(limited.productId)).toBe(2);
  });

  it('keeps server-calculated cart totals', () => {
    const { api, component } = create('MOCK');
    api.cart.and.returnValue(of({ warehouseId: 'warehouse-1', items: [], subtotal: 50, taxAmount: 2.5, grandTotal: 52.5 }));

    component.add(product());

    expect(component.cart()?.grandTotal).toBe(52.5);
  });

  it('submits checkout through the existing order API', () => {
    const { api, component } = create('META_TEST');
    api.cart.and.returnValue(of({ warehouseId: 'warehouse-1', items: [], subtotal: 50, taxAmount: 2.5, grandTotal: 52.5 }));
    api.order.and.returnValue(of({ orderId: 'order-1', orderNumber: 'INV-1', erpStatus: 'HELD', grandTotal: 52.5, redeemedCoins: 0, coinDiscount: 0, messages: [] }));
    component.add(product());
    component.deliveryAddress = 'Main Road';
    component.selectFulfillment('WALK_IN');
    component.selectCheckoutPayment('COD');

    component.place();

    expect(api.order).toHaveBeenCalledWith('customer-1', 'warehouse-1', [{ productId: 'product-1', quantity: 1 }], 'Main Road', 'WALK_IN', 'COD', 0);
  });

  it('identifies META_TEST without changing MOCK behavior', () => {
    expect(create('META_TEST').component.isMetaTest()).toBeTrue();
    expect(create('MOCK').component.isMetaTest()).toBeFalse();
  });
});
