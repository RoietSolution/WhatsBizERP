import { of, throwError } from 'rxjs';
import { POSApiService } from './pos-api.service';
import { POSProduct } from './pos.models';
import { POSScreenComponent } from './pos-screen.component';

describe('POSScreenComponent barcode flow', () => {
  function setup() {
    const api = jasmine.createSpyObj<POSApiService>('POSApiService', [
      'methods',
      'warehouses',
      'products',
      'customers',
      'quickCustomer',
      'invoice',
      'hold',
      'print',
    ]);
    api.methods.and.returnValue(of([]));
    api.warehouses.and.returnValue(
      of([{ warehouseId: 'warehouse-1', warehouseName: 'Main', isDefault: true }]),
    );
    const snack = jasmine.createSpyObj('MatSnackBar', ['open']);
    const dialog = jasmine.createSpyObj('MatDialog', ['open']);
    const router = jasmine.createSpyObj('Router', ['navigate']);
    const component = new POSScreenComponent(api, dialog, snack, router);
    return { api, component, snack };
  }

  it('adds an exact camera barcode match and increments the same cart line on a later scan', () => {
    const { api, component } = setup();
    api.products.and.returnValue(of([product()]));

    component.cameraBarcode('8901234567890');
    component.cameraBarcode('8901234567890');

    expect(api.products).toHaveBeenCalledWith(undefined, '8901234567890', 'warehouse-1', 1);
    expect(component.cart()).toHaveSize(1);
    expect(component.cart()[0].quantity).toBe(2);
    expect(component.scannerFeedback()).toContain('added to cart');
  });

  it('keeps the cart unchanged for an unknown barcode', () => {
    const { api, component, snack } = setup();
    api.products.and.returnValue(of([]));

    component.cameraBarcode('UNKNOWN');

    expect(component.cart()).toEqual([]);
    expect(component.scannerFeedback()).toContain('No active product found');
    expect(snack.open).toHaveBeenCalled();
  });

  it('does not add beyond available stock', () => {
    const { component, snack } = setup();
    const unavailable = product({ availableQuantity: 0 });

    expect(component.add(unavailable)).toBeFalse();

    expect(component.cart()).toEqual([]);
    expect(snack.open).toHaveBeenCalledWith(
      'Stock unavailable for Test product.',
      undefined,
      jasmine.any(Object),
    );
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

    expect(api.products).toHaveBeenCalledWith('Test', undefined, 'warehouse-1');
    expect(component.products()).toHaveSize(1);
  });

  it('looks up manufacturer QR content exactly through the existing POS API', () => {
    const { api, component } = setup();
    const qr = 'https://manufacturer.example/products/ABC?lot=26';
    api.products.and.returnValue(of([product({ barcode: qr })]));

    component.cameraBarcode(qr);

    expect(api.products).toHaveBeenCalledWith(undefined, qr, 'warehouse-1', 1);
    expect(component.cart()).toHaveSize(1);
  });

  function product(overrides: Partial<POSProduct> = {}): POSProduct {
    return {
      productId: 'product-1',
      productCode: 'P-001',
      barcode: '8901234567890',
      productName: 'Test product',
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
