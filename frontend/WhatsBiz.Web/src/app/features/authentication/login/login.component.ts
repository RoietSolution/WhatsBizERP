import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../../core/services/authentication.service';

@Component({
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  template: '<mat-card><mat-card-title>WhatsBiz ERP</mat-card-title><mat-card-content><mat-form-field><input matInput placeholder="Username" [formControl]="username"></mat-form-field><mat-form-field><input matInput type="password" placeholder="Password" [formControl]="password"></mat-form-field><p>{{ error() }}</p></mat-card-content><mat-card-actions><button mat-button [disabled]="username.invalid || password.invalid" (click)="login()">Sign in</button></mat-card-actions></mat-card>',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  readonly username = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly password = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly error = signal('');

  constructor(
    private readonly authentication: AuthenticationService,
    private readonly router: Router,
  ) {}

  login(): void {
    this.error.set('');
    this.authentication.login(this.username.value, this.password.value).subscribe({
      next: () => void this.router.navigateByUrl('/dashboard'),
      error: (error: HttpErrorResponse) =>
        this.error.set(
          error.status === 401
            ? 'Unable to sign in with the provided credentials.'
            : 'The server is unavailable. Confirm that the local API and SQL Server are running.',
        ),
    });
  }
}
