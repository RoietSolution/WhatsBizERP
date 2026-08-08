import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
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
}
