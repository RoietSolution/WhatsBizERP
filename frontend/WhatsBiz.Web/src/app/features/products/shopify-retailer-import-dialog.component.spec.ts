import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of, Subject } from 'rxjs';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { CurrentUserService } from '../../core/services/current-user.service';
import { ProductApiService } from './product-api.service';
import { ShopifyRetailerImportPanelComponent } from './shopify-retailer-import-dialog.component';
import { ProductImportDialogComponent } from './product-import-dialog.component';

describe('ShopifyRetailerImportPanelComponent', () => {
  it('accepts a store name, supplier name, email, or domain and trims the source key', async () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['previewRetailerShopify']);
    api.previewRetailerShopify.and.returnValue(of({ units: [], fieldMappings: {}, mappingSnapshotHash: '', effectiveUnitId: '' }));
    await TestBed.configureTestingModule({
      imports: [ShopifyRetailerImportPanelComponent],
      providers: [{ provide: ProductApiService, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.file.set(new File(['Handle,Title'], 'products.csv', { type: 'text/csv' }));
    const sources = ['GuturGo', 'Fashion Supplier', 'supplier@example.com', 'myshop.myshopify.com'];

    for (const source of sources) {
      component.sourceStore.set(`  ${source}  `);
      component.previewFile();
    }

    expect(api.previewRetailerShopify.calls.allArgs().map(args => args[1])).toEqual(sources);
    const defaults = api.previewRetailerShopify.calls.first().args[2] as any;
    expect(defaults.fieldMappings.sellingPrice).toBe('Variant Price');
    expect(defaults.fieldMappings.mrp).toBe('Variant Compare At Price');
    expect(defaults.fieldMappings.productCost).toBe('Supplier Product Cost');
    expect(defaults.fieldMappings.packagingCost).toBe('Packaging Cost');
    expect(defaults.fieldMappings.shippingCost).toBe('Shipping Cost');
    expect(fixture.nativeElement.textContent).toContain('Shopify Source Store / Supplier Name');
    expect(fixture.nativeElement.textContent).toContain('Import Field Mappings');
    expect(fixture.nativeElement.textContent).toContain('Save Import Mapping');
    expect(getComputedStyle(fixture.nativeElement.querySelector('.source-row')).display).toBe('grid');
    expect(getComputedStyle(fixture.nativeElement.querySelector('.file-picker')).borderTopStyle).toBe('dashed');
    fixture.destroy();
  });

  it('shows exactly ERP Template and V92 Shopify formats in the existing Product Import dialog', async () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['previewRetailerShopify']);
    api.previewRetailerShopify.and.returnValue(of({ units: [] }));
    await TestBed.configureTestingModule({
      imports: [ProductImportDialogComponent],
      providers: [
        { provide: ProductApiService, useValue: api },
        { provide: MAT_DIALOG_DATA, useValue: {} },
        { provide: MatDialogRef, useValue: jasmine.createSpyObj('MatDialogRef', ['close']) },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProductImportDialogComponent);
    fixture.componentInstance.format = 'SHOPIFY';
    fixture.detectChanges();
    const format = fixture.nativeElement.querySelector('.format-choice select') as HTMLSelectElement;
    expect(Array.from(format.options).map(option => option.textContent?.trim())).toEqual([
      'ERP Template (13 columns)', 'Shopify CSV / XLSX',
    ]);

    format.value = 'SHOPIFY';
    format.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-shopify-retailer-import-panel')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[mat-dialog-title]')?.textContent).toContain('Product import');
    expect(fixture.nativeElement.textContent).not.toContain('Supplier Catalogue');
    fixture.destroy();
  });

  it('hands ERP Template selection back to the existing 13-column upload path', async () => {
    const ref = jasmine.createSpyObj('MatDialogRef', ['close']);
    await TestBed.configureTestingModule({
      imports: [ProductImportDialogComponent],
      providers: [{ provide: MatDialogRef, useValue: ref }],
    }).compileComponents();
    const fixture = TestBed.createComponent(ProductImportDialogComponent);
    fixture.detectChanges();
    const continueButton = Array.from(fixture.nativeElement.querySelectorAll('mat-dialog-actions button'))
      .find((button: any) => button.textContent.includes('Continue ERP import')) as HTMLButtonElement;
    continueButton.click();
    expect(ref.close).toHaveBeenCalledOnceWith('ERP');
    fixture.destroy();
  });

  it('commits the same price mappings and options that were previewed with the returned snapshot', async () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['previewRetailerShopify', 'commitRetailerShopify']);
    api.previewRetailerShopify.and.returnValue(of({
      units: [{ unitId: 'unit-1' }], effectiveUnitId: 'unit-1', mappingSnapshotHash: 'snapshot-1',
      invalidCount: 0, alreadyImported: false,
    }));
    api.commitRetailerShopify.and.returnValue(of({ alreadyImported: false, errorCount: 0 }));
    await TestBed.configureTestingModule({ imports: [ShopifyRetailerImportPanelComponent], providers: [{ provide: ProductApiService, useValue: api }] }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.file.set(new File(['Handle,Title'], 'products.csv', { type: 'text/csv' }));
    panel.sourceStore.set('GuturGo');
    panel.previewFile();
    panel.commit();

    const previewSelections = api.previewRetailerShopify.calls.mostRecent().args[2] as any;
    const commitSelections = api.commitRetailerShopify.calls.mostRecent().args[2] as any;
    expect(previewSelections.fieldMappings.sellingPrice).toBe('Variant Price');
    expect(previewSelections.fieldMappings.mrp).toBe('Variant Compare At Price');
    expect(commitSelections).toEqual({ ...previewSelections, unitId: 'unit-1' });
    expect(api.commitRetailerShopify.calls.mostRecent().args[3]).toBe('snapshot-1');
    fixture.destroy();
  });

  it('renders a shared supplier SKU as informational and not as an invalid product', async () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['previewRetailerShopify']);
    api.previewRetailerShopify.and.returnValue(of({
      units: [{ unitId: 'unit-1', unitName: 'Each', shortName: 'ea' }], effectiveUnitId: 'unit-1',
      mappingSnapshotHash: 'snapshot-1', total: 2, newCount: 2, updateCount: 0, unchangedCount: 0,
      invalidCount: 0, sharedSupplierSkuCount: 1, duplicateSkuCount: 1,
      categories: [], brands: [], alreadyImported: false,
      items: [{ rowNumber: 2, handle: 'dress-a', title: 'Dress A', sku: 'SHARED-1', internalProductCode: 'SHP-ABC',
        status: 'NEW', errors: [], warnings: ['Supplier SKU is shared by distinct Shopify Handles; each Handle + SKU is imported separately.'],
        fieldChanges: [], images: [], imageCount: 0, additionalImageCount: 0, type: 'Kitchen', vendor: '',
        categorySourceColumn: 'Product Category', categoryBindingStatus: 'MATCHED',
        productCostSourceColumn: 'Wholesale Price', packagingCostSourceColumn: 'Packaging Charges', shippingCostSourceColumn: 'Shipping Cost2',
        supplierProductCost: 12, packagingCost: 2, shippingCost: 3, effectiveSupplierProductCost: 12,
        effectivePackagingCost: 2, effectiveShippingCost: 3, importedTotalSupplierCost: 17,
        importedProfitLoss: 8, importedMarginPercentage: 32, estimatedTotalSupplierCost: 17,
        estimatedProfitLoss: 8, estimatedMarginPercentage: 32, effectiveSellingPrice: 25, effectiveMrp: 40,
        sellingPrice: 25, mrp: 40 }],
    }));
    await TestBed.configureTestingModule({ imports: [ShopifyRetailerImportPanelComponent], providers: [{ provide: ProductApiService, useValue: api }] }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.file.set(new File(['Handle,Title,Variant SKU,Variant Price'], 'products.csv', { type: 'text/csv' }));
    panel.sourceStore.set('GuturGo');
    panel.previewFile();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('1 shared supplier SKUs');
    expect(fixture.nativeElement.textContent).toContain('Supplier SKU is shared by distinct Shopify Handles');
    expect(fixture.nativeElement.textContent).toContain('ERP internal SKU: SHP-ABC');
    expect(fixture.nativeElement.textContent).toContain('Wholesale Price');
    expect(fixture.nativeElement.textContent).toContain('Packaging Charges');
    expect(fixture.nativeElement.textContent).toContain('Shipping Cost2');
    expect(fixture.nativeElement.textContent).toContain('17');
    expect(fixture.nativeElement.textContent).toContain('Imported profit / loss');
    expect(fixture.nativeElement.textContent).toContain('Effective profit / loss');
    expect(fixture.nativeElement.textContent).toContain('Imported margin');
    expect(fixture.nativeElement.textContent).toContain('Effective margin');
    expect(fixture.nativeElement.textContent).toContain('32%');
    expect(fixture.nativeElement.textContent).toContain('Effective ERP after selected update rules: 25');
    expect(fixture.nativeElement.textContent).toContain('Effective ERP after selected update rules: 40');
    expect(fixture.nativeElement.textContent).toContain('Product Category: Kitchen');
    expect(fixture.nativeElement.querySelector('.status.invalid')).toBeNull();
    expect(fixture.nativeElement.querySelector('.confirm-row button').disabled).toBeFalse();
    const previewSource = fixture.nativeElement.querySelector('.table-wrap tbody td small') as HTMLElement;
    expect(getComputedStyle(previewSource).display).toBe('block');
    expect(getComputedStyle(fixture.nativeElement.querySelector('.table-wrap')).overflow).toContain('auto');
    expect(getComputedStyle(fixture.nativeElement.querySelector('.table-wrap td .warning')).backgroundColor).toBe('rgb(255, 247, 229)');
    fixture.destroy();
  });

  it('detects actual upload headers and preserves explicit custom cost mappings', async () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['detectRetailerShopifyHeaders']);
    api.detectRetailerShopifyHeaders.and.returnValue(of({ headers: ['Handle', 'Type', 'Product Category', 'Packaging Charges', 'Shipping Cost2', 'Wholesale Price'], duplicateHeaders: [] }));
    await TestBed.configureTestingModule({ imports: [ShopifyRetailerImportPanelComponent], providers: [{ provide: ProductApiService, useValue: api }] }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.selectFile({ target: { files: [new File(['headers'], 'products.csv', { type: 'text/csv' })] } } as unknown as Event);
    panel.setFieldMapping('packagingCost', 'Packaging Charges');
    panel.setFieldMapping('shippingCost', 'Shipping Cost2');
    panel.setFieldMapping('productCost', 'Wholesale Price');
    fixture.detectChanges();

    expect(panel.sourceHeaders()).toEqual(['Handle', 'Type', 'Product Category', 'Packaging Charges', 'Shipping Cost2', 'Wholesale Price']);
    expect(panel.fieldMappings['category']).toBe('Product Category');
    panel.setFieldMapping('category', 'Type');
    expect(panel.fieldMappings['category']).toBe('Type');
    expect(panel.hasSourceHeader(panel.fieldMappings['packagingCost'])).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Supplier Dropshipping Costs');
    expect(fixture.nativeElement.textContent).toContain('Packaging Cost');
    fixture.destroy();
  });

  it('keeps explicitly selected Excel source columns when loading a saved source mapping before preview', async () => {
    const key = 'whatsbiz.shopify-retailer-import.v1:tenant-pricing:guturgo';
    localStorage.setItem(key, JSON.stringify({ fieldMappings: {
      sellingPrice: 'Variant Price', mrp: 'Variant Compare At Price', productCost: 'Supplier Product Cost',
      packagingCost: 'Packaging Cost', shippingCost: 'Shipping Cost', category: 'Type',
    } }));
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['detectRetailerShopifyHeaders', 'previewRetailerShopify']);
    api.detectRetailerShopifyHeaders.and.returnValue(of({ headers: ['Handle', 'Title', 'SKU', 'ERP Selling Price', 'Variant Compare At Price', 'Supplier Product Cost', 'Packaging Cost', 'Shipping Cost', 'Product Category'], duplicateHeaders: [] }));
    api.previewRetailerShopify.and.returnValue(of({ units: [], effectiveUnitId: '', mappingSnapshotHash: 'snapshot', items: [], invalidCount: 0 }));
    await TestBed.configureTestingModule({
      imports: [ShopifyRetailerImportPanelComponent],
      providers: [
        { provide: ProductApiService, useValue: api },
        { provide: CurrentUserService, useValue: { user: signal({ tenantId: 'tenant-pricing' }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.setSourceStore('GuturGo');
    panel.selectFile({ target: { files: [new File(['headers'], 'pricing.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })] } } as unknown as Event);
    panel.setFieldMapping('sellingPrice', 'ERP Selling Price');
    panel.setFieldMapping('mrp', 'Variant Compare At Price');
    panel.setFieldMapping('productCost', 'Supplier Product Cost');
    panel.setFieldMapping('packagingCost', 'Packaging Cost');
    panel.setFieldMapping('shippingCost', 'Shipping Cost');
    panel.previewFile();

    const sent = (api.previewRetailerShopify.calls.mostRecent().args[2] as any).fieldMappings;
    expect(sent.sellingPrice).toBe('ERP Selling Price');
    expect(sent.mrp).toBe('Variant Compare At Price');
    expect(sent.productCost).toBe('Supplier Product Cost');
    expect(sent.packagingCost).toBe('Packaging Cost');
    expect(sent.shippingCost).toBe('Shipping Cost');
    expect(sent.category).toBe('Product Category');
    fixture.destroy();
    localStorage.removeItem(key);
  });

  it('defaults GuturGo to inclusive Supplier Product Cost and direct retail price columns', async () => {
    const key = 'whatsbiz.shopify-retailer-import.v1:tenant-guturgo:guturgo';
    localStorage.setItem(key, JSON.stringify({ fieldMappings: {
      sellingPrice: 'Variant Price', mrp: 'Variant Compare At Price', productCost: 'Supplier Product Cost',
      packagingCost: 'Packaging Cost', shippingCost: 'Shipping Cost', category: 'Type',
    } }));
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['detectRetailerShopifyHeaders', 'previewRetailerShopify']);
    api.detectRetailerShopifyHeaders.and.returnValue(of({ headers: [
      'Handle', 'Title', 'Variant SKU', 'Variant Price', 'Packaging Cost', 'Shipping Cost',
      'Supplier Product Cost', 'ERP Selling Price', 'Variant Compare At Price', 'Product Category', 'Type',
    ], duplicateHeaders: [] }));
    api.previewRetailerShopify.and.returnValue(of({ units: [], effectiveUnitId: '', mappingSnapshotHash: 'actual-snapshot', items: [], invalidCount: 0 }));
    await TestBed.configureTestingModule({
      imports: [ShopifyRetailerImportPanelComponent],
      providers: [
        { provide: ProductApiService, useValue: api },
        { provide: CurrentUserService, useValue: { user: signal({ tenantId: 'tenant-guturgo' }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.setSourceStore('GuturGo');
    panel.selectFile({ target: { files: [new File(['headers'], 'products.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })] } } as unknown as Event);
    panel.previewFile();

    const mappings = (api.previewRetailerShopify.calls.mostRecent().args[2] as any).fieldMappings;
    expect(mappings.sellingPrice).toBe('ERP Selling Price');
    expect(mappings.productCost).toBe('Supplier Product Cost');
    expect(mappings.packagingCost).toBe('Packaging Cost');
    expect(mappings.shippingCost).toBe('Shipping Cost');
    expect(mappings.mrp).toBe('Variant Compare At Price');
    expect(mappings.category).toBe('Product Category');
    fixture.destroy();
    localStorage.removeItem(key);
  });

  it('resets mappings and saved selections to current source defaults', async () => {
    const key = 'whatsbiz.shopify-retailer-import.v1:tenant-reset:guturgo';
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['detectRetailerShopifyHeaders']);
    api.detectRetailerShopifyHeaders.and.returnValue(of({ headers: [
      'Handle', 'Title', 'Variant SKU', 'Variant Price', 'ERP Selling Price', 'Product Category', 'Type',
    ], duplicateHeaders: [] }));
    await TestBed.configureTestingModule({
      imports: [ShopifyRetailerImportPanelComponent],
      providers: [
        { provide: ProductApiService, useValue: api },
        { provide: CurrentUserService, useValue: { user: signal({ tenantId: 'tenant-reset' }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.setSourceStore('GuturGo');
    panel.selectFile({ target: { files: [new File(['headers'], 'products.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })] } } as unknown as Event);
    panel.setFieldMapping('sellingPrice', 'Wrong Price');
    panel.setFieldMapping('category', 'Type');
    panel.saveImportMapping();
    panel.resetFieldMappings();

    expect(panel.fieldMappings['sellingPrice']).toBe('ERP Selling Price');
    expect(panel.fieldMappings['productCost']).toBe('Supplier Product Cost');
    expect(panel.supplierCostTreatment).toBe('INCLUSIVE');
    expect(panel.fieldMappings['category']).toBe('Product Category');
    expect(JSON.parse(localStorage.getItem(key)!).explicitFieldMappings).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('Reset to Default Mappings');
    fixture.destroy();
    localStorage.removeItem(key);
  });

  it('renders the mapped GuturGo preview values separately from existing effective ERP values', async () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['detectRetailerShopifyHeaders', 'previewRetailerShopify']);
    api.detectRetailerShopifyHeaders.and.returnValue(of({ headers: [
      'Handle', 'Title', 'Variant SKU', 'Variant Price', 'Packaging Cost', 'Shipping Cost',
      'Supplier Product Cost', 'ERP Selling Price', 'Variant Compare At Price', 'Product Category', 'Type',
    ], duplicateHeaders: [] }));
    api.previewRetailerShopify.and.returnValue(of({
      sourceStore: 'GuturGo', fieldMappings: {}, mappingSnapshotHash: 'pricing-preview', effectiveUnitId: 'unit-1',
      units: [{ unitId: 'unit-1', unitName: 'Each', shortName: 'ea' }], brands: [],
      categories: [{ productCategoryId: 'shipping', displayName: 'Shipping Supplies', categoryCode: 'SHIP' }],
      total: 1, newCount: 0, updateCount: 1, unchangedCount: 0, invalidCount: 0, alreadyImported: false,
      items: [{ rowNumber: 2, handle: 'courier-bags', title: 'Courier Polybags 6x8 Inch Self Adhesive Tamper Proof Bags',
        sku: 'BAG-6X8', status: 'UPDATE', errors: [], warnings: [], fieldChanges: [], images: [], imageCount: 0,
        type: 'Shipping Supplies', categorySourceColumn: 'Product Category', categoryBindingStatus: 'MATCHED',
        selectedCategoryId: 'shipping', productCostSourceColumn: 'Supplier Product Cost', supplierProductCost: 328,
        packagingCostSourceColumn: 'Packaging Cost', packagingCost: 30, shippingCostSourceColumn: 'Shipping Cost', shippingCost: 60,
        importedTotalSupplierCost: 328, estimatedTotalSupplierCost: 328, importedProfitLoss: 131,
        estimatedProfitLoss: 131, importedMarginPercentage: 28.54, estimatedMarginPercentage: 28.54,
        importedSellingPrice: 459, erpSellingPrice: 459, effectiveSellingPrice: 459,
        importedMrp: 714, erpMrp: 714, effectiveMrp: 714, importedPricingWarnings: [], effectivePricingWarnings: [] }],
    }));
    await TestBed.configureTestingModule({
      imports: [ShopifyRetailerImportPanelComponent],
      providers: [
        { provide: ProductApiService, useValue: api },
        { provide: CurrentUserService, useValue: { user: signal({ tenantId: 'tenant-guturgo-preview' }) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.setSourceStore('GuturGo');
    panel.selectFile({ target: { files: [new File(['workbook'], 'products.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })] } } as unknown as Event);
    panel.previewFile();
    fixture.detectChanges();

    const cells = fixture.nativeElement.querySelectorAll('.table-wrap tbody tr:first-child td');
    expect(cells[0].textContent).toContain('328');
    expect(cells[0].textContent).toContain('Supplier Product Cost');
    expect(cells[3].textContent).toContain('328');
    expect(cells[5].textContent).toContain('131');
    expect(cells[7].textContent).toContain('28.54%');
    expect(cells[11].textContent).toContain('459');
    expect(cells[11].textContent).toContain('ERP Selling Price');
    expect(cells[11].textContent).not.toContain('238');
    expect(cells[12].textContent).toContain('714');
    expect(cells[13].textContent).toContain('Product Category: Shipping Supplies');
    expect(cells[13].textContent).toContain('MATCHED');
    expect((cells[13].querySelector('select') as HTMLSelectElement).value).toBe('shipping');
    const previewHeadings = Array.from(fixture.nativeElement.querySelectorAll('.table-wrap th'))
      .map((heading: any) => heading.textContent.trim());
    expect(previewHeadings).toContain('Supplier Product Cost (Inclusive of Packaging & Shipping)');
    expect(previewHeadings).toContain('Imported packaging cost');
    expect(previewHeadings).toContain('Imported shipping estimate');
    expect(previewHeadings).toContain('Imported ERP Selling Price');
    expect(previewHeadings).toContain('Imported ERP MRP');
    expect(fixture.nativeElement.textContent).toContain('0 invalid');
    expect(fixture.nativeElement.querySelectorAll('.error-text').length).toBe(0);
    expect(fixture.nativeElement.querySelectorAll('.warning').length).toBe(0);
    expect(getComputedStyle(fixture.nativeElement.querySelector('.table-wrap')).overflow).toContain('auto');
    fixture.destroy();
  });

  it('discards an in-flight preview as soon as a field mapping changes', async () => {
    const staleResponse = new Subject<any>();
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['previewRetailerShopify']);
    api.previewRetailerShopify.and.returnValues(
      of({ mappingSnapshotHash: 'first-snapshot', items: [{ title: 'First' }], invalidCount: 0 }),
      staleResponse,
    );
    await TestBed.configureTestingModule({
      imports: [ShopifyRetailerImportPanelComponent],
      providers: [{ provide: ProductApiService, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.file.set(new File(['Handle,Title'], 'products.csv', { type: 'text/csv' }));
    panel.sourceStore.set('GuturGo');
    panel.previewFile();
    expect(panel.preview()).not.toBeNull();

    panel.previewFile();
    expect(panel.busy()).toBeTrue();
    panel.setFieldMapping('sellingPrice', 'ERP Selling Price');
    expect(panel.preview()).toBeNull();
    expect(panel.busy()).toBeFalse();
    expect(panel.mappingDirty()).toBeTrue();

    staleResponse.next({ mappingSnapshotHash: 'stale-snapshot', items: [{ title: 'Stale result' }], invalidCount: 0 });
    fixture.detectChanges();
    expect(panel.preview()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Stale result');
    fixture.destroy();
  });

  it('applies one manual category mapping to every product with the same source category', async () => {
    const item = { rowNumber: 2, handle: 'one', title: 'One', type: 'Kitchen', status: 'INVALID', errors: ['Map product category'], warnings: [], images: [] };
    const resolvedItems = [item, { ...item, rowNumber: 3, handle: 'two', title: 'Two' }]
      .map(row => ({ ...row, status: 'NEW', errors: [], selectedCategoryId: 'category-kitchen', categoryBindingStatus: 'MANUAL' }));
    const categories = [{ productCategoryId: 'category-kitchen', displayName: 'Home > Kitchen', categoryCode: 'KIT' }];
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['previewRetailerShopify']);
    api.previewRetailerShopify.and.returnValues(
      of({ units: [], effectiveUnitId: '', mappingSnapshotHash: 'snapshot-1', alreadyImported: false, total: 2, invalidCount: 2, newCount: 0, categories, brands: [], items: [item, { ...item, rowNumber: 3, handle: 'two', title: 'Two' }] }),
      of({ units: [], effectiveUnitId: '', mappingSnapshotHash: 'snapshot-2', alreadyImported: false, total: 2, invalidCount: 0, newCount: 2, categories, brands: [], items: resolvedItems }),
    );
    await TestBed.configureTestingModule({ imports: [ShopifyRetailerImportPanelComponent], providers: [{ provide: ProductApiService, useValue: api }] }).compileComponents();
    const fixture = TestBed.createComponent(ShopifyRetailerImportPanelComponent);
    fixture.detectChanges();
    const panel = fixture.componentInstance;
    panel.file.set(new File(['Handle,Title,Product Category'], 'products.csv', { type: 'text/csv' }));
    panel.sourceStore.set('GuturGo');
    panel.previewFile();
    fixture.detectChanges();
    const blockingError = fixture.nativeElement.querySelector('.table-wrap td .error-text') as HTMLElement;
    expect(getComputedStyle(blockingError).backgroundColor).toBe('rgb(255, 240, 239)');
    const categorySelects = Array.from(fixture.nativeElement.querySelectorAll('td select[aria-label="Category mapping"]')) as HTMLSelectElement[];
    expect(categorySelects.length).toBe(2);
    categorySelects[0].value = 'category-kitchen';
    categorySelects[0].dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.previewRetailerShopify).toHaveBeenCalledTimes(2);
    expect(panel.categoryByType['KITCHEN']).toBe('category-kitchen');
    expect((api.previewRetailerShopify.calls.mostRecent().args[2] as any).categoryByType.KITCHEN).toBe('category-kitchen');
    expect(fixture.nativeElement.textContent).toContain('0 invalid');
    expect(fixture.nativeElement.querySelector('.status.invalid')).toBeNull();
    const refreshedCategorySelects = Array.from(fixture.nativeElement.querySelectorAll('td select[aria-label="Category mapping"]')) as HTMLSelectElement[];
    expect(refreshedCategorySelects.map(select => select.value)).toEqual(['category-kitchen', 'category-kitchen']);
    fixture.destroy();
  });

});
