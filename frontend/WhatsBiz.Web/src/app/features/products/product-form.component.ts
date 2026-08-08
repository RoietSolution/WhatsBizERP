import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { ProductApiService } from './product-api.service';
import { Brand, Category, ProductInput, UnitOfMeasure } from './product.models';

@Component({
  selector: 'app-product-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
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
      @media (max-width: 800px) {
        .grid {
          grid-template-columns: 1fr;
        }
        .wide,
        .checks {
          grid-column: span 1;
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
  readonly productId: string | null;
  private selectedImage?: File;
  readonly form = this.formBuilder.group(
    {
      productCode: ['', [Validators.required, Validators.maxLength(50)]],
      barcode: [''],
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
    forkJoin({
      categories: api.categories(),
      brands: api.brands(),
      units: api.units(),
      product: this.productId ? api.get(this.productId) : of(null),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (data) => {
          this.categories.set(data.categories);
          this.flatCategories.set(this.flatten(data.categories));
          this.brands.set(data.brands.filter((x) => x.isActive));
          this.units.set(data.units.filter((x) => x.isActive));
          if (data.product) {
            this.form.patchValue(data.product);
            if (data.product.imageUrl)
              this.api
                .image(data.product.productId)
                .subscribe((blob) => this.imagePreview.set(URL.createObjectURL(blob)));
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
    const input = this.form.getRawValue() as ProductInput;
    const request = this.productId
      ? this.api.update(this.productId, input)
      : this.api.create(input);
    request.subscribe({
      next: (product) => {
        const upload = this.selectedImage
          ? this.api.uploadImage(product.productId, this.selectedImage)
          : null;
        if (upload)
          upload.pipe(finalize(() => this.finish(product.productId))).subscribe({
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
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.selectedImage = file;
    const reader = new FileReader();
    reader.onload = () => this.imagePreview.set(reader.result as string);
    reader.readAsDataURL(file);
  }
  deleteImage(): void {
    if (!this.productId) return;
    this.api.deleteImage(this.productId).subscribe({
      next: () => {
        this.imagePreview.set(null);
        this.selectedImage = undefined;
        this.snackBar.open('Image deleted.', undefined, { duration: 2500 });
      },
      error: () => this.snackBar.open('Image could not be deleted.', 'Dismiss', { duration: 4000 }),
    });
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
