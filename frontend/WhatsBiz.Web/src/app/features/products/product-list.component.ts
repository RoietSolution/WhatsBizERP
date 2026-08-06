import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { finalize } from 'rxjs';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { ProductApiService } from './product-api.service';
import { ProductListItem } from './product.models';

@Component({
  selector: 'app-product-list',
  imports: [DecimalPipe, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressSpinnerModule, MatSelectModule, MatSortModule, MatTableModule],
  template: `
    <header><div><h1>Products</h1><p>Manage product catalog, pricing, inventory attributes, and media.</p></div><a mat-flat-button routerLink="/products/new"><mat-icon>add</mat-icon>New product</a></header>
    <section class="toolbar">
      <mat-form-field><mat-label>Search</mat-label><input matInput [formControl]="search" (keyup.enter)="reload()"><mat-icon matSuffix>search</mat-icon></mat-form-field>
      <mat-form-field><mat-label>Status</mat-label><mat-select [formControl]="status" (selectionChange)="reload()"><mat-option value="all">All</mat-option><mat-option value="active">Active</mat-option><mat-option value="inactive">Inactive</mat-option></mat-select></mat-form-field>
      <button mat-stroked-button (click)="exportProducts()"><mat-icon>download</mat-icon>Export</button>
      <button mat-stroked-button (click)="downloadTemplate()">Template</button>
      <button mat-stroked-button (click)="fileInput.click()"><mat-icon>upload</mat-icon>Import</button><input #fileInput hidden type="file" accept=".xlsx" (change)="importProducts($event)">
    </section>
    @if (loading()) { <div class="loading"><mat-spinner diameter="40" /></div> }
    <div class="table-wrap">
      <table mat-table matSort (matSortChange)="sortChanged($event)" [dataSource]="items()">
        <ng-container matColumnDef="productCode"><th mat-header-cell *matHeaderCellDef mat-sort-header>Code</th><td mat-cell *matCellDef="let item">{{ item.productCode }}</td></ng-container>
        <ng-container matColumnDef="productName"><th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th><td mat-cell *matCellDef="let item"><a [routerLink]="['/products', item.productId]">{{ item.productName }}</a></td></ng-container>
        <ng-container matColumnDef="categoryName"><th mat-header-cell *matHeaderCellDef mat-sort-header>Category</th><td mat-cell *matCellDef="let item">{{ item.categoryName }}</td></ng-container>
        <ng-container matColumnDef="brandName"><th mat-header-cell *matHeaderCellDef>Brand</th><td mat-cell *matCellDef="let item">{{ item.brandName }}</td></ng-container>
        <ng-container matColumnDef="sellingPrice"><th mat-header-cell *matHeaderCellDef mat-sort-header>Selling price</th><td mat-cell *matCellDef="let item">{{ item.sellingPrice | number:'1.2-2' }}</td></ng-container>
        <ng-container matColumnDef="isActive"><th mat-header-cell *matHeaderCellDef>Status</th><td mat-cell *matCellDef="let item"><span [class.inactive]="!item.isActive">{{ item.isActive ? 'Active' : 'Inactive' }}</span></td></ng-container>
        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let item"><a mat-icon-button [routerLink]="['/products', item.productId, 'edit']" aria-label="Edit"><mat-icon>edit</mat-icon></a><button mat-icon-button (click)="remove(item)" aria-label="Delete"><mat-icon>delete</mat-icon></button></td></ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr><tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>
      @if (!loading() && items().length === 0) { <p class="empty">No products match the current filters.</p> }
    </div>
    <mat-paginator [length]="total()" [pageIndex]="pageNumber - 1" [pageSize]="pageSize" [pageSizeOptions]="[10,20,50,100]" (page)="pageChanged($event)" />`,
  styles: [`header{display:flex;justify-content:space-between;gap:1rem;align-items:center}h1{margin-bottom:.25rem}.toolbar{display:flex;flex-wrap:wrap;align-items:center;gap:.75rem;margin:1rem 0}.toolbar mat-form-field{min-width:180px}.table-wrap{overflow:auto;background:var(--mat-sys-surface-container-low);border-radius:12px}table{width:100%;min-width:850px}.loading{display:grid;place-items:center;padding:2rem}.empty{text-align:center;padding:2rem}.inactive{color:var(--mat-sys-error)}@media(max-width:600px){header{align-items:flex-start;flex-direction:column}.toolbar>*{flex:1 1 100%}}`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProductListComponent {
  readonly columns = ['productCode', 'productName', 'categoryName', 'brandName', 'sellingPrice', 'isActive', 'actions']; readonly items = signal<ProductListItem[]>([]); readonly total = signal(0); readonly loading = signal(false); readonly search = new FormControl('', { nonNullable: true }); readonly status = new FormControl('all', { nonNullable: true }); pageNumber = 1; pageSize = 20; sortBy = 'productName'; descending = false;
  constructor(private readonly api: ProductApiService, private readonly snackBar: MatSnackBar, private readonly dialog: MatDialog) { this.reload(); }
  reload(): void { this.loading.set(true); const isActive = this.status.value === 'all' ? undefined : this.status.value === 'active'; this.api.search({ search: this.search.value.trim() || undefined, isActive, sortBy: this.sortBy, descending: this.descending, pageNumber: this.pageNumber, pageSize: this.pageSize }).pipe(finalize(() => this.loading.set(false))).subscribe({ next: result => { this.items.set(result.items); this.total.set(result.totalCount); }, error: () => this.snackBar.open('Unable to load products.', 'Dismiss', { duration: 4000 }) }); }
  pageChanged(event: PageEvent): void { this.pageNumber = event.pageIndex + 1; this.pageSize = event.pageSize; this.reload(); }
  sortChanged(event: Sort): void { this.sortBy = event.active || 'productName'; this.descending = event.direction === 'desc'; this.pageNumber = 1; this.reload(); }
  remove(item: ProductListItem): void { this.dialog.open(ConfirmDialogComponent, { data: { title: 'Delete product', message: `Delete ${item.productName}? This action is recorded as a soft delete.` } }).afterClosed().subscribe(confirmed => { if (confirmed) this.api.delete(item.productId).subscribe({ next: () => { this.snackBar.open('Product deleted.', undefined, { duration: 2500 }); this.reload(); }, error: () => this.snackBar.open('Product could not be deleted.', 'Dismiss', { duration: 4000 }) }); }); }
  exportProducts(): void { const active = this.status.value === 'all' ? undefined : this.status.value === 'active'; this.api.export(this.search.value.trim() || undefined, active).subscribe(blob => this.download(blob, 'products.xlsx')); }
  downloadTemplate(): void { this.api.template().subscribe(blob => this.download(blob, 'product-import-template.xlsx')); }
  importProducts(event: Event): void { const input = event.target as HTMLInputElement; const file = input.files?.[0]; if (!file) return; this.loading.set(true); this.api.import(file).pipe(finalize(() => { this.loading.set(false); input.value = ''; })).subscribe({ next: result => { this.snackBar.open(`Imported ${result.importedCount} product(s). ${result.errors.length} row(s) rejected.`, 'Dismiss', { duration: 6000 }); this.reload(); }, error: () => this.snackBar.open('Product import failed.', 'Dismiss', { duration: 5000 }) }); }
  private download(blob: Blob, name: string): void { const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; anchor.click(); URL.revokeObjectURL(url); }
}
