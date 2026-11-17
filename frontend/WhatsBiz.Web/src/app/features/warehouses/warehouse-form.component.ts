import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { WarehouseApiService } from './warehouse-api.service';
import { WarehouseInput, WarehouseType, WarehouseStorefrontFulfilment } from './warehouse.models';
type EditableSellerArea = Omit<WarehouseStorefrontFulfilment['sellerAreas'][number], 'pincodes'> & { pincodesText: string };
@Component({
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatTabsModule,
  ],
  templateUrl: './warehouse-form.component.html',
  styles: [
    `
      header {
        display: flex;
        justify-content: space-between;
      }
      .grid {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
        padding: 1.25rem;
      }
      .wide,
      .checks {
        grid-column: span 3;
      }
      section {
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      footer {
        display: flex;
        justify-content: flex-end;
        padding: 1rem;
      }
      .fulfilment-panel { padding: 1.25rem; }
      .fulfilment-notice { padding: .875rem 1rem; border-radius: .5rem; background: var(--mat-sys-surface-container); color: var(--mat-sys-on-surface); margin: .5rem 0 1rem; }
      .fulfilment-result { display: inline-flex; align-items: center; gap: .4rem; padding: .35rem .7rem; border-radius: 999px; font-weight: 600; background: #fef3c7; color: #854d0e; }
      .fulfilment-result.eligible { background: #dcfce7; color: #166534; }
      .seller-area { border: 1px solid var(--mat-sys-outline-variant); border-radius: .75rem; padding: 1rem; margin: .75rem 0; }
      .seller-area-head { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
      .seller-area h3 { margin: 0; font-size: 1rem; }
      .seller-area textarea { min-height: 5rem; resize: vertical; }
      .area-pincodes { width: min(100%, 44rem); margin-top: .75rem; }
      .pincode-help { color: var(--mat-sys-on-surface-variant); font-size: .875rem; margin: 0 0 .75rem; }
      .eligibility-error { color: var(--mat-sys-error); margin: .5rem 0; }
      @media (max-width: 800px) {
        .grid {
          grid-template-columns: 1fr;
        }
        .wide,
        .checks {
          grid-column: span 1;
        }
        .seller-area-head { align-items: flex-start; flex-direction: column; gap: .25rem; }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WarehouseFormComponent {
  private readonly fb = inject(FormBuilder);
  readonly id: string | null;
  readonly types = signal<WarehouseType[]>([]);
  readonly saving = signal(false);
  readonly fulfilment = signal<WarehouseStorefrontFulfilment | null>(null);
  readonly fulfilmentAreas = signal<EditableSellerArea[]>([]);
  readonly fulfilmentError = signal<string | null>(null);
  readonly form = this.fb.group({
    warehouseCode: ['', Validators.required],
    warehouseName: ['', Validators.required],
    warehouseTypeId: ['', Validators.required],
    branchId: [''],
    managerName: [''],
    email: ['', Validators.email],
    phone: [''],
    mobile: [''],
    capacity: [0, Validators.min(0)],
    isDefault: [false],
    isActive: [true],
    remarks: [''],
    address: this.fb.group({
      addressId: [null as string | null],
      addressLine1: [''],
      addressLine2: [''],
      city: [''],
      district: [''],
      state: [''],
      country: ['India'],
      postalCode: [''],
    }),
    contacts: this.fb.array([]),
    zones: this.fb.array([]),
  });
  get contacts() {
    return this.form.controls.contacts as FormArray;
  }
  get zones() {
    return this.form.controls.zones as FormArray;
  }
  bins(index: number) {
    return this.zones.at(index).get('bins') as FormArray;
  }
  constructor(
    private readonly api: WarehouseApiService,
    route: ActivatedRoute,
    private readonly router: Router,
    private readonly snack: MatSnackBar,
  ) {
    this.id = route.snapshot.paramMap.get('id');
    forkJoin({
      types: api.types(),
      warehouse: this.id ? api.get(this.id) : of(null),
      fulfilment: this.id ? api.getStorefrontFulfilment(this.id).pipe(catchError(() => of(null))) : of(null),
    }).subscribe(
      (x) => {
        this.types.set(x.types);
        this.fulfilment.set(x.fulfilment);
        this.fulfilmentAreas.set((x.fulfilment?.sellerAreas ?? []).map((area) => ({ ...area, pincodesText: area.pincodes.join('\n') })));
        if (this.id && !x.fulfilment) this.fulfilmentError.set('Warehouse fulfilment settings could not be loaded. Retry the page before changing them.');
        if (x.warehouse) {
          this.form.patchValue({ ...x.warehouse, address: x.warehouse.address });
          for (const contact of x.warehouse.contacts) this.addContact(contact);
          for (const zone of x.warehouse.zones) {
            this.addZone(zone);
            const index = this.zones.length - 1;
            for (const bin of zone.bins) this.addBin(index, bin);
          }
        } else {
          this.addContact();
          this.addZone();
        }
      },
    );
  }
  addContact(value?: Record<string, unknown>) {
    const group = this.fb.group({
      contactId: [null as string | null],
      contactPerson: ['', Validators.required],
      designation: [''],
      mobile: [''],
      email: ['', Validators.email],
      isPrimary: [false],
    });
    if (value) group.patchValue(value);
    this.contacts.push(group);
  }
  addZone(value?: Record<string, unknown>) {
    const group = this.fb.group({
      zoneId: [null as string | null],
      zoneCode: ['', Validators.required],
      zoneName: ['', Validators.required],
      description: [''],
      isActive: [true],
      bins: this.fb.array([]),
    });
    if (value) group.patchValue(value);
    this.zones.push(group);
  }
  addBin(zoneIndex: number, value?: Record<string, unknown>) {
    const group = this.fb.group({
      binId: [null as string | null],
      binCode: ['', Validators.required],
      binName: ['', Validators.required],
      maximumCapacity: [0, Validators.min(0)],
      isActive: [true],
    });
    if (value) group.patchValue(value);
    this.bins(zoneIndex).push(group);
  }
  updateFulfilmentArea(index: number, patch: Partial<EditableSellerArea>) {
    this.fulfilmentAreas.update((areas) => areas.map((area, i) => i === index ? { ...area, ...patch } : area));
  }
  parsePincodes(value: string): string[] {
    return [...new Set(value.split(/[\s,;]+/).map((x) => x.trim()).filter(Boolean))];
  }
  private saveFulfilment(warehouseId: string, done: () => void) {
    if (!this.fulfilment()) {
      if (this.id) { this.saving.set(false); return; }
      done();
      return;
    }
    const sellerAreas = this.fulfilmentAreas().map(({ pincodesText, ...area }) => ({ ...area, pincodes: this.parsePincodes(pincodesText) }));
    this.api.saveStorefrontFulfilment(warehouseId, sellerAreas).subscribe({
      next: () => done(),
      error: (error: { error?: { detail?: string; message?: string } }) => {
        this.saving.set(false);
        this.fulfilmentError.set(error?.error?.detail || error?.error?.message || 'Warehouse fulfilment settings could not be saved.');
      },
    });
  }
  save() {
    if (this.form.invalid) return;
    this.saving.set(true);
    const raw = this.form.getRawValue();
    const clean = (value: string | null | undefined) => value || undefined;
    const input = {
      ...raw,
      branchId: clean(raw.branchId),
      address: raw.address?.addressLine1 ? raw.address : undefined,
      contacts: raw.contacts,
      zones: raw.zones,
    } as unknown as WarehouseInput;
    (this.id ? this.api.update(this.id, input) : this.api.create(input)).subscribe({
      next: (x) => {
        this.saveFulfilment(x.warehouseId, () => {
          this.snack.open('Warehouse saved.', undefined, { duration: 2500 });
          void this.router.navigate(['/warehouses', x.warehouseId]);
        });
      },
      error: (error: { error?: { detail?: string; message?: string } }) => {
        this.saving.set(false);
        const message = error?.error?.detail || error?.error?.message || 'Warehouse could not be saved.';
        this.snack.open(message, 'Dismiss', { duration: 7000 });
      },
    });
  }
}
