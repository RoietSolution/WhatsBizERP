import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CollectionApiService } from './collection-api.service';
@Component({ selector: 'app-collection-form', imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatDatepickerModule, MatNativeDateModule, PageContainerComponent, PageHeaderComponent], templateUrl: './collection-form.component.html', styles: [`.form-card{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;padding:20px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md)}.wide{grid-column:1/-1}.actions{display:flex;justify-content:flex-end;gap:10px;grid-column:1/-1}@media(max-width:700px){.form-card{grid-template-columns:1fr}.wide{grid-column:auto}}`], changeDetection: ChangeDetectionStrategy.OnPush })
export class CollectionFormComponent {
  private readonly fb = inject(FormBuilder); readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  readonly form = this.fb.group({ name: ['', [Validators.required, Validators.maxLength(200)]], description: ['', Validators.maxLength(1000)], isActive: [true], displayOrder: [0, [Validators.required, Validators.min(0)]], startDate: [null as Date | null], endDate: [null as Date | null] });
  constructor(private readonly api: CollectionApiService, private readonly router: Router, private readonly snack: MatSnackBar) { if (this.id) this.api.get(this.id).subscribe(x => this.form.patchValue({ name: x.name, description: x.description ?? '', isActive: x.isActive, displayOrder: x.displayOrder, startDate: this.parseDate(x.startDate), endDate: this.parseDate(x.endDate) })); }
  private parseDate(value?: string | null) { return value ? new Date(`${value.slice(0, 10)}T00:00:00`) : null; }
  private formatDate(value: Date | null) { return value ? `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}` : undefined; }
  save() { if (this.form.invalid) { this.form.markAllAsTouched(); return; } const x = this.form.getRawValue(); const startDate = this.formatDate(x.startDate); const endDate = this.formatDate(x.endDate); if (startDate && endDate && endDate < startDate) { this.snack.open('End date must be on or after start date.', 'Dismiss', { duration: 3500 }); return; } const input = { name: x.name ?? '', description: x.description || undefined, isActive: !!x.isActive, displayOrder: Number(x.displayOrder ?? 0), startDate, endDate }; const request = this.id ? this.api.update(this.id, input) : this.api.create(input); request.subscribe({ next: item => { this.snack.open('Collection saved.', undefined, { duration: 2500 }); void this.router.navigate(['/products/collections', item.collectionId, 'products']); }, error: () => this.snack.open('Collection could not be saved.', 'Dismiss', { duration: 4000 }) }); }
}
