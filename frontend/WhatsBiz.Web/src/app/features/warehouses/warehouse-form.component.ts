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
import { WarehouseApiService } from './warehouse-api.service';
import { WarehouseInput, WarehouseType } from './warehouse.models';
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
      @media (max-width: 800px) {
        .grid {
          grid-template-columns: 1fr;
        }
        .wide,
        .checks {
          grid-column: span 1;
        }
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
    forkJoin({ types: api.types(), warehouse: this.id ? api.get(this.id) : of(null) }).subscribe(
      (x) => {
        this.types.set(x.types);
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
        this.snack.open('Warehouse saved.', undefined, { duration: 2500 });
        void this.router.navigate(['/warehouses', x.warehouseId]);
      },
      error: () => {
        this.saving.set(false);
        this.snack.open('Warehouse could not be saved.', 'Dismiss', { duration: 5000 });
      },
    });
  }
}
