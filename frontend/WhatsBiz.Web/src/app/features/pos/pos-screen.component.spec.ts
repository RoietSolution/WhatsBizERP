import { EMPTY, of, throwError } from 'rxjs';
import { POSApiService } from './pos-api.service';
import { PaymentMethod, POSProduct } from './pos.models';
import { POSScreenComponent } from './pos-screen.component';
import { ProductAddedSoundService } from './product-added-sound.service';

describe('POSScreenComponent barcode flow', () => {
  function setup(paymentMethods: PaymentMethod[] = []) {
    const api = jasmine.createSpyObj<POSApiService>('POSApiService', [
      'methods',
      'warehouses',
      'categories',
      'brands',
      'products',
      'productImage',
      'customers',
      'quickCustomer',
      'invoice',
      'hold',
      'print',
    ]);
    api.methods.and.returnValue(of(paymentMethods));
    api.warehouses.and.returnValue(
      of([{ warehouseId: 'warehouse-1', warehouseName: 'Main', isDefault: true }]),
    );
    api.categories.and.returnValue(of([]));
    api.brands.and.returnValue(of([]));
    api.productImage.and.returnValue(EMPTY);
    const snack = jasmine.createSpyObj('MatSnackBar', ['open']);
    const dialog = jasmine.createSpyObj('MatDialog', ['open']);
    const router = jasmine.createSpyObj('Router', ['navigate']);
    const sound = jasmine.createSpyObj<ProductAddedSoundService>('ProductAddedSoundService', [
      'unlock',
      'play',
    ]);
    const component = new POSScreenComponent(api, dialog, snack, router, sound);
    return { api, component, dialog, snack, sound };
  }

  it('adds an exact camera barcode match and increments the same cart line on a later scan', () => {
    const { api, component, sound } = setup();
    api.products.and.returnValue(of([product()]));

    component.cameraBarcode('8901234567890');
    component.cameraBarcode('8901234567890');

    expect(api.products).toHaveBeenCalledWith(undefined, '8901234567890', 'warehouse-1', 1);
    expect(component.cart()).toHaveSize(1);
    expect(component.cart()[0].quantity).toBe(2);
    expect(component.scannerFeedback()).toContain('added to cart');
    expect(sound.play).toHaveBeenCalledTimes(2);
  });

  it('keeps the cart unchanged for an unknown barcode', () => {
    const { api, component, snack, sound } = setup();
    api.products.and.returnValue(of([]));

    component.cameraBarcode('UNKNOWN');

    expect(component.cart()).toEqual([]);
    expect(component.scannerFeedback()).toContain('No active product found');
    expect(snack.open).toHaveBeenCalled();
    expect(sound.play).not.toHaveBeenCalled();
  });

  it('does not add beyond available stock', () => {
    const { component, snack, sound } = setup();
    const unavailable = product({ availableQuantity: 0 });

    expect(component.add(unavailable)).toBeFalse();

    expect(component.cart()).toEqual([]);
    expect(snack.open).toHaveBeenCalledWith(
      'Stock unavailable for Test product.',
      undefined,
      jasmine.any(Object),
    );
    expect(sound.play).not.toHaveBeenCalled();
  });

  it('preserves the cart when the barcode network lookup fails', () => {
    const { api, component } = setup();
    component.add(product());
    api.products.and.returnValue(throwError(() => new Error('offline')));

    component.cameraBarcode('NETWORK-FAILURE');

    expect(component.cart()).toHaveSize(1);
    expect(component.scannerFeedback()).toContain('lookup failed');
  });

  it('keeps existing manual product search behavior', () => {
    const { api, component } = setup();
    api.products.and.returnValue(of([product()]));
    component.search = 'Test';

    component.findProducts();

    expect(api.products).toHaveBeenCalledWith(
      'Test', undefined, 'warehouse-1', undefined, undefined, undefined,
    );
    expect(component.products()).toHaveSize(1);
  });

  it('loads products when a category or brand filter is selected', () => {
    const { api, component } = setup();
    api.products.and.returnValue(of([product()]));
    component.categoryId = 'category-1';
    component.brandId = 'brand-1';

    component.findProducts();

    expect(api.products).toHaveBeenCalledWith(
      undefined, undefined, 'warehouse-1', undefined, 'category-1', 'brand-1',
    );
    expect(component.products()).toHaveSize(1);
  });

  it('loads a product thumbnail only once when an item is added repeatedly', () => {
    const { api, component } = setup();

    component.add(product());
    component.add(product());

    expect(api.productImage).toHaveBeenCalledOnceWith('product-1');
  });

  it('asks for confirmation before removing a bill item', () => {
    const { component, dialog } = setup();
    component.add(product());
    dialog.open.and.returnValue({ afterClosed: () => of(false) } as never);

    component.remove(component.cart()[0]);

    expect(component.cart()).toHaveSize(1);
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as never);
    component.remove(component.cart()[0]);
    expect(component.cart()).toEqual([]);
  });

  it('looks up manufacturer QR content exactly through the existing POS API', () => {
    const { api, component } = setup();
    const qr = 'https://manufacturer.example/products/ABC?lot=26';
    api.products.and.returnValue(of([product({ barcode: qr })]));

    component.cameraBarcode(qr);

    expect(api.products).toHaveBeenCalledWith(undefined, qr, 'warehouse-1', 1);
    expect(component.cart()).toHaveSize(1);
  });

  it('orders shortcuts as New Bill, Customer, Payment, Hold, and Print first', () => {
    const { component } = setup();

    expect(component.shortcuts.slice(0, 5).map((x) => x.label)).toEqual([
      'New Bill', 'Customer', 'Payment', 'Hold', 'Print',
    ]);
  });

  it('keeps only Cash and UPI available for sale settlement', () => {
    const { component } = setup([
      { paymentMethodId: 'cash', methodCode: 'CASH', methodName: 'Cash', requiresReference: false },
      { paymentMethodId: 'upi', methodCode: 'UPI', methodName: 'UPI', requiresReference: true },
      { paymentMethodId: 'card', methodCode: 'CARD', methodName: 'Card', requiresReference: true },
      { paymentMethodId: 'wallet', methodCode: 'WALLET', methodName: 'Wallet', requiresReference: true },
      { paymentMethodId: 'credit', methodCode: 'CREDIT', methodName: 'Credit', requiresReference: false },
    ]);

    expect(component.methods().map((method) => method.methodCode)).toEqual(['CASH', 'UPI']);
  });

  function product(overrides: Partial<POSProduct> = {}): POSProduct {
    return {
      productId: 'product-1',
      productCode: 'P-001',
      barcode: '8901234567890',
      productName: 'Test product',
      categoryId: 'category-1',
      brandId: 'brand-1',
      sellingPrice: 100,
      mrp: 110,
      gstPercentage: 18,
      isBatchManaged: false,
      isSerialManaged: false,
      availableQuantity: 10,
      negativeStockAllowed: false,
      ...overrides,
    };
  }
});
