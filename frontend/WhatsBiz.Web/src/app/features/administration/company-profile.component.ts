import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { FormSectionComponent } from '../../shared/components/form-section/form-section.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { AdminApiService, Company } from './admin-api.service';
@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageContainerComponent,
    PageHeaderComponent,
    FormSectionComponent,
    StatusChipComponent,
  ],
  templateUrl: './company-profile.component.html',
  styleUrl: './company-profile.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompanyProfileComponent {
  readonly model = signal<Company | null>(null);
  readonly saving = signal(false);
  readonly states = [
    { name: 'Andhra Pradesh', code: '37' }, { name: 'Arunachal Pradesh', code: '12' }, { name: 'Assam', code: '18' }, { name: 'Bihar', code: '10' },
    { name: 'Chhattisgarh', code: '22' }, { name: 'Goa', code: '30' }, { name: 'Gujarat', code: '24' }, { name: 'Haryana', code: '06' },
    { name: 'Himachal Pradesh', code: '02' }, { name: 'Jharkhand', code: '20' }, { name: 'Karnataka', code: '29' }, { name: 'Kerala', code: '32' },
    { name: 'Madhya Pradesh', code: '23' }, { name: 'Maharashtra', code: '27' }, { name: 'Manipur', code: '14' }, { name: 'Meghalaya', code: '17' },
    { name: 'Mizoram', code: '15' }, { name: 'Nagaland', code: '13' }, { name: 'Odisha', code: '21' }, { name: 'Punjab', code: '03' },
    { name: 'Rajasthan', code: '08' }, { name: 'Sikkim', code: '11' }, { name: 'Tamil Nadu', code: '33' }, { name: 'Telangana', code: '36' },
    { name: 'Tripura', code: '16' }, { name: 'Uttar Pradesh', code: '09' }, { name: 'Uttarakhand', code: '05' }, { name: 'West Bengal', code: '19' },
    { name: 'Andaman and Nicobar Islands', code: '35' }, { name: 'Chandigarh', code: '04' }, { name: 'Dadra and Nagar Haveli and Daman and Diu', code: '26' },
    { name: 'Delhi', code: '07' }, { name: 'Jammu and Kashmir', code: '01' }, { name: 'Ladakh', code: '38' }, { name: 'Lakshadweep', code: '31' }, { name: 'Puducherry', code: '34' },
  ];
  constructor(
    private api: AdminApiService,
    private snack: MatSnackBar,
  ) {
    api.company().subscribe({ next: (x) => {
      const expected = this.states.find(state => state.name.localeCompare(x.state ?? '', undefined, { sensitivity: 'accent' }) === 0)?.code;
      if (expected && x.stateCode !== expected) {
        this.model.set({ ...x, stateCode: expected });
        this.snack.open(`GST State Code was corrected to ${expected} for ${x.state}. Save the profile to persist it.`, 'Close', { duration: 6000 });
      } else this.model.set(x);
    }, error: () => this.snack.open('Company profile could not be loaded.', 'Close', { duration: 4000 }) });
  }
  save() {
    const company = this.model();
    if (!company || this.saving()) return;
    this.saving.set(true);
    this.api.saveCompany(company).subscribe({
      next: (x) => { this.model.set(x); this.saving.set(false); this.snack.open('Company saved', 'Close', { duration: 2000 }); },
      error: (error: HttpErrorResponse) => { this.saving.set(false); this.snack.open(error.error?.detail || 'Company could not be saved. Check the highlighted fields and try again.', 'Close', { duration: 5000 }); },
    });
  }
  stateChanged(state: string) {
    const code = this.states.find(x => x.name === state)?.code;
    if (code) this.model.update(x => x ? { ...x, stateCode: code } : x);
  }
  stateOptions() {
    const current = this.model()?.state;
    return current && !this.states.some(x => x.name === current)
      ? [{ name: current, code: this.model()?.stateCode ?? '' }, ...this.states]
      : this.states;
  }
}
