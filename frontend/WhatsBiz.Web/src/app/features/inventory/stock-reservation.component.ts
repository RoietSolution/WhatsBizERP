import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { InventoryApiService } from './inventory-api.service';
import { ProductOption, Reservation, WarehouseOption } from './inventory.models';
import { WarehouseUiVisibilityService } from '../../shared/services/warehouse-ui-visibility.service';
@Component({
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './stock-reservation.component.html',
  styles: [
    `
      .grid {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
        max-width: 1100px;
      }
      section {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 0.75rem;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      .location-message { grid-column: 1 / -1; margin: 0; padding: .65rem .75rem; color: var(--wb-text-secondary); background: var(--wb-background); border: 1px solid var(--wb-border); border-radius: 8px; }
      @media (max-width: 800px) {
        .grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockReservationComponent {
  readonly products = signal<ProductOption[]>([]);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly reservations = signal<Reservation[]>([]);
  readonly manualLocationChoice = signal(false);
  readonly locationUnavailable = signal(false);
  readonly form;
  constructor(
    private readonly api: InventoryApiService,
    fb: FormBuilder,
    private readonly snack: MatSnackBar,
    private readonly warehouseUi: WarehouseUiVisibilityService,
  ) {
    this.form = fb.group({
      action: ['RESERVE'],
      stockReservationId: [null],
      productId: ['', Validators.required],
      warehouseId: ['', Validators.required],
      quantity: [1, Validators.min(0.0001)],
      reservationReason: ['', Validators.required],
      referenceType: [''],
      referenceId: [null],
    });
    api.products().subscribe((x) => this.products.set(x.items));
    api.warehouses().subscribe({
      next: (x) => {
        this.warehouses.set(x);
        const resolved = this.warehouseUi.resolveDefaultOrOnly(x);
        if (resolved) this.form.controls.warehouseId.setValue(resolved);
        this.manualLocationChoice.set(this.warehouseUi.showOptionalSelectors || (x.length > 1 && !resolved));
        this.locationUnavailable.set(x.length === 0);
      },
      error: () => this.locationUnavailable.set(true),
    });
    this.load();
  }
  load() {
    this.api.reservations().subscribe((x) => this.reservations.set(x));
  }
  save() {
    if (this.form.invalid) return;
    this.api.reserve(this.form.getRawValue()).subscribe((x) => {
      this.snack.open(`Reservation ${x.number} created.`, undefined, { duration: 3000 });
      this.load();
    });
  }
  release(x: Reservation) {
    this.api
      .reserve({
        action: 'RELEASE',
        stockReservationId: x.stockReservationId,
        quantity: x.quantity - x.releasedQuantity,
      })
      .subscribe(() => {
        this.snack.open('Reservation released.', undefined, { duration: 3000 });
        this.load();
      });
  }
}
