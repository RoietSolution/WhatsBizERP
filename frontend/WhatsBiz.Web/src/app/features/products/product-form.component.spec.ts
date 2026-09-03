import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder } from '@angular/forms';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { ProductFormComponent } from './product-form.component';

describe('ProductFormComponent manufacturer codes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [FormBuilder] });
  });

  function setup() {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', [
      'categories',
      'brands',
      'units',
      'get',
      'images',
      'create',
      'update',
      'uploadImage',
      'deleteProductImage',
      'createBrand',
      'createUnit',
    ]);
    api.categories.and.returnValue(of([]));
    api.brands.and.returnValue(of([]));
    api.units.and.returnValue(of([]));
    const route = {
      snapshot: {
        paramMap: { get: () => null },
        queryParamMap: { get: () => null },
      },
    } as unknown as ActivatedRoute;
    const router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    const snack = jasmine.createSpyObj('MatSnackBar', ['open']);
    const component = TestBed.runInInjectionContext(
      () => new ProductFormComponent(api, route, router, snack),
    );
    return { component, api, snack };
  }

  it('captures a scanned EAN as the primary barcode without creating an additional row', () => {
    const { component } = setup();
    component.openScanner('primary');

    component.scanned({ value: '8901234567890', barcodeType: 'EAN13' });

    expect(component.form.controls.barcode.value).toBe('8901234567890');
    expect(component.form.controls.barcodeType.value).toBe('EAN13');
    expect(component.additionalBarcodes()).toEqual([]);
  });

  it('defaults WhatsApp Ecommerce visibility on and allows the retailer to opt out', () => {
    const { component } = setup();

    expect(component.form.controls.isWhatsAppVisible.value).toBeTrue();

    component.form.controls.isWhatsAppVisible.setValue(false);

    expect(component.form.getRawValue().isWhatsAppVisible).toBeFalse();
    expect(component.form.controls.isActive.value).toBeTrue();
  });

  it('stores exact manufacturer QR URL text without navigating to it', () => {
    const { component } = setup();
    const qr = ' https://manufacturer.example/item/ABC?lot=Lot%2026 ';
    const open = spyOn(window, 'open');
    component.openScanner('additional');

    component.scanned({ value: qr, barcodeType: 'QR' });
    expect(component.newBarcode).toBe(qr);
    expect(component.newBarcodeType).toBe('QR');
    expect(component.additionalBarcodes()).toEqual([]);

    component.addManualBarcode();

    expect(component.additionalBarcodes()).toEqual([{ barcode: qr, barcodeType: 'QR' }]);
    expect(component.newBarcode).toBe('');
    expect(component.newBarcodeType).toBe('CUSTOM');
    expect(open).not.toHaveBeenCalled();
  });

  it('does not create a duplicate additional code for the current product', () => {
    const { component, snack } = setup();
    component.additionalBarcodes.set([{ barcode: 'SAME-CODE', barcodeType: 'CODE128' }]);
    component.newBarcode = 'same-code';
    component.newBarcodeType = 'CODE128';

    component.addManualBarcode();

    expect(component.additionalBarcodes()).toHaveSize(1);
    expect(snack.open).toHaveBeenCalledWith(
      'This additional code is already linked to the current product.',
      'Dismiss',
      jasmine.any(Object),
    );
  });

  it('rejects QR content longer than the backend limit', () => {
    const { component, snack } = setup();
    component.newBarcode = 'X'.repeat(451);
    component.newBarcodeType = 'QR';

    component.addManualBarcode();

    expect(component.additionalBarcodes()).toEqual([]);
    expect(snack.open).toHaveBeenCalledWith(
      'Additional barcode/QR content cannot exceed 450 characters.',
      'Dismiss',
      jasmine.any(Object),
    );
  });

  it('keeps primary and additional scanner destinations separate', () => {
    const { component } = setup();
    component.form.patchValue({ barcode: 'PRIMARY-1', barcodeType: 'CODE128' });
    component.newBarcode = 'ADDITIONAL-DRAFT';

    component.openScanner('primary');
    component.scanned({ value: '8901234567890', barcodeType: 'EAN13' });

    expect(component.form.controls.barcode.value).toBe('8901234567890');
    expect(component.newBarcode).toBe('ADDITIONAL-DRAFT');

    component.openScanner('additional');
    component.scanned({ value: 'MANUFACTURER-2', barcodeType: 'CODE39' });

    expect(component.form.controls.barcode.value).toBe('8901234567890');
    expect(component.newBarcode).toBe('MANUFACTURER-2');
    expect(component.newBarcodeType).toBe('CODE39');
  });

  it('maps UPC variants to UPC and unknown scan formats to CUSTOM', () => {
    const { component } = setup();
    component.openScanner('additional');
    component.scanned({ value: '012345678905', barcodeType: 'UPCA' });
    expect(component.newBarcodeType).toBe('UPC');

    component.openScanner('additional');
    component.scanned({ value: 'UNMAPPED', barcodeType: 'DATA_MATRIX' });
    expect(component.newBarcodeType).toBe('CUSTOM');
  });

  it('rejects an empty additional code and a code matching the primary barcode', () => {
    const { component } = setup();
    component.addManualBarcode();
    expect(component.additionalCodeError()).toContain('Enter or scan');

    component.form.controls.barcode.setValue('PRIMARY-CODE');
    component.newBarcode = ' primary-code ';
    component.addManualBarcode();

    expect(component.additionalBarcodes()).toEqual([]);
    expect(component.additionalCodeError()).toBe(
      'Additional code must be different from the primary barcode.',
    );
  });

  it('adds and immediately selects a brand without asking for a code', () => {
    const { component, api } = setup();
    api.createBrand.and.returnValue(
      of({ brandId: 'brand-1', brandCode: 'BR-AUTO', brandName: 'New Brand', isActive: true }),
    );
    component.quickBrandName = ' New Brand ';

    component.createBrand();

    expect(api.createBrand).toHaveBeenCalledWith({
      brandCode: '',
      brandName: 'New Brand',
      description: '',
      logo: '',
      isActive: true,
    });
    expect(component.form.controls.brandId.value).toBe('brand-1');
    expect(component.brands()).toHaveSize(1);
  });

  it('adds and immediately selects a unit with an automatically derived short name', () => {
    const { component, api } = setup();
    api.createUnit.and.returnValue(
      of({
        unitId: 'unit-1',
        unitCode: 'UOM-AUTO',
        unitName: 'Packet',
        shortName: 'PACKET',
        decimalPlaces: 0,
        isActive: true,
      }),
    );
    component.quickUnitName = 'Packet';

    component.createUnit();

    expect(api.createUnit).toHaveBeenCalledWith({
      unitCode: '',
      unitName: 'Packet',
      shortName: 'PACKET',
      decimalPlaces: 0,
      isActive: true,
    });
    expect(component.form.controls.unitId.value).toBe('unit-1');
    expect(component.units()).toHaveSize(1);
  });
});
