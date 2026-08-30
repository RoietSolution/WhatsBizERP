import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize, forkJoin, of } from 'rxjs';
import { BarcodeScanResult } from '../pos/barcode-camera.service';
import { BarcodeScannerComponent } from '../pos/barcode-scanner.component';
import { ProductApiService } from './product-api.service';
import {
  Brand,
  Category,
  ProductBarcodeInput,
  ProductImage,
  ProductInput,
  UnitOfMeasure,
} from './product.models';

@Component({
  selector: 'app-product-form',
  imports: [
    ReactiveFormsModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    BarcodeScannerComponent,
  ],
  templateUrl: './product-form.component.html',
  styles: [
    `
      header {
        display: flex;
        justify-content: space-between;
        align-items: center;
      }
      form {
        display: grid;
        gap: 1rem;
      }
      .grid {
        padding-top: 1rem;
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 0 1rem;
      }
      .wide {
        grid-column: span 3;
      }
      .checks {
        display: flex;
        gap: 1rem;
        align-items: center;
        grid-column: span 2;
      }
      .media {
        display: flex;
        gap: 1rem;
        align-items: center;
        padding-top: 1rem;
      }
      .media img {
        width: 160px;
        height: 160px;
        object-fit: contain;
        border-radius: 8px;
      }
      .gallery { display:flex; gap:12px; flex-wrap:wrap; }
      .gallery > div { display:flex; flex-direction:column; gap:4px; align-items:center; }
      .loading {
        display: grid;
        place-items: center;
        padding: 4rem;
      }
      footer {
        display: flex;
        justify-content: flex-end;
      }
      .error {
        color: var(--mat-sys-error);
      }
      .codes-header,
      .code-row,
      .scan-preview,
      .code-entry,
      .lookup-with-action,
      .quick-master {
        display: flex;
        align-items: center;
        gap: 0.75rem;
      }
      .lookup-with-action mat-form-field {
        flex: 1;
      }
      .quick-master {
        grid-column: span 3;
        align-items: flex-start;
        flex-wrap: wrap;
        padding: 0.75rem;
        margin-bottom: 1rem;
        border-radius: 8px;
        background: var(--wb-primary-soft);
      }
      .quick-master mat-form-field {
        flex: 1 1 180px;
      }
      .codes-header {
        justify-content: space-between;
        margin-bottom: 0.75rem;
      }
      .code-entry mat-form-field:first-child {
        flex: 1;
      }
      .code-row {
        justify-content: space-between;
        padding: 0.65rem 0;
        border-bottom: 1px solid var(--wb-border);
      }
      .code-value,
      .scan-value {
        min-width: 0;
        overflow: hidden;
        overflow-wrap: anywhere;
        text-overflow: ellipsis;
      }
      .code-value {
        max-width: min(70vw, 720px);
        white-space: nowrap;
      }
      .scan-preview {
        align-items: flex-start;
        flex-wrap: wrap;
        margin: 0.75rem 0 1rem;
        padding: 0.75rem;
        background: var(--wb-primary-soft);
        border-radius: 8px;
      }
      .scan-preview .scan-value {
        flex: 1 1 260px;
        max-height: 5rem;
      }
      @media (max-width: 800px) {
        .grid {
          grid-template-columns: 1fr;
        }
        .wide,
        .checks,
        .quick-master {
          grid-column: span 1;
        }
        .lookup-with-action {
          align-items: stretch;
          flex-direction: column;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductFormComponent {
  private readonly formBuilder = inject(FormBuilder);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly categories = signal<Category[]>([]);
  readonly brands = signal<Brand[]>([]);
  readonly units = signal<UnitOfMeasure[]>([]);
  readonly flatCategories = signal<Category[]>([]);
  readonly imagePreview = signal<string | null>(null);
  readonly images = signal<ProductImage[]>([]);
  readonly additionalBarcodes = signal<ProductBarcodeInput[]>([]);
  readonly scannerOpen = signal(false);
  readonly scanTarget = signal<'primary' | 'additional'>('additional');
  readonly pendingScan = signal<BarcodeScanResult | null>(null);
  readonly addingBrand = signal(false);
  readonly addingUnit = signal(false);
  readonly savingBrand = signal(false);
  readonly savingUnit = signal(false);
  readonly barcodeTypes = ['EAN13', 'EAN8', 'UPCA', 'UPCE', 'CODE128', 'CODE39', 'QR', 'CUSTOM'];
  readonly copySourceId: string | null;
  selectedImagesCount = () => this.selectedImages.length;
  readonly productId: string | null;
  private selectedImages: File[] = [];
  newBarcode = '';
  newBarcodeType = 'CUSTOM';
  quickBrandName = '';
  quickUnitName = '';
  quickUnitShortName = '';
  readonly form = this.formBuilder.group(
    {
      productCode: ['', [Validators.required, Validators.maxLength(50)]],
      barcode: [''],
      barcodeType: ['CODE128', Validators.required],
      productName: ['', [Validators.required, Validators.maxLength(250)]],
      shortDescription: [''],
      longDescription: [''],
      categoryId: ['', Validators.required],
      brandId: ['', Validators.required],
      unitId: ['', Validators.required],
      hsnCode: [''],
      sacCode: [''],
      gstPercentage: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
      purchasePrice: [0, [Validators.required, Validators.min(0)]],
      sellingPrice: [0, [Validators.required, Validators.min(0)]],
      mrp: [0, [Validators.required, Validators.min(0)]],
      minimumStock: [0, Validators.min(0)],
      maximumStock: [0, Validators.min(0)],
      reorderLevel: [0, Validators.min(0)],
      weight: [null as number | null, Validators.min(0)],
      length: [null as number | null, Validators.min(0)],
      width: [null as number | null, Validators.min(0)],
      height: [null as number | null, Validators.min(0)],
      isBatchManaged: [false],
      isSerialManaged: [false],
      isActive: [true],
      isWhatsAppVisible: [true],
    },
    {
      validators: (control) =>
        Number(control.get('sellingPrice')?.value) < Number(control.get('purchasePrice')?.value)
          ? { price: true }
          : null,
    },
  );
  constructor(
    private readonly api: ProductApiService,
    route: ActivatedRoute,
    private readonly router: Router,
    private readonly snackBar: MatSnackBar,
  ) {
    this.productId = route.snapshot.paramMap.get('id');
    this.copySourceId = this.productId ? null : route.snapshot.queryParamMap.get('copyFrom');
    forkJoin({
      categories: api.categories(),
      brands: api.brands(),
      units: api.units(),
      product: this.productId || this.copySourceId ? api.get(this.productId ?? this.copySourceId!) : of(null),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (data) => {
          this.categories.set(data.categories);
          this.flatCategories.set(this.flatten(data.categories));
          this.brands.set(data.brands.filter((x) => x.isActive));
          this.units.set(data.units.filter((x) => x.isActive));
          if (data.product) {
            this.form.patchValue(
              this.copySourceId
                ? { ...data.product, productCode: '', barcode: null, productName: `${data.product.productName} Copy` }
                : data.product,
            );
            this.additionalBarcodes.set(this.copySourceId ? [] : (data.product.additionalBarcodes ?? []));
            if (!this.copySourceId)
              this.api.images(data.product.productId).subscribe((images) => { this.images.set(images); if (images[0]) this.imagePreview.set(images[0].url); });
          }
        },
        error: () =>
          this.snackBar.open('Unable to load the product form.', 'Dismiss', { duration: 5000 }),
      });
  }
  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    const raw = this.form.getRawValue();
    const input = {
      ...raw,
      barcode: raw.barcode || null,
      additionalBarcodes: this.additionalBarcodes(),
    } as ProductInput;
    const request = this.productId
      ? this.api.update(this.productId, input)
      : this.api.create(input);
    request.subscribe({
      next: (product) => {
        const uploads = this.selectedImages.map((file) => this.api.uploadImage(product.productId, file));
        if (uploads.length)
          forkJoin(uploads).pipe(finalize(() => this.finish(product.productId))).subscribe({
            error: () =>
              this.snackBar.open('Product saved, but the image upload failed.', 'Dismiss', {
                duration: 5000,
              }),
          });
        else this.finish(product.productId);
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open(
          'Product could not be saved. Check unique codes and required fields.',
          'Dismiss',
          { duration: 5000 },
        );
      },
    });
  }
  selectImage(event: Event): void {
    const files = Array.from((event.target as HTMLInputElement).files ?? []);
    if (!files.length) return;
    const allowed = new Set(['image/jpeg', 'image/png', 'image/webp']);
    const invalid = files.find((file) => !allowed.has(file.type.toLowerCase()) || file.size > 5 * 1024 * 1024);
    if (invalid) {
      this.snackBar.open(`${invalid.name}: choose a valid JPEG, PNG, or WebP image up to 5 MB.`, 'Dismiss', { duration: 5000 });
      return;
    }
    if ((this.images().length + this.selectedImages.length + files.length) > 5) { this.snackBar.open('A product can have a maximum of 5 images.', 'Dismiss', { duration: 4000 }); return; }
    this.selectedImages.push(...files);
    const reader = new FileReader();
    reader.onload = () => this.imagePreview.set(reader.result as string);
    reader.readAsDataURL(files[0]);
  }
  deleteImage(image: ProductImage): void {
    if (!this.productId) return;
    this.api.deleteProductImage(this.productId, image.productImageId).subscribe({
      next: () => {
        this.images.update((items) => items.filter((x) => x.productImageId !== image.productImageId));
        this.imagePreview.set(this.images()[0]?.url ?? null);
        this.snackBar.open('Image deleted.', undefined, { duration: 2500 });
      },
      error: () => this.snackBar.open('Image could not be deleted.', 'Dismiss', { duration: 4000 }),
    });
  }
  openScanner(target: 'primary' | 'additional'): void {
    this.scanTarget.set(target);
    this.pendingScan.set(null);
    this.scannerOpen.set(true);
  }
  scanned(result: BarcodeScanResult): void {
    this.pendingScan.set(result);
    this.scannerOpen.set(false);
  }
  saveScanned(): void {
    const result = this.pendingScan();
    if (!result) return;
    if (this.scanTarget() === 'primary') {
      if (result.value.length > 100) {
        this.snackBar.open('Primary barcode cannot exceed 100 characters. Save it as an additional QR/code instead.', 'Dismiss', { duration: 5000 });
        return;
      }
      this.form.patchValue({ barcode: result.value, barcodeType: result.barcodeType });
      this.pendingScan.set(null);
      this.snackBar.open('Primary barcode captured.', undefined, { duration: 2500 });
      return;
    }
    if (this.addAdditional(result.value, result.barcodeType)) {
      this.pendingScan.set(null);
      this.snackBar.open('Code linked to product.', undefined, { duration: 2500 });
    }
  }
  addManualBarcode(): void {
    if (this.addAdditional(this.newBarcode, this.newBarcodeType)) {
      this.newBarcode = '';
      this.newBarcodeType = 'CUSTOM';
    }
  }
  removeBarcode(barcode: ProductBarcodeInput): void {
    this.additionalBarcodes.update((items) => items.filter((item) => item !== barcode));
  }
  createBrand(): void {
    const brandName = this.quickBrandName.trim();
    if (!brandName || this.savingBrand()) return;
    this.savingBrand.set(true);
    this.api
      .createBrand({ brandCode: '', brandName, description: '', logo: '', isActive: true })
      .pipe(finalize(() => this.savingBrand.set(false)))
      .subscribe({
        next: (brand) => {
          this.brands.update((items) => [...items, brand].sort((a, b) => a.brandName.localeCompare(b.brandName)));
          this.form.controls.brandId.setValue(brand.brandId);
          this.quickBrandName = '';
          this.addingBrand.set(false);
          this.snackBar.open('Brand added and selected.', undefined, { duration: 2500 });
        },
        error: () => this.snackBar.open('Brand could not be added.', 'Dismiss', { duration: 4000 }),
      });
  }
  createUnit(): void {
    const unitName = this.quickUnitName.trim();
    const shortName = this.quickUnitShortName.trim() || unitName.slice(0, 20).toUpperCase();
    if (!unitName || this.savingUnit()) return;
    this.savingUnit.set(true);
    this.api
      .createUnit({ unitCode: '', unitName, shortName, decimalPlaces: 0, isActive: true })
      .pipe(finalize(() => this.savingUnit.set(false)))
      .subscribe({
        next: (unit) => {
          this.units.update((items) => [...items, unit].sort((a, b) => a.unitName.localeCompare(b.unitName)));
          this.form.controls.unitId.setValue(unit.unitId);
          this.quickUnitName = '';
          this.quickUnitShortName = '';
          this.addingUnit.set(false);
          this.snackBar.open('Unit added and selected.', undefined, { duration: 2500 });
        },
        error: () => this.snackBar.open('Unit could not be added.', 'Dismiss', { duration: 4000 }),
      });
  }
  private addAdditional(value: string, barcodeType: string): boolean {
    if (!value || !value.trim()) return false;
    if (value.length > 450) {
      this.snackBar.open('Additional barcode/QR content cannot exceed 450 characters.', 'Dismiss', { duration: 5000 });
      return false;
    }
    if (this.form.controls.barcode.value === value || this.additionalBarcodes().some((x) => x.barcode === value)) {
      this.snackBar.open('This code is already linked to the current product.', undefined, { duration: 3000 });
      return false;
    }
    if (this.additionalBarcodes().length >= 10) {
      this.snackBar.open('A product can have a maximum of 10 additional codes.', 'Dismiss', { duration: 4000 });
      return false;
    }
    this.additionalBarcodes.update((items) => [...items, { barcode: value, barcodeType }]);
    return true;
  }
  private flatten(items: Category[]): Category[] {
    return items.flatMap((item) => [item, ...this.flatten(item.children)]);
  }
  private finish(id: string): void {
    this.saving.set(false);
    this.snackBar.open('Product saved.', undefined, { duration: 2500 });
    void this.router.navigate(['/products', id]);
  }
}
