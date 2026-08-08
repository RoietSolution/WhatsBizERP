import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { InventoryApiService } from './inventory-api.service';
import { ProductOption, WarehouseOption } from './inventory.models';
@Component({
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './stock-transfer.component.html',
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
export class StockTransferComponent {
  readonly products = signal<ProductOption[]>([]);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly form;
  constructor(
    private readonly api: InventoryApiService,
    fb: FormBuilder,
    private readonly snack: MatSnackBar,
  ) {
    this.form = fb.group({
      productId: ['', Validators.required],
      sourceWarehouseId: ['', Validators.required],
      destinationWarehouseId: ['', Validators.required],
      quantity: [1, Validators.min(0.0001)],
      transferDate: [new Date().toISOString().slice(0, 16), Validators.required],
      remarks: [''],
    });
    api.products().subscribe((x) => this.products.set(x.items));
    api.warehouses().subscribe((x) => this.warehouses.set(x));
  }
  save() {
    if (this.form.invalid) return;
    this.api
      .transfer(this.form.getRawValue())
      .subscribe((x) =>
        this.snack.open(`Transfer ${x.number} completed.`, undefined, { duration: 3000 }),
      );
  }
}
