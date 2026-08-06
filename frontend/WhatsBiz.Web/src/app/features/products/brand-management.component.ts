import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { ProductApiService } from './product-api.service';
import { Brand } from './product.models';

@Component({ selector: 'app-brand-management', imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule], template: `<h1>Brands</h1><div class="layout"><form [formGroup]="form" (ngSubmit)="save()"><h2>{{ editingId ? 'Edit' : 'New' }} brand</h2><mat-form-field><mat-label>Code</mat-label><input matInput formControlName="brandCode"></mat-form-field><mat-form-field><mat-label>Name</mat-label><input matInput formControlName="brandName"></mat-form-field><mat-form-field><mat-label>Description</mat-label><textarea matInput formControlName="description"></textarea></mat-form-field><mat-form-field><mat-label>Logo URL</mat-label><input matInput formControlName="logo"></mat-form-field><mat-slide-toggle formControlName="isActive">Active</mat-slide-toggle><div><button mat-flat-button type="submit" [disabled]="form.invalid">Save</button><button mat-button type="button" (click)="reset()">Clear</button></div></form><section>@for(item of items();track item.brandId){<article><div><strong>{{item.brandName}}</strong><small>{{item.brandCode}} · {{item.isActive?'Active':'Inactive'}}</small></div><button mat-button (click)="edit(item)">Edit</button><button mat-button (click)="remove(item)">Delete</button></article>}</section></div>`, styles: [`.layout{display:grid;grid-template-columns:320px 1fr;gap:1.5rem}form{display:grid}article{display:flex;align-items:center;border-bottom:1px solid var(--mat-sys-outline-variant);padding:.75rem}article div{display:grid;flex:1}@media(max-width:800px){.layout{grid-template-columns:1fr}}`], changeDetection: ChangeDetectionStrategy.OnPush })
export class BrandManagementComponent {
  private readonly fb = inject(FormBuilder); readonly items = signal<Brand[]>([]); editingId?: string; readonly form = this.fb.group({ brandCode: ['', Validators.required], brandName: ['', Validators.required], description: [''], logo: [''], isActive: [true] });
  constructor(private readonly api: ProductApiService, private readonly snack: MatSnackBar, private readonly dialog: MatDialog) { this.load(); }
  load(): void { this.api.brands().subscribe(items => this.items.set(items)); }
  save(): void { if (this.form.invalid) return; const input = this.form.getRawValue() as Omit<Brand, 'brandId'>; (this.editingId ? this.api.updateBrand(this.editingId, input) : this.api.createBrand(input)).subscribe({ next: () => { this.snack.open('Brand saved.', undefined, { duration: 2000 }); this.reset(); this.load(); }, error: () => this.snack.open('Brand could not be saved.', 'Dismiss', { duration: 4000 }) }); }
  edit(item: Brand): void { this.editingId = item.brandId; this.form.patchValue(item); }
  reset(): void { this.editingId = undefined; this.form.reset({ brandCode: '', brandName: '', description: '', logo: '', isActive: true }); }
  remove(item: Brand): void { this.dialog.open(ConfirmDialogComponent, { data: { title: 'Delete brand', message: `Delete ${item.brandName}?` } }).afterClosed().subscribe(ok => { if (ok) this.api.deleteBrand(item.brandId).subscribe({ next: () => this.load(), error: () => this.snack.open('Brand is in use and cannot be deleted.', 'Dismiss', { duration: 4000 }) }); }); }
}
