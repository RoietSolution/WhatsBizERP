import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
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
  templateUrl: './physical-stock-verification.component.html',
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
          grid-column: auto;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PhysicalStockVerificationComponent {
  readonly products = signal<ProductOption[]>([]);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly saving = signal(false);
  readonly form;
  constructor(
    private readonly api: StockControlApiService,
    fb: FormBuilder,
    private readonly snack: MatSnackBar,
  ) {
    this.form = fb.group({
      warehouseId: ['', Validators.required],
      barcode: [''],
      productId: ['', Validators.required],
      countedQuantity: [0, [Validators.required, Validators.min(0)]],
      unitCost: [0, Validators.min(0)],
      approvalStatus: ['PENDING', Validators.required],
      remarks: [''],
    });
    api.products().subscribe((x) => this.products.set(x.items));
    api.warehouses().subscribe((x) => this.warehouses.set(x));
  }
  selectBarcode() {
    const value = this.form.controls.barcode.value?.trim().toLowerCase();
    const product = this.products().find((x) => x.productCode.toLowerCase() === value);
    if (product) this.form.controls.productId.setValue(product.productId);
  }
  save() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.api
      .verify({
        warehouseId: v.warehouseId,
        verificationDate: new Date().toISOString(),
        approvalStatus: v.approvalStatus,
        remarks: v.remarks,
        items: [
          {
            productId: v.productId,
            zoneId: null,
            binId: null,
            batchNo: null,
            serialNo: null,
            countedQuantity: v.countedQuantity,
            unitCost: v.unitCost,
          },
        ],
      })
      .subscribe({
        next: (x) => {
          this.saving.set(false);
          this.snack.open(`Verification ${x.number} saved.`, undefined, { duration: 3000 });
        },
        error: () => this.saving.set(false),
      });
  }
}
