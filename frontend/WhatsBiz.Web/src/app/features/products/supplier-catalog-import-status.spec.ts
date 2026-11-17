import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { SupplierCatalogImportDialogComponent } from './supplier-catalog-import-dialog.component';

describe('Supplier catalogue Shopify status preview', () => {
  it('shows source, current, effective status and a disabled-sync activation warning', async () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['supplierOptions']);
    api.supplierOptions.and.returnValue(of([]));
    await TestBed.configureTestingModule({
      imports: [SupplierCatalogImportDialogComponent],
      providers: [
        { provide: ProductApiService, useValue: api },
        { provide: MAT_DIALOG_DATA, useValue: {} },
        { provide: MatDialogRef, useValue: { close: jasmine.createSpy('close') } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SupplierCatalogImportDialogComponent);
    fixture.componentInstance.preview.set({
      totalRows: 1, existingCategoryCount: 1, newCategoryCount: 0, needsMappingCount: 0,
      rows: [{
        rowNumber: 2, title: 'Test product', supplierSku: 'SKU-1', tagOptions: ['Kitchen'],
        tags: '  Mobile   Accessories,   Mobile Headphones  ', erpCategoryName: 'Mobile Accessories, Mobile Headphones',
        categoryStatus: 'EXISTING', gstPercentage: 0, gstSource: 'Default', gstPricingType: 'INCLUSIVE',
        errors: [], missingFields: [], images: [], imageCount: 0,
        shopifyStatus: 'draft', currentErpStatus: 'ACTIVE', proposedErpStatus: 'INACTIVE',
        effectiveErpStatus: 'ACTIVE', statusAction: 'STATUS SYNC OFF — WOULD DEACTIVATE',
      }],
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Shopify: draft');
    expect(text).toContain('Mobile Accessories, Mobile Headphones');
    expect(text).toContain('Existing tenant category matched');
    expect(text).toContain('Shopify Tags:   Mobile   Accessories,   Mobile Headphones');
    expect(text).toContain('GST: 0% (Default) | INCLUSIVE');
    expect(text).toContain('Current ERP: ACTIVE');
    expect(text).toContain('From file: INACTIVE');
    expect(text).toContain('After import: ACTIVE');
    expect(text).toContain('STATUS SYNC OFF — WOULD DEACTIVATE');
    fixture.destroy();
  });
});
