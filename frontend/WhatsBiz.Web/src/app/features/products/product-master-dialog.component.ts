import { Component, Inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { Brand, UnitOfMeasure } from './product.models';

export type ProductMasterDialogData = 'brand' | 'unit';

@Component({
  selector: 'app-product-master-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressSpinnerModule],
  template: `
    <h2 mat-dialog-title>Add {{ data === 'brand' ? 'brand' : 'unit' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        <p class="required-note">Fields marked with * are required</p>
        @if (data === 'brand') {
          <mat-form-field appearance="outline"><mat-label>Brand name</mat-label><input matInput formControlName="name" required maxlength="200" /><mat-error>Brand name is required.</mat-error></mat-form-field>
        } @else {
          <mat-form-field appearance="outline"><mat-label>Unit name</mat-label><input matInput formControlName="name" required maxlength="200" /><mat-error>Unit name is required.</mat-error></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Unit code</mat-label><input matInput formControlName="code" maxlength="50" /><mat-hint>Leave blank to generate a unique code.</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Short name</mat-label><input matInput formControlName="shortName" maxlength="20" /><mat-hint>Optional; defaults from the unit name.</mat-hint></mat-form-field>
        }
        @if (error) { <p class="form-error" role="alert">{{ error }}</p> }
      </mat-dialog-content>
      <mat-dialog-actions align="end"><button mat-button type="button" (click)="dialogRef.close()">Cancel</button><button mat-flat-button color="primary" type="submit" [disabled]="form.invalid || saving">@if (saving) { <mat-spinner diameter="18" /> } @else { Save }</button></mat-dialog-actions>
    </form>
  `,
  styles: [`
    mat-dialog-content { display: grid; gap: 4px; min-width: min(420px, 78vw); }
    mat-form-field { width: 100%; }
    .required-note { margin: 0 0 8px; color: #b42318; font-weight: 700; }
    .form-error { color: #b42318; font-weight: 600; }
  `],
})
export class ProductMasterDialogComponent {
  readonly form = this.formBuilder.group({ name: ['', [Validators.required, Validators.maxLength(200)]], code: ['', Validators.maxLength(50)], shortName: ['', Validators.maxLength(20)] });
  saving = false;
  error = '';

  constructor(
    @Inject(MAT_DIALOG_DATA) readonly data: ProductMasterDialogData,
    private readonly formBuilder: FormBuilder,
    private readonly api: ProductApiService,
    readonly dialogRef: MatDialogRef<ProductMasterDialogComponent>,
  ) {}

  save(): void {
    if (this.form.invalid || this.saving) { this.form.markAllAsTouched(); return; }
    const value = this.form.getRawValue();
    this.saving = true;
    const request = this.data === 'brand'
      ? this.api.createBrand({ brandCode: value.code?.trim() ?? '', brandName: value.name!.trim(), description: '', logo: '', isActive: true })
      : this.api.createUnit({ unitCode: value.code?.trim() ?? '', unitName: value.name!.trim(), shortName: (value.shortName?.trim() || value.name!.trim().slice(0, 20).toUpperCase()), decimalPlaces: 0, isActive: true });
    request.pipe(finalize(() => this.saving = false)).subscribe({
      next: result => this.dialogRef.close(result),
      error: error => this.error = error?.error?.message || `Unable to add ${this.data}. Check the entered values and try again.`,
    });
  }
}
