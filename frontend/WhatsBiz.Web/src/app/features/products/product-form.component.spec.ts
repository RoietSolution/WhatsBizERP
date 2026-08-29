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
    component.saveScanned();

    expect(component.form.controls.barcode.value).toBe('8901234567890');
    expect(component.form.controls.barcodeType.value).toBe('EAN13');
    expect(component.additionalBarcodes()).toEqual([]);
  });

  it('stores exact manufacturer QR URL text without navigating to it', () => {
    const { component } = setup();
    const qr = ' https://manufacturer.example/item/ABC?lot=Lot%2026 ';
    const open = spyOn(window, 'open');
    component.openScanner('additional');

    component.scanned({ value: qr, barcodeType: 'QR' });
    component.saveScanned();

    expect(component.additionalBarcodes()).toEqual([{ barcode: qr, barcodeType: 'QR' }]);
    expect(open).not.toHaveBeenCalled();
  });

  it('does not create a duplicate when the same code is scanned for the current product', () => {
    const { component, snack } = setup();
    component.additionalBarcodes.set([{ barcode: 'SAME-CODE', barcodeType: 'CODE128' }]);
    component.openScanner('additional');
    component.scanned({ value: 'SAME-CODE', barcodeType: 'CODE128' });

    component.saveScanned();

    expect(component.additionalBarcodes()).toHaveSize(1);
    expect(snack.open).toHaveBeenCalledWith(
      'This code is already linked to the current product.',
      undefined,
      jasmine.any(Object),
    );
  });

  it('rejects QR content longer than the backend limit', () => {
    const { component, snack } = setup();
    component.openScanner('additional');
    component.scanned({ value: 'X'.repeat(451), barcodeType: 'QR' });

    component.saveScanned();

    expect(component.additionalBarcodes()).toEqual([]);
    expect(snack.open).toHaveBeenCalledWith(
      'Additional barcode/QR content cannot exceed 450 characters.',
      'Dismiss',
      jasmine.any(Object),
    );
  });
});
