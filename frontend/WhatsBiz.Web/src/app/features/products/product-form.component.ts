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
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, MatSelectModule],
  template: `
    <header><div><h1>{{ productId ? 'Edit product' : 'Create product' }}</h1><p>Fields marked with * are required.</p></div><a mat-button routerLink="/products">Cancel</a></header>
    @if (loading()) { <div class="loading"><mat-spinner /></div> } @else {
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-card><mat-card-header><mat-card-title>Identity</mat-card-title></mat-card-header><mat-card-content class="grid">
        <mat-form-field><mat-label>Product code</mat-label><input matInput formControlName="productCode" maxlength="50" required></mat-form-field>
        <mat-form-field><mat-label>Barcode</mat-label><input matInput formControlName="barcode" maxlength="100"></mat-form-field>
        <mat-form-field class="wide"><mat-label>Product name</mat-label><input matInput formControlName="productName" maxlength="250" required></mat-form-field>
        <mat-form-field><mat-label>Category</mat-label><mat-select formControlName="categoryId" required>@for (item of flatCategories(); track item.productCategoryId) { <mat-option [value]="item.productCategoryId">{{ item.categoryName }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field><mat-label>Brand</mat-label><mat-select formControlName="brandId" required>@for (item of brands(); track item.brandId) { <mat-option [value]="item.brandId">{{ item.brandName }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field><mat-label>Unit</mat-label><mat-select formControlName="unitId" required>@for (item of units(); track item.unitId) { <mat-option [value]="item.unitId">{{ item.unitName }} ({{ item.shortName }})</mat-option> }</mat-select></mat-form-field>
        <mat-form-field class="wide"><mat-label>Short description</mat-label><textarea matInput formControlName="shortDescription"></textarea></mat-form-field>
        <mat-form-field class="wide"><mat-label>Long description</mat-label><textarea matInput formControlName="longDescription" rows="4"></textarea></mat-form-field>
      </mat-card-content></mat-card>
      <mat-card><mat-card-header><mat-card-title>Tax and pricing</mat-card-title></mat-card-header><mat-card-content class="grid">
        <mat-form-field><mat-label>HSN code</mat-label><input matInput formControlName="hsnCode"></mat-form-field><mat-form-field><mat-label>SAC code</mat-label><input matInput formControlName="sacCode"></mat-form-field><mat-form-field><mat-label>GST %</mat-label><input matInput type="number" formControlName="gstPercentage" min="0" max="100"></mat-form-field>
        <mat-form-field><mat-label>Purchase price</mat-label><input matInput type="number" formControlName="purchasePrice" min="0"></mat-form-field><mat-form-field><mat-label>Selling price</mat-label><input matInput type="number" formControlName="sellingPrice" min="0"></mat-form-field><mat-form-field><mat-label>MRP</mat-label><input matInput type="number" formControlName="mrp" min="0"></mat-form-field>
      </mat-card-content></mat-card>
      <mat-card><mat-card-header><mat-card-title>Inventory and dimensions</mat-card-title></mat-card-header><mat-card-content class="grid">
        <mat-form-field><mat-label>Minimum stock</mat-label><input matInput type="number" formControlName="minimumStock" min="0"></mat-form-field><mat-form-field><mat-label>Maximum stock</mat-label><input matInput type="number" formControlName="maximumStock" min="0"></mat-form-field><mat-form-field><mat-label>Reorder level</mat-label><input matInput type="number" formControlName="reorderLevel" min="0"></mat-form-field>
        <mat-form-field><mat-label>Weight</mat-label><input matInput type="number" formControlName="weight" min="0"></mat-form-field><mat-form-field><mat-label>Length</mat-label><input matInput type="number" formControlName="length" min="0"></mat-form-field><mat-form-field><mat-label>Width</mat-label><input matInput type="number" formControlName="width" min="0"></mat-form-field><mat-form-field><mat-label>Height</mat-label><input matInput type="number" formControlName="height" min="0"></mat-form-field>
        <div class="checks"><mat-checkbox formControlName="isBatchManaged">Batch managed</mat-checkbox><mat-checkbox formControlName="isSerialManaged">Serial managed</mat-checkbox><mat-checkbox formControlName="isActive">Active</mat-checkbox></div>
      </mat-card-content></mat-card>
      <mat-card><mat-card-header><mat-card-title>Product image</mat-card-title></mat-card-header><mat-card-content class="media">@if (imagePreview()) { <img [src]="imagePreview()" alt="Product image preview"> }<div><button type="button" mat-stroked-button (click)="imageInput.click()">{{ imagePreview() ? 'Replace image' : 'Choose image' }}</button><input #imageInput hidden type="file" accept="image/png,image/jpeg,image/webp" (change)="selectImage($event)">@if (productId && imagePreview()) { <button type="button" mat-button color="warn" (click)="deleteImage()">Delete image</button> }</div></mat-card-content></mat-card>
      @if (form.hasError('price')) { <p class="error">Selling price must be greater than or equal to purchase price.</p> }
      <footer><button mat-flat-button type="submit" [disabled]="form.invalid || saving()">@if (saving()) { <mat-spinner diameter="20" /> } @else { Save product }</button></footer>
    </form> }`,
  styles: [`header{display:flex;justify-content:space-between;align-items:center}form{display:grid;gap:1rem}.grid{padding-top:1rem;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:0 1rem}.wide{grid-column:span 3}.checks{display:flex;gap:1rem;align-items:center;grid-column:span 2}.media{display:flex;gap:1rem;align-items:center;padding-top:1rem}.media img{width:160px;height:160px;object-fit:contain;border-radius:8px}.loading{display:grid;place-items:center;padding:4rem}footer{display:flex;justify-content:flex-end}.error{color:var(--mat-sys-error)}@media(max-width:800px){.grid{grid-template-columns:1fr}.wide,.checks{grid-column:span 1}}`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProductFormComponent {
  private readonly formBuilder = inject(FormBuilder);
  readonly loading = signal(true); readonly saving = signal(false); readonly categories = signal<Category[]>([]); readonly brands = signal<Brand[]>([]); readonly units = signal<UnitOfMeasure[]>([]); readonly flatCategories = signal<Category[]>([]); readonly imagePreview = signal<string | null>(null); readonly productId: string | null; private selectedImage?: File;
  readonly form = this.formBuilder.group({ productCode: ['', [Validators.required, Validators.maxLength(50)]], barcode: [''], productName: ['', [Validators.required, Validators.maxLength(250)]], shortDescription: [''], longDescription: [''], categoryId: ['', Validators.required], brandId: ['', Validators.required], unitId: ['', Validators.required], hsnCode: [''], sacCode: [''], gstPercentage: [0, [Validators.required, Validators.min(0), Validators.max(100)]], purchasePrice: [0, [Validators.required, Validators.min(0)]], sellingPrice: [0, [Validators.required, Validators.min(0)]], mrp: [0, [Validators.required, Validators.min(0)]], minimumStock: [0, Validators.min(0)], maximumStock: [0, Validators.min(0)], reorderLevel: [0, Validators.min(0)], weight: [null as number | null, Validators.min(0)], length: [null as number | null, Validators.min(0)], width: [null as number | null, Validators.min(0)], height: [null as number | null, Validators.min(0)], isBatchManaged: [false], isSerialManaged: [false], isActive: [true] }, { validators: control => Number(control.get('sellingPrice')?.value) < Number(control.get('purchasePrice')?.value) ? { price: true } : null });
  constructor(private readonly api: ProductApiService, route: ActivatedRoute, private readonly router: Router, private readonly snackBar: MatSnackBar) { this.productId = route.snapshot.paramMap.get('id'); forkJoin({ categories: api.categories(), brands: api.brands(), units: api.units(), product: this.productId ? api.get(this.productId) : of(null) }).pipe(finalize(() => this.loading.set(false))).subscribe({ next: data => { this.categories.set(data.categories); this.flatCategories.set(this.flatten(data.categories)); this.brands.set(data.brands.filter(x => x.isActive)); this.units.set(data.units.filter(x => x.isActive)); if (data.product) { this.form.patchValue(data.product); if (data.product.imageUrl) this.api.image(data.product.productId).subscribe(blob => this.imagePreview.set(URL.createObjectURL(blob))); } }, error: () => this.snackBar.open('Unable to load the product form.', 'Dismiss', { duration: 5000 }) }); }
  save(): void { if (this.form.invalid) { this.form.markAllAsTouched(); return; } this.saving.set(true); const input = this.form.getRawValue() as ProductInput; const request = this.productId ? this.api.update(this.productId, input) : this.api.create(input); request.subscribe({ next: product => { const upload = this.selectedImage ? this.api.uploadImage(product.productId, this.selectedImage) : null; if (upload) upload.pipe(finalize(() => this.finish(product.productId))).subscribe({ error: () => this.snackBar.open('Product saved, but the image upload failed.', 'Dismiss', { duration: 5000 }) }); else this.finish(product.productId); }, error: () => { this.saving.set(false); this.snackBar.open('Product could not be saved. Check unique codes and required fields.', 'Dismiss', { duration: 5000 }); } }); }
  selectImage(event: Event): void { const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return; this.selectedImage = file; const reader = new FileReader(); reader.onload = () => this.imagePreview.set(reader.result as string); reader.readAsDataURL(file); }
  deleteImage(): void { if (!this.productId) return; this.api.deleteImage(this.productId).subscribe({ next: () => { this.imagePreview.set(null); this.selectedImage = undefined; this.snackBar.open('Image deleted.', undefined, { duration: 2500 }); }, error: () => this.snackBar.open('Image could not be deleted.', 'Dismiss', { duration: 4000 }) }); }
  private flatten(items: Category[]): Category[] { return items.flatMap(item => [item, ...this.flatten(item.children)]); }
  private finish(id: string): void { this.saving.set(false); this.snackBar.open('Product saved.', undefined, { duration: 2500 }); void this.router.navigate(['/products', id]); }
}
