import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { ProductApiService } from './product-api.service';
import { Brand } from './product.models';
import { finalize } from 'rxjs';

@Component({
  selector: 'app-brand-management',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
  ],
  templateUrl: './brand-management.component.html',
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
      }
      article {
        display: flex;
        align-items: center;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
        padding: 0.75rem;
      }
      article div {
        display: grid;
        flex: 1;
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
export class BrandManagementComponent {
  private readonly fb = inject(FormBuilder);
  readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  readonly importing = signal(false);
  readonly items = signal<Brand[]>([]);
  editingId?: string;
  readonly form = this.fb.group({
    brandCode: ['', Validators.required],
    brandName: ['', Validators.required],
    description: [''],
    logo: [''],
    isActive: [true],
  });
  constructor(
    private readonly api: ProductApiService,
    private readonly snack: MatSnackBar,
    private readonly dialog: MatDialog,
  ) {
    this.load();
  }
  load(): void {
    this.api.brands().subscribe((items) => this.items.set(items));
  }
  save(): void {
    if (this.form.invalid) return;
    const input = this.form.getRawValue() as Omit<Brand, 'brandId'>;
    (this.editingId
      ? this.api.updateBrand(this.editingId, input)
      : this.api.createBrand(input)
    ).subscribe({
      next: () => {
        this.snack.open('Brand saved.', undefined, { duration: 2000 });
        this.reset();
        this.load();
      },
      error: () => this.snack.open('Brand could not be saved.', 'Dismiss', { duration: 4000 }),
    });
  }
  edit(item: Brand): void {
    this.editingId = item.brandId;
    this.form.patchValue(item);
  }
  reset(): void {
    this.editingId = undefined;
    this.form.reset({ brandCode: '', brandName: '', description: '', logo: '', isActive: true });
  }
  remove(item: Brand): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: { title: 'Delete brand', message: `Delete ${item.brandName}?` },
      })
      .afterClosed()
      .subscribe((ok) => {
        if (ok)
          this.api.deleteBrand(item.brandId).subscribe({
            next: () => this.load(),
            error: () =>
              this.snack.open('Brand is in use and cannot be deleted.', 'Dismiss', {
                duration: 4000,
              }),
          });
      });
  }
  export(): void { this.api.exportBrands().subscribe((file) => download(file, 'brands.xlsx')); }
  template(): void { this.api.brandTemplate().subscribe((file) => download(file, 'brand-import-template.xlsx')); }
  upload(event: Event): void {
    const input = event.target as HTMLInputElement; const file = input.files?.[0]; if (!file) return;
    this.importing.set(true);
    this.api.importBrands(file).pipe(finalize(() => { this.importing.set(false); input.value = ''; })).subscribe({
      next: (result) => { const suffix = result.errors.length ? ` ${result.errors.length} row(s) skipped.` : ''; this.snack.open(`Imported ${result.importedCount} brand(s).${suffix}`, 'Close', { duration: 5000 }); this.load(); },
      error: () => this.snack.open('Brand import failed.', 'Dismiss', { duration: 4000 }),
    });
  }
}

function download(file: Blob, name: string): void { const url = URL.createObjectURL(file); const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; anchor.click(); URL.revokeObjectURL(url); }
