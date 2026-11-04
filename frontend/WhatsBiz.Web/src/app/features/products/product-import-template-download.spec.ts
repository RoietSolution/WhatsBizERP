import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { ProductImportTemplatesDialogComponent } from './product-import-templates-dialog.component';

describe('Product import template downloads', () => {
  let api: ProductApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ProductApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests the standard ERP workbook as a Blob from the existing endpoint', () => {
    const file = new Blob(['erp-xlsx']);
    api.template().subscribe(result => expect(result).toBe(file));

    const request = http.expectOne('/api/products/import-template');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(file);
  });

  it('requests the authenticated supplier-specific Shopify workbook as a Blob', () => {
    const file = new Blob(['shopify-xlsx']);
    api.shopifySupplierCatalogTemplate('supplier-id').subscribe(result => expect(result).toBe(file));

    const request = http.expectOne('/api/suppliers/supplier-id/catalog-import/template');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(file);
  });

  it('downloads the Shopify Blob with the expected filename and defers URL revocation', () => {
    const file = new Blob(['valid-xlsx']);
    const apiStub = jasmine.createSpyObj<ProductApiService>('ProductApiService', [
      'supplierOptions', 'template', 'shopifySupplierCatalogTemplate',
    ]);
    apiStub.supplierOptions.and.returnValue(of([]));
    apiStub.shopifySupplierCatalogTemplate.and.returnValue(of(file));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ProductImportTemplatesDialogComponent],
      providers: [
        { provide: ProductApiService, useValue: apiStub },
        { provide: MatDialogRef, useValue: { close: jasmine.createSpy('close') } },
      ],
    });
    const fixture = TestBed.createComponent(ProductImportTemplatesDialogComponent);
    fixture.detectChanges();
    const createUrl = spyOn(URL, 'createObjectURL').and.returnValue('blob:shopify-template');
    const revokeUrl = spyOn(URL, 'revokeObjectURL');
    const click = spyOn(HTMLAnchorElement.prototype, 'click');
    fixture.componentInstance.supplierId = 'supplier-id';

    fixture.componentInstance.downloadShopify();

    expect(apiStub.shopifySupplierCatalogTemplate).toHaveBeenCalledOnceWith('supplier-id');
    expect(createUrl).toHaveBeenCalledOnceWith(file);
    expect(click).toHaveBeenCalled();
    expect(revokeUrl).not.toHaveBeenCalled();
    expect(fixture.componentInstance.error()).toBe('');
    fixture.destroy();
  });
});
