import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnDestroy, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { ProductApiService } from './product-api.service';
import { Category } from './product.models';
import { finalize } from 'rxjs';

@Component({
  selector: 'app-category-management',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTableModule,
  ],
  templateUrl: './category-management.component.html',
  styles: [
    `
      .layout {
        display: grid;
        grid-template-columns: 320px 1fr;
        gap: 1.5rem;
      }
      .page-heading,.page-actions{display:flex;align-items:center}.page-heading{justify-content:space-between;gap:1rem}.page-actions{gap:.5rem;flex-wrap:wrap}.page-actions .material-symbols-rounded{font-size:21px;margin-right:5px;vertical-align:middle}
      form {
        display: grid;
        gap: 0.25rem;
      }
      .table {
        overflow: auto;
      }
      table {
        width: 100%;
      }
      @media (max-width: 800px) {
        .layout {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoryManagementComponent implements OnDestroy {
  readonly categoryImages=signal<Record<string,string>>({});
  private readonly destroyRef=inject(DestroyRef);
  private imageGeneration=0;
  ngOnDestroy(){this.imageGeneration++;for(const url of Object.values(this.categoryImages()))URL.revokeObjectURL(url);}
  private readonly fb = inject(FormBuilder);
  readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  readonly importing = signal(false);
  readonly columns = ['code', 'name', 'image', 'status', 'actions'];
  readonly imageVersion=signal(Date.now());
  readonly items = signal<Category[]>([]);
  readonly flat = signal<Category[]>([]);
  editingId?: string;
  readonly form = this.fb.group({
    categoryCode: [''],
    categoryName: ['', Validators.required],
    description: [''],
    displayOrder: [0, [Validators.required, Validators.min(0)]],
    parentCategoryId: [null as string | null],
    isActive: [true],
  });
  constructor(
    private readonly http: HttpClient,
    private readonly api: ProductApiService,
    private readonly snack: MatSnackBar,
    private readonly dialog: MatDialog,
  ) {
    this.load();
  }
  load(): void {
    this.api.categories().subscribe((items) => {
      this.items.set(items);
      this.flat.set(this.flatten(items));
      this.loadCategoryImages(items);
    });
  }
  save(): void {
    if (this.form.invalid) return;
    const input = this.form.getRawValue() as Omit<Category, 'productCategoryId' | 'children'>;
    const request = this.editingId
      ? this.api.updateCategory(this.editingId, input)
      : this.api.createCategory(input);
    request.subscribe({
      next: () => {
        this.snack.open('Category saved.', undefined, { duration: 2000 });
        this.reset();
        this.load();
      },
      error: () => this.snack.open('Category could not be saved.', 'Dismiss', { duration: 4000 }),
    });
  }
  edit(item: Category): void {
    this.editingId = item.productCategoryId;
    this.form.patchValue(item);
  }
  reset(): void {
    this.editingId = undefined;
    this.form.reset({
      categoryCode: '',
      categoryName: '',
      description: '',
      displayOrder: 0,
      parentCategoryId: null,
      isActive: true,
    });
  }
  remove(item: Category): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: { title: 'Delete category', message: `Delete ${item.categoryName}?` },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (confirmed)
          this.api.deleteCategory(item.productCategoryId).subscribe({
            next: () => this.load(),
            error: () =>
              this.snack.open('Category is in use and cannot be deleted.', 'Dismiss', {
                duration: 4000,
              }),
          });
      });
  }
  export(): void { this.api.exportCategories().subscribe((file) => download(file, 'product-categories.xlsx')); }
  template(): void { this.api.categoryTemplate().subscribe((file) => download(file, 'product-category-import-template.xlsx')); }
  upload(event: Event): void {
    const input = event.target as HTMLInputElement; const file = input.files?.[0]; if (!file) return;
    this.importing.set(true);
    this.api.importCategories(file).pipe(finalize(() => { this.importing.set(false); input.value = ''; })).subscribe({
      next: (result) => { const suffix = result.errors.length ? ` ${result.errors.length} row(s) skipped.` : ''; this.snack.open(`Imported ${result.importedCount} categor${result.importedCount === 1 ? 'y' : 'ies'}.${suffix}`, 'Close', { duration: 5000 }); this.load(); },
      error: () => this.snack.open('Category import failed.', 'Dismiss', { duration: 4000 }),
    });
  }
  categoryImage(id:string):string|undefined{return this.categoryImages()[id];}
  private loadCategoryImages(items:Category[]):void{
    const generation=++this.imageGeneration;
    for(const url of Object.values(this.categoryImages()))URL.revokeObjectURL(url);
    this.categoryImages.set({});
    for(const item of this.flatten(items))this.http.get(`/api/storefront-administration/categories/${item.productCategoryId}/image`,{responseType:"blob"}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:blob=>{const url=URL.createObjectURL(blob);if(generation!==this.imageGeneration){URL.revokeObjectURL(url);return;}this.categoryImages.update(images=>({...images,[item.productCategoryId]:url}));},error:()=>{}});
  }
  hideBrokenImage(event:Event):void{(event.target as HTMLImageElement).style.display='none';}
  uploadCategoryImage(id:string,event:Event):void{const input=event.target as HTMLInputElement,file=input.files?.[0];if(!file)return;this.api.uploadCategoryStorefrontImage(id,file).subscribe({next:()=>{this.imageVersion.set(Date.now());input.value='';this.snack.open('Category image updated.',undefined,{duration:2000});},error:()=>this.snack.open('Category image could not be uploaded.','Dismiss',{duration:4000})});}
  removeCategoryImage(id:string):void{this.api.removeCategoryStorefrontImage(id).subscribe({next:()=>{this.imageVersion.set(Date.now());this.snack.open('Category image removed.',undefined,{duration:2000});},error:()=>this.snack.open('Category image could not be removed.','Dismiss',{duration:4000})});}
  private flatten(items: Category[]): Category[] {
    return items.flatMap((item) => [item, ...this.flatten(item.children)]);
  }
}

function download(file: Blob, name: string): void { const url = URL.createObjectURL(file); const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; anchor.click(); URL.revokeObjectURL(url); }
