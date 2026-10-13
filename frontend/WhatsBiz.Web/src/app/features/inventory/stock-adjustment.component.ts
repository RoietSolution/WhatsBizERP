import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { StockControlApiService } from './stock-control-api.service';
import { ProductOption, WarehouseOption } from './inventory.models';
@Component({
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './stock-adjustment.component.html',
  styles: [
    `
      .grid {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
        max-width: 1100px;
      }
      .wide {
        grid-column: span 3;
      }
      @media (max-width: 800px) {
        .grid {
          grid-template-columns: 1fr;
        }
        .wide {
          grid-column: span 1;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockAdjustmentComponent {
  readonly products = signal<ProductOption[]>([]);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly reasons = ['DAMAGE', 'EXPIRY', 'LOST', 'FOUND', 'INTERNAL_CONSUMPTION', 'PHYSICAL_VERIFICATION', 'OTHER'];
  readonly form;
  readonly saving = signal(false);
  constructor(
    private readonly api: StockControlApiService,
    fb: FormBuilder,
    private readonly snack: MatSnackBar,
  ) {
    this.form = fb.group({
      productId: ['', Validators.required],
      warehouseId: ['', Validators.required],
      zoneId: [null],
      binId: [null],
      batchNo: [''],
      serialNo: [''],
      quantity: [1, Validators.min(0.0001)],
      unitCost: [0, Validators.min(0)],
      adjustmentType: ['INCREASE', Validators.required],
      reasonCode: ['OTHER', Validators.required],
      remarks: [''],
    });
    api.products().subscribe({ next: (x) => this.products.set(x.items), error: () => this.snack.open('Products could not be loaded.', 'Dismiss', { duration: 4000 }) });
    api.warehouses().subscribe({ next: (x) => this.warehouses.set(x), error: () => this.snack.open('Warehouses could not be loaded.', 'Dismiss', { duration: 4000 }) });
  }
  save() {
    if (this.form.invalid || this.saving()) { this.form.markAllAsTouched(); return; }
    this.saving.set(true);
    const v = this.form.getRawValue();
    this.api.adjust({
      warehouseId: v.warehouseId,
      adjustmentType: v.adjustmentType,
      reasonCode: v.reasonCode,
      approvalStatus: 'APPROVED',
      remarks: v.remarks,
      items: [{
        productId: v.productId,
        sourceZoneId: v.zoneId,
        sourceBinId: v.binId,
        destinationZoneId: null,
        destinationBinId: null,
        batchNo: v.batchNo || null,
        serialNo: v.serialNo || null,
        quantity: v.quantity,
        unitCost: v.unitCost,
      }],
    }).subscribe({
      next: (x) => { this.snack.open(`Adjustment ${x.number} posted.`, undefined, { duration: 3000 }); this.form.patchValue({ quantity: 1, remarks: '' }); this.saving.set(false); },
      error: (error: HttpErrorResponse) => { this.saving.set(false); this.snack.open(this.adjustmentError(error), 'Dismiss', { duration: 6000 }); },
    });
  }
  private adjustmentError(error: HttpErrorResponse): string { const detail = error.error?.detail || error.error?.title; if (detail) return detail; if (error.status === 409) return 'The stock adjustment conflicts with current stock or warehouse data. Refresh and try again.'; if (error.status === 504) return 'The adjustment request timed out. Check the adjustment history before retrying.'; return 'The stock adjustment could not be posted.'; }
}
