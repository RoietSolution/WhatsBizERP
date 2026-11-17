import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogActions, MatDialogContent, MatDialogRef, MatDialogTitle } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { ShopifyRetailerImportPanelComponent } from './shopify-retailer-import-dialog.component';

@Component({
  selector: 'app-product-import-dialog',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatDialogTitle, MatDialogContent, MatDialogActions, ShopifyRetailerImportPanelComponent],
  template: `
    <h2 mat-dialog-title>Product import</h2>
    <mat-dialog-content>
      <label class="format-choice">Import format
        <select [(ngModel)]="format" aria-label="Import format">
          <option value="ERP_TEMPLATE">ERP Template (13 columns)</option>
          <option value="SHOPIFY">Shopify CSV / XLSX</option>
        </select>
      </label>
      @if (format === 'SHOPIFY') {
        <app-shopify-retailer-import-panel (imported)="ref.close('IMPORTED')" />
      } @else {
        <p class="erp-note">Continue to the existing ERP Template import. The 13-column import flow is unchanged.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="ref.close()">Close</button>
      @if (format === 'ERP_TEMPLATE') {
        <button mat-flat-button color="primary" type="button" (click)="ref.close('ERP')">Continue ERP import</button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    :host { display:block; color:var(--wb-text); }
    mat-dialog-content { max-height:78vh; }
    .format-choice { display:grid; gap:.4rem; max-width:360px; margin:0 0 1rem; font-size:.9rem; font-weight:600; }
    select { width:100%; min-height:42px; padding:.55rem .7rem; border:1px solid var(--wb-border); border-radius:8px; background:var(--wb-surface); color:var(--wb-text); font:inherit; }
    .erp-note { padding:.75rem; border-radius:8px; background:var(--wb-surface-muted); color:var(--wb-text-secondary); }
    @media(max-width:600px) { mat-dialog-actions { position:sticky; bottom:0; background:var(--wb-surface); padding-bottom:max(8px,env(safe-area-inset-bottom)); } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductImportDialogComponent {
  readonly ref = inject(MatDialogRef<ProductImportDialogComponent>);
  format: 'ERP_TEMPLATE' | 'SHOPIFY' = 'ERP_TEMPLATE';
}
