import { CommonModule } from '@angular/common';
import { Component, EventEmitter, inject, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { finalize } from 'rxjs';
import { CurrentUserService } from '../../core/services/current-user.service';
import { ProductApiService } from './product-api.service';

const DEFAULT_FIELD_MAPPINGS: Record<string, string> = {
  handle: 'Handle', title: 'Title', sku: 'Variant SKU', description: 'Body (HTML)',
  category: 'Product Category', brand: 'Vendor', barcode: 'Variant Barcode', sellingPrice: 'Variant Price',
  mrp: 'Variant Compare At Price', image: 'Image Src', imagePosition: 'Image Position',
  imageAlt: 'Image Alt Text',
  productCost: 'Supplier Product Cost', packagingCost: 'Packaging Cost', shippingCost: 'Shipping Cost',
  gstPercentage: '', weight: '', weightUnit: '',
};

// Older source-scoped preferences stored the original V92 UI defaults. Treat those
// as defaults, not as an explicit retailer choice, so current source/header defaults
// can correct legacy mappings for the GuturGo workbook.
const LEGACY_FIELD_MAPPING_DEFAULTS: Record<string, string> = {
  ...DEFAULT_FIELD_MAPPINGS,
  category: 'Type',
};

type UpdateField = 'productName' | 'description' | 'images' | 'category' | 'brand' | 'unit' |
  'sellingPrice' | 'mrp' | 'productCost' | 'packagingCost' | 'shippingCost' | 'gst' | 'weight';

@Component({
  selector: 'app-shopify-retailer-import-panel', standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './shopify-retailer-import-dialog.component.html', styleUrl: './shopify-retailer-import-dialog.component.scss',
})
export class ShopifyRetailerImportPanelComponent {
  private readonly api = inject(ProductApiService);
  private readonly currentUser = inject(CurrentUserService);
  @Output() imported = new EventEmitter<void>();
  readonly file = signal<File | null>(null);
  readonly sourceStore = signal('');
  readonly preview = signal<any>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly sourceHeaders = signal<string[]>([]);
  readonly duplicateHeaders = signal<string[]>([]);
  readonly headersBusy = signal(false);
  readonly mappingDirty = signal(true);
  readonly fieldMappings = { ...DEFAULT_FIELD_MAPPINGS };
  supplierCostTreatment: 'INCLUSIVE' | 'EXCLUSIVE' = 'EXCLUSIVE';
  readonly categoryByType: Record<string, string> = {};
  readonly brandByVendor: Record<string, string> = {};
  readonly updateFields: Record<UpdateField, boolean> = {
    productName: true, description: true, images: true, category: true, brand: true, unit: true,
    sellingPrice: true, mrp: true, productCost: false, packagingCost: false, shippingCost: false, gst: false, weight: false,
  };
  readonly fieldMappingGroups = [
    { key: 'identity', label: 'Product Identity' }, { key: 'catalogue', label: 'Catalogue' },
    { key: 'retail', label: 'Retail Pricing' }, { key: 'costs', label: 'Supplier Dropshipping Costs' },
    { key: 'images', label: 'Images' },
  ];
  readonly fieldMappingRows = [
    { key: 'handle', label: 'Source Handle', group: 'identity', required: true },
    { key: 'title', label: 'Product name', group: 'identity', required: true },
    { key: 'sku', label: 'Product SKU', group: 'identity', required: true },
    { key: 'barcode', label: 'Barcode', group: 'identity', required: false },
    { key: 'category', label: 'Category', group: 'catalogue', required: false },
    { key: 'description', label: 'Description', group: 'catalogue', required: false },
    { key: 'brand', label: 'Brand / Vendor', group: 'catalogue', required: false },
    { key: 'gstPercentage', label: 'GST percentage (optional)', group: 'catalogue', required: false },
    { key: 'weight', label: 'Weight value (optional)', group: 'catalogue', required: false },
    { key: 'weightUnit', label: 'Weight unit (optional)', group: 'catalogue', required: false },
    { key: 'sellingPrice', label: 'ERP Selling Price', group: 'retail', required: true },
    { key: 'mrp', label: 'ERP MRP', group: 'retail', required: false },
    { key: 'productCost', label: 'Supplier Product Cost', group: 'costs', required: false },
    { key: 'packagingCost', label: 'Packaging Cost', group: 'costs', required: false },
    { key: 'shippingCost', label: 'Shipping Cost (estimate)', group: 'costs', required: false },
    { key: 'image', label: 'Primary / gallery image URL', group: 'images', required: false },
    { key: 'imagePosition', label: 'Image position', group: 'images', required: false },
    { key: 'imageAlt', label: 'Image description', group: 'images', required: false },
  ];
  unitId = '';
  defaultBrandId = '';
  defaultGstPercentage: number | null = null;
  activateNew = false;
  private previewHash = '';
  private loadedSourceKey = '';
  private headerRequestSequence = 0;
  private previewRequestSequence = 0;
  private categoryMappingExplicit = false;
  private readonly touchedFieldMappings = new Set<string>();

  constructor() { }

  selectFile(event: Event): void {
    const selected = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.previewRequestSequence++;
    this.busy.set(false);
    this.file.set(selected);
    this.preview.set(null);
    this.previewHash = '';
    this.mappingDirty.set(true);
    this.error.set('');
    this.notice.set('');
    this.sourceHeaders.set([]);
    this.duplicateHeaders.set([]);
    const requestSequence = ++this.headerRequestSequence;
    if (selected) {
      this.headersBusy.set(true);
      this.api.detectRetailerShopifyHeaders(selected).pipe(finalize(() => {
        if (requestSequence === this.headerRequestSequence) this.headersBusy.set(false);
      })).subscribe({
        next: result => {
          if (requestSequence !== this.headerRequestSequence) return;
          this.sourceHeaders.set(result.headers ?? []);
          this.duplicateHeaders.set(result.duplicateHeaders ?? []);
          this.applyUntouchedDefaults();
          if (this.applyDefaultCategoryHeader(result.headers ?? [])) this.markDirty();
        },
        error: response => {
          if (requestSequence === this.headerRequestSequence) this.error.set(response?.error?.message ?? 'Could not read source columns from this file.');
        },
      });
    } else this.headersBusy.set(false);
  }

  setSourceStore(value: string): void {
    if (value !== this.sourceStore()) {
      const sourceMappingWasLoaded = !!this.loadedSourceKey;
      this.previewRequestSequence++;
      this.busy.set(false);
      this.sourceStore.set(value);
      this.supplierCostTreatment = this.defaultSupplierCostTreatment(value);
      this.loadedSourceKey = '';
      // Preserve edits made before the source was entered. Once a source-specific
      // preference has been loaded, discard its manual category IDs on source switch
      // and let the next source load its own preferences.
      if (sourceMappingWasLoaded) this.touchedFieldMappings.clear();
      this.categoryMappingExplicit = this.touchedFieldMappings.has('category');
      this.preview.set(null);
      this.previewHash = '';
      this.applyUntouchedDefaults();
      this.markDirty();
    }
  }

  loadMappingForCurrentSource(): void { this.loadSavedMappingForSource(); }

  setFieldMapping(key: string, value: string): void {
    if (this.isGuturGoSource(this.sourceStore()) && this.isPricingMapping(key) && this.normalizeKey(value) === 'VARIANTPRICE') {
      this.error.set('For GuturGo, Variant Price is not used for pricing or supplier costs. Select Supplier Product Cost, Packaging Cost, Shipping Cost, ERP Selling Price or Variant Compare At Price.');
      return;
    }
    this.fieldMappings[key] = value;
    this.error.set('');
    this.touchedFieldMappings.add(key);
    if (key === 'category') this.categoryMappingExplicit = true;
    this.markDirty();
  }

  setUpdateField(key: UpdateField, value: boolean): void {
    this.updateFields[key] = value;
    this.markDirty();
  }

  setCategoryMapping(category: string, id: string): void {
    const key = this.normalizeKey(category);
    const shouldRefresh = !!this.preview() && !!this.file() && !!this.sourceStore().trim();
    if (id) this.categoryByType[key] = id;
    else delete this.categoryByType[key];
    const currentPreview = this.preview();
    if (currentPreview?.items?.length) {
      this.preview.set({
        ...currentPreview,
        items: currentPreview.items.map((row: any) => this.normalizeKey(row.type || '') === key
          ? { ...row, selectedCategoryId: id || null, categoryBindingStatus: id ? 'MANUAL' : row.categoryBindingStatus }
          : row),
      });
    }
    this.markDirty();
    // A chosen category resolves preview validation and changes the valid/invalid
    // totals. Refresh immediately while preserving this source-scoped mapping.
    if (shouldRefresh) this.previewFile();
  }

  setBrandMapping(vendor: string, id: string): void {
    const key = this.normalizeKey(vendor);
    if (id) this.brandByVendor[key] = id;
    else delete this.brandByVendor[key];
    this.markDirty();
  }

  setUnit(id: string): void { this.unitId = id; this.markDirty(); }
  setDefaultBrand(id: string): void { this.defaultBrandId = id; this.markDirty(); }
  setDefaultGst(value: number | null): void { this.defaultGstPercentage = value; this.markDirty(); }
  setSupplierCostTreatment(value: string): void {
    if (value !== 'INCLUSIVE' && value !== 'EXCLUSIVE') return;
    this.supplierCostTreatment = value;
    this.markDirty();
  }
  setActivateNew(value: boolean): void { this.activateNew = value; this.markDirty(); }

  resetFieldMappings(): void {
    this.touchedFieldMappings.clear();
    this.categoryMappingExplicit = false;
    Object.assign(this.fieldMappings, this.mappingDefaultsForSource(this.sourceStore(), this.sourceHeaders()));
    this.supplierCostTreatment = this.defaultSupplierCostTreatment(this.sourceStore());
    this.applyDefaultCategoryHeader(this.sourceHeaders());
    const storageKey = this.storageKey(this.sourceStore());
    let savedReset = true;
    if (storageKey) {
      try {
        const saved = JSON.parse(localStorage.getItem(storageKey) || '{}');
        localStorage.setItem(storageKey, JSON.stringify({
          ...saved,
          fieldMappings: { ...this.fieldMappings },
          explicitFieldMappings: [],
        }));
        this.loadedSourceKey = storageKey;
      } catch {
        savedReset = false;
      }
    }
    this.markDirty();
    this.notice.set(savedReset
      ? 'Field mappings reset to the defaults for this source and uploaded file. Preview again to validate them.'
      : 'Default mappings were reset for this preview, but the browser could not update the saved mapping.');
  }

  previewFile(): void {
    if (this.headersBusy()) return;
    if (!this.file() || !this.sourceStore().trim()) {
      this.error.set('Choose a Shopify CSV/XLSX file and enter a source store or supplier name.');
      return;
    }
    this.loadSavedMappingForSource();
    const selections = this.buildSelections();
    const requestSequence = ++this.previewRequestSequence;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    this.api.previewRetailerShopify(this.file()!, this.sourceStore().trim(), selections)
      .pipe(finalize(() => { if (requestSequence === this.previewRequestSequence) this.busy.set(false); }))
      .subscribe({
        next: result => {
          if (requestSequence !== this.previewRequestSequence) return;
          this.preview.set(result);
          this.previewHash = result.mappingSnapshotHash ?? '';
          if (!this.unitId) this.unitId = result.effectiveUnitId ?? result.units?.[0]?.unitId ?? '';
          if (this.unitId && !result.units?.some((unit: any) => unit.unitId === this.unitId)) {
            this.unitId = '';
            this.error.set('The saved default unit is no longer active. Select an active unit and preview again.');
            this.mappingDirty.set(true);
          } else this.mappingDirty.set(false);
        },
        error: response => {
          if (requestSequence === this.previewRequestSequence)
            this.error.set(response?.error?.message ?? 'The product file could not be previewed.');
        },
      });
  }

  saveImportMapping(): void {
    const storageKey = this.storageKey(this.sourceStore());
    if (!storageKey) {
      this.error.set('Enter the source identifier and sign in to this retailer before saving its import mapping.');
      return;
    }
    try {
      localStorage.setItem(storageKey, JSON.stringify(this.savedConfiguration()));
      this.notice.set('Import mapping saved for this retailer and source in this browser.');
      this.error.set('');
    } catch {
      this.error.set('The browser could not save this import mapping.');
    }
  }

  commit(): void {
    if (!this.file() || !this.preview() || !this.previewHash || this.mappingDirty() || !this.unitId || this.preview().invalidCount || this.preview().alreadyImported) return;
    const selections = this.buildSelections();
    this.busy.set(true);
    this.error.set('');
    this.api.commitRetailerShopify(this.file()!, this.sourceStore().trim(), selections, this.previewHash)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: result => {
          if (result.alreadyImported) {
            this.error.set('This exact source file was already imported. No duplicate products were created.');
            return;
          }
          if (result.errorCount) {
            this.error.set(`${result.errorCount} row(s) could not be imported. Correct the issues and preview again.`);
            this.preview.set({ ...this.preview(), invalidCount: result.errorCount });
            this.mappingDirty.set(true);
            return;
          }
          this.imported.emit();
        },
        error: response => this.error.set(response?.error?.message ?? 'Import failed. No products were committed. Resolve the issue and preview again.'),
      });
  }

  private markDirty(): void {
    // Any request already in flight was built from an older selection snapshot.
    // Fence its response and remove the old preview/hash so it cannot be shown
    // or committed while the edited selections await a fresh preview.
    this.previewRequestSequence++;
    this.busy.set(false);
    this.preview.set(null);
    this.previewHash = '';
    this.mappingDirty.set(true);
    this.error.set('');
  }

  private buildSelections(): object {
    return {
      unitId: this.unitId || null,
      categoryByType: this.categoryByType,
      brandByVendor: this.brandByVendor,
      activateNewProducts: this.activateNew,
      fieldMappings: { ...this.fieldMappings },
      defaultBrandId: this.defaultBrandId || null,
      defaultGstPercentage: this.defaultGstPercentage,
      updateProductName: this.updateFields.productName,
      updateDescription: this.updateFields.description,
      updateImages: this.updateFields.images,
      updateCategory: this.updateFields.category,
      updateBrand: this.updateFields.brand,
      updateUnit: this.updateFields.unit,
      updateSellingPrice: this.updateFields.sellingPrice,
      updateMrp: this.updateFields.mrp,
      updateProductCost: this.updateFields.productCost,
      updatePackagingCost: this.updateFields.packagingCost,
      updateShippingCost: this.updateFields.shippingCost,
      updateGst: this.updateFields.gst,
      updateWeight: this.updateFields.weight,
      supplierCostTreatment: this.supplierCostTreatment,
    };
  }

  private savedConfiguration(): object {
    return {
      fieldMappings: { ...this.fieldMappings }, categoryByType: { ...this.categoryByType },
      explicitFieldMappings: [...this.touchedFieldMappings],
      brandByVendor: { ...this.brandByVendor }, updateFields: { ...this.updateFields },
      unitId: this.unitId || '', defaultBrandId: this.defaultBrandId,
      defaultGstPercentage: this.defaultGstPercentage, activateNew: this.activateNew,
      supplierCostTreatment: this.supplierCostTreatment,
    };
  }

  private loadSavedMappingForSource(): void {
    const nextKey = this.storageKey(this.sourceStore());
    if (!nextKey || nextKey === this.loadedSourceKey) return;
    const touchedMappings = new Map([...this.touchedFieldMappings].map(key => [key, this.fieldMappings[key]]));
    this.loadedSourceKey = nextKey;
    Object.assign(this.fieldMappings, this.mappingDefaultsForSource(this.sourceStore(), this.sourceHeaders()));
    this.supplierCostTreatment = this.defaultSupplierCostTreatment(this.sourceStore());
    for (const key of Object.keys(this.categoryByType)) delete this.categoryByType[key];
    for (const key of Object.keys(this.brandByVendor)) delete this.brandByVendor[key];
    Object.assign(this.updateFields, { productName: true, description: true, images: true, category: true, brand: true, unit: true, sellingPrice: true, mrp: true, productCost: false, packagingCost: false, shippingCost: false, gst: false, weight: false });
    this.unitId = '';
    this.defaultBrandId = '';
    this.defaultGstPercentage = null;
    this.activateNew = false;
    let saved: any = null;
    try {
      const raw = localStorage.getItem(nextKey);
      if (raw) saved = JSON.parse(raw);
    } catch { /* Invalid local configuration falls back to the safe V92 defaults. */ }
    if (saved) {
      const hasExplicitMappingMetadata = Array.isArray(saved.explicitFieldMappings);
      const savedExplicitMappings = new Set<string>(hasExplicitMappingMetadata
        ? saved.explicitFieldMappings.filter((key: unknown): key is string => typeof key === 'string')
        : Object.entries(saved.fieldMappings ?? {})
          .filter(([key, value]) => key in LEGACY_FIELD_MAPPING_DEFAULTS && String(value ?? '') !== LEGACY_FIELD_MAPPING_DEFAULTS[key])
          .map(([key]) => key));
      if (saved.fieldMappings && typeof saved.fieldMappings === 'object') {
        for (const [key, value] of Object.entries(saved.fieldMappings))
          if (savedExplicitMappings.has(key) && !touchedMappings.has(key)) this.fieldMappings[key] = String(value ?? '');
      }
      for (const key of savedExplicitMappings) this.touchedFieldMappings.add(key);
      if (saved.categoryByType && typeof saved.categoryByType === 'object') Object.assign(this.categoryByType, saved.categoryByType);
      if (saved.brandByVendor && typeof saved.brandByVendor === 'object') Object.assign(this.brandByVendor, saved.brandByVendor);
      if (saved.updateFields && typeof saved.updateFields === 'object') Object.assign(this.updateFields, saved.updateFields);
      this.unitId = typeof saved.unitId === 'string' ? saved.unitId : '';
      this.defaultBrandId = typeof saved.defaultBrandId === 'string' ? saved.defaultBrandId : '';
      this.defaultGstPercentage = typeof saved.defaultGstPercentage === 'number' ? saved.defaultGstPercentage : null;
      this.activateNew = saved.activateNew === true;
      if (saved.supplierCostTreatment === 'INCLUSIVE' || saved.supplierCostTreatment === 'EXCLUSIVE')
        this.supplierCostTreatment = saved.supplierCostTreatment;
      this.categoryMappingExplicit = touchedMappings.has('category') || savedExplicitMappings.has('category');
    }
    for (const [key, value] of touchedMappings) this.fieldMappings[key] = value;
    // Legacy GuturGo preferences used Variant Price for the selling or base-cost
    // field. This import configuration must use only the five retailer columns.
    let correctedLegacyPriceMapping = false;
    if (this.isGuturGoSource(this.sourceStore())) {
      const safeMappings: Record<string, string[]> = {
        sellingPrice: ['ERP Selling Price'], mrp: ['Variant Compare At Price'],
        productCost: ['Supplier Product Cost'], packagingCost: ['Packaging Cost'], shippingCost: ['Shipping Cost'],
      };
      for (const [key, defaults] of Object.entries(safeMappings)) {
        if (this.normalizeKey(this.fieldMappings[key]) === 'VARIANTPRICE') {
          this.fieldMappings[key] = this.findHeader(this.sourceHeaders(), defaults) ?? defaults[0];
          this.touchedFieldMappings.delete(key);
          correctedLegacyPriceMapping = true;
        }
      }
    }
    if (!saved) this.categoryMappingExplicit = touchedMappings.has('category');
    if (this.applyDefaultCategoryHeader(this.sourceHeaders()) || correctedLegacyPriceMapping) this.markDirty();
  }

  private storageKey(source: string): string | null {
    const tenantId = this.currentUser.user()?.tenantId;
    const normalizedSource = source.normalize('NFC').trim().replace(/\s+/g, ' ').toLowerCase();
    return tenantId && normalizedSource ? `whatsbiz.shopify-retailer-import.v1:${tenantId}:${normalizedSource}` : null;
  }

  normalizeKey(value: string): string { return value.normalize('NFKC').replace(/[^\p{L}\p{N}]/gu, '').toUpperCase(); }

  hasSourceHeader(value: string): boolean {
    const selected = this.normalizeKey(value);
    return !!selected && this.sourceHeaders().some(header => this.normalizeKey(header) === selected);
  }

  private findHeader(headers: string[], preferred: string[]): string | null {
    for (const name of preferred) {
      const match = headers.find(header => this.normalizeKey(header) === this.normalizeKey(name));
      if (match) return match;
    }
    return null;
  }

  private applyDefaultCategoryHeader(headers: string[]): boolean {
    if (this.categoryMappingExplicit) return false;
    // Never silently bind to Shopify Type when the configured Product Category
    // source column is absent; the retailer can explicitly select Type if desired.
    const categoryHeader = this.findHeader(headers, ['Product Category']);
    if (!categoryHeader || this.fieldMappings['category'] === categoryHeader) return false;
    this.fieldMappings['category'] = categoryHeader;
    return true;
  }

  private mappingDefaultsForSource(source: string, headers: string[]): Record<string, string> {
    const defaults = { ...DEFAULT_FIELD_MAPPINGS };
    if (this.isGuturGoSource(source)) {
      const sellingPrice = this.findHeader(headers, ['ERP Selling Price']);
      const supplierCost = this.findHeader(headers, ['Supplier Product Cost']);
      if (sellingPrice) defaults['sellingPrice'] = sellingPrice;
      if (supplierCost) defaults['productCost'] = supplierCost;
    }
    const category = this.findHeader(headers, ['Product Category']);
    if (category) defaults['category'] = category;
    return defaults;
  }

  private isGuturGoSource(source: string): boolean {
    const normalized = this.normalizeKey(source.trim());
    return normalized === 'GUTURGO' || normalized === 'GUTURGOMYSHOPIFYCOM';
  }

  private defaultSupplierCostTreatment(source: string): 'INCLUSIVE' | 'EXCLUSIVE' {
    return this.isGuturGoSource(source) ? 'INCLUSIVE' : 'EXCLUSIVE';
  }

  private isPricingMapping(key: string): boolean {
    return ['sellingPrice', 'mrp', 'productCost', 'packagingCost', 'shippingCost'].includes(key);
  }

  private applyUntouchedDefaults(): void {
    const defaults = this.mappingDefaultsForSource(this.sourceStore(), this.sourceHeaders());
    for (const [key, value] of Object.entries(defaults))
      if (!this.touchedFieldMappings.has(key)) this.fieldMappings[key] = value;
  }
}
