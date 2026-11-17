import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatDialog } from '@angular/material/dialog';
import { PermissionService } from '../../core/services/permission.service';
import { SupplierApiService } from './supplier-api.service';
import { Supplier } from './supplier.models';
import { SupplierCatalogImportDialogComponent } from '../products/supplier-catalog-import-dialog.component';
@Component({
  imports: [RouterLink, MatButtonModule, MatTabsModule],
  templateUrl: './supplier-view.component.html',
  styles: [
    `
      header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 1rem;
      }
      .actions { display:flex; flex-wrap:wrap; gap:.5rem; justify-content:flex-end; }
      mat-tab-group {
        margin-top: 1rem;
      }
      dl {
        display: grid;
        grid-template-columns: 150px 1fr;
        gap: 1rem;
        padding: 1rem;
      }
      dd {
        margin: 0;
      }
      .upload {
        display: flex;
        gap: 1rem;
        padding: 1rem;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SupplierViewComponent {
  readonly supplier = signal<Supplier | null>(null);
  readonly canImportSupplierCatalog: boolean;
  private readonly id: string;
  constructor(
    private readonly api: SupplierApiService,
    route: ActivatedRoute,
    private readonly snack: MatSnackBar,
    private readonly dialog: MatDialog,
    private readonly permissions: PermissionService,
  ) {
    this.canImportSupplierCatalog = this.permissions.has('product.create');
    this.id = route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }
  load() {
    this.api.get(this.id).subscribe((x) => this.supplier.set(x));
  }
  openSupplierCatalogImport() {
    this.dialog.open(SupplierCatalogImportDialogComponent, { width: '1100px', maxWidth: 'calc(100vw - 24px)', autoFocus: false });
  }
  upload(type: string, input: HTMLInputElement) {
    const f = input.files?.[0];
    if (f)
      this.api.upload(this.id, type, f).subscribe(() => {
        input.value = '';
        this.load();
      });
  }
  preview(did: string, name: string) {
    this.api.document(this.id, did).subscribe((b) => {
      const u = URL.createObjectURL(b);
      window.open(u, '_blank');
      setTimeout(() => URL.revokeObjectURL(u), 60000);
    });
  }
  remove(did: string) {
    this.api.deleteDocument(this.id, did).subscribe(() => this.load());
  }
}
