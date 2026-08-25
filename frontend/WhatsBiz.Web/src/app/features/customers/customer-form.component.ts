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
import { CustomerApiService } from './customer-api.service';
import { CustomerInput, PaymentTerm } from './customer.models';
import { CustomerGroup } from './customer-group.models';
import { CustomerGroupApiService } from './customer-group-api.service';
import { WhatsAppApiService } from '../whatsapp/whatsapp-api.service';
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
  templateUrl: './customer-form.component.html',
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
export class CustomerFormComponent {
  private readonly fb = inject(FormBuilder);
  readonly id: string | null;
  readonly whatsappContactId: string | null;
  readonly terms = signal<PaymentTerm[]>([]);
  readonly groups = signal<CustomerGroup[]>([]);
  readonly form = this.fb.group({
    customerCode: ['', Validators.required],
    customerName: ['', Validators.required],
    customerType: ['Retail', Validators.required],
    gstin: [''],
    pan: [''],
    email: ['', Validators.email],
    mobile: [''],
    telephone: [''],
    website: [''],
    currency: ['INR', Validators.required],
    paymentTermId: [null as string | null],
    creditLimit: [0, Validators.min(0)],
    openingBalance: [0],
    salesPersonId: [''],
    customerGroupId: [''],
    priceListId: [''],
    isGSTRegistered: [false],
    isActive: [true],
    remarks: [''],
    contact: this.fb.group({
      contactPerson: [''],
      designation: [''],
      department: [''],
      mobile: [''],
      email: [''],
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
    private readonly api: CustomerApiService,
    route: ActivatedRoute,
    private readonly router: Router,
    private readonly snack: MatSnackBar,
    private readonly groupApi: CustomerGroupApiService,
    private readonly whatsappApi: WhatsAppApiService,
  ) {
    this.id = route.snapshot.paramMap.get('id');
    this.whatsappContactId = route.snapshot.queryParamMap.get('whatsappContactId');
    if(!this.id&&this.whatsappContactId)this.form.patchValue({customerCode:route.snapshot.queryParamMap.get('code')??'',customerName:route.snapshot.queryParamMap.get('name')??'',mobile:route.snapshot.queryParamMap.get('mobile')??'',remarks:'Created from an inbound WhatsApp contact.'});
    forkJoin({ terms: api.terms(), groups: groupApi.list(), customer: this.id ? api.get(this.id) : of(null) }).subscribe(
      (x) => {
        this.terms.set(x.terms);
        this.groups.set(x.groups);
        if (x.customer)
          this.form.patchValue({
            ...x.customer,
            contact: x.customer.contacts[0],
            address: x.customer.addresses[0],
            bank: x.customer.bankAccounts[0],
          });
      },
    );
  }
  save() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const clean = (x: string | null | undefined) => x || undefined;
    const input = {
      ...v,
      gstin: clean(v.gstin),
      pan: clean(v.pan),
      email: clean(v.email),
      mobile: clean(v.mobile),
      telephone: clean(v.telephone),
      website: clean(v.website),
      remarks: clean(v.remarks),
      salesPersonId: clean(v.salesPersonId),
      customerGroupId: clean(v.customerGroupId),
      priceListId: clean(v.priceListId),
      contacts: v.contact?.contactPerson ? [v.contact] : [],
      addresses: v.address?.addressLine1 ? [v.address] : [],
      bankAccounts: v.bank?.accountNumber ? [v.bank] : [],
    } as unknown as CustomerInput;
    (this.id ? this.api.update(this.id, input) : this.api.create(input)).subscribe({
      next: (x) => {
        this.snack.open('Customer saved.', undefined, { duration: 2500 });
        if(this.whatsappContactId)this.whatsappApi.linkContact(this.whatsappContactId,x.customerId).subscribe({next:()=>void this.router.navigate(['/customers/whatsapp-contacts']),error:()=>{this.snack.open('Customer was created, but the WhatsApp contact could not be linked. Link it manually from WhatsApp Contacts.','Dismiss',{duration:6000});void this.router.navigate(['/customers',x.customerId]);}});
        else void this.router.navigate(['/customers', x.customerId]);
      },
      error: () => this.snack.open('Customer could not be saved.', 'Dismiss', { duration: 5000 }),
    });
  }
}
