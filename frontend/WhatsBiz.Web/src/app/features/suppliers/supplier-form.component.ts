import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { forkJoin, of } from 'rxjs';
import { SupplierApiService } from './supplier-api.service';
import { PaymentTerm, SupplierInput } from './supplier.models';
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
  templateUrl: './supplier-form.component.html',
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
        padding: 1.5rem;
      }
      .wide,
      .checks {
        grid-column: span 3;
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
export class SupplierFormComponent {
  private readonly fb = inject(FormBuilder);
  readonly id: string | null;
  readonly terms = signal<PaymentTerm[]>([]);
  readonly form = this.fb.group({
    supplierCode: ['', Validators.required],
    supplierName: ['', Validators.required],
    supplierType: ['Domestic', Validators.required],
    gstin: [''],
    pan: [''],
    msmeRegistrationNumber: [''],
    email: ['', Validators.email],
    mobile: [''],
    telephone: [''],
    website: [''],
    currency: ['INR', Validators.required],
    paymentTermId: [null as string | null],
    creditLimit: [0, Validators.min(0)],
    openingBalance: [0],
    isGSTRegistered: [false],
    isTDSApplicable: [false],
    isActive: [true],
    remarks: [''],
    contact: this.fb.group({
      contactPerson: [''],
      designation: [''],
      mobile: [''],
      email: [''],
      department: [''],
      isPrimary: [true],
    }),
    address: this.fb.group({
      addressType: ['Billing'],
      addressLine1: [''],
      addressLine2: [''],
      city: [''],
      district: [''],
      state: [''],
      country: ['India'],
      postalCode: [''],
    }),
    bank: this.fb.group({
      bankName: [''],
      branch: [''],
      accountNumber: [''],
      ifscCode: [''],
      upiId: [''],
    }),
  });
  constructor(
    private readonly api: SupplierApiService,
    route: ActivatedRoute,
    private readonly router: Router,
    private readonly snack: MatSnackBar,
  ) {
    this.id = route.snapshot.paramMap.get('id');
    forkJoin({ terms: api.terms(), supplier: this.id ? api.get(this.id) : of(null) }).subscribe(
      (x) => {
        this.terms.set(x.terms);
        if (x.supplier) {
          this.form.patchValue({
            ...x.supplier,
            contact: x.supplier.contacts[0],
            address: x.supplier.addresses[0],
            bank: x.supplier.bankAccounts[0],
          });
        }
      },
    );
  }
  save() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const clean = (x: string | null | undefined) => x?.trim() || undefined;
    const input = {
      ...v,
      gstin: clean(v.gstin),
      pan: clean(v.pan),
      msmeRegistrationNumber: clean(v.msmeRegistrationNumber),
      email: clean(v.email),
      mobile: clean(v.mobile),
      telephone: clean(v.telephone),
      website: clean(v.website),
      remarks: clean(v.remarks),
      contacts: v.contact?.contactPerson ? [v.contact] : [],
      addresses: v.address?.addressLine1 ? [v.address] : [],
      bankAccounts: v.bank?.accountNumber ? [v.bank] : [],
    } as unknown as SupplierInput;
    (this.id ? this.api.update(this.id, input) : this.api.create(input)).subscribe({
      next: (x) => {
        this.snack.open('Supplier saved.', undefined, { duration: 2500 });
        void this.router.navigate(['/suppliers', x.supplierId]);
      },
      error: () => this.snack.open('Supplier could not be saved.', 'Dismiss', { duration: 5000 }),
    });
  }
}
