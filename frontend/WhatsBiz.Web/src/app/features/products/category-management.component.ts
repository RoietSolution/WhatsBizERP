import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
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
export class CategoryManagementComponent {
  private readonly fb = inject(FormBuilder);
  readonly columns = ['code', 'name', 'status', 'actions'];
  readonly items = signal<Category[]>([]);
  readonly flat = signal<Category[]>([]);
  editingId?: string;
  readonly form = this.fb.group({
    categoryCode: ['', Validators.required],
    categoryName: ['', Validators.required],
    description: [''],
    displayOrder: [0, [Validators.required, Validators.min(0)]],
    parentCategoryId: [null as string | null],
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
    this.api.categories().subscribe((items) => {
      this.items.set(items);
      this.flat.set(this.flatten(items));
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
  private flatten(items: Category[]): Category[] {
    return items.flatMap((item) => [item, ...this.flatten(item.children)]);
  }
}
