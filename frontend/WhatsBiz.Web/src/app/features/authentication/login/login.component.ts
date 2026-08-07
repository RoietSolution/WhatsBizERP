import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { AuthenticationService } from '../../../core/services/authentication.service';
import { LoadingOverlayComponent } from '../../../shared/components/loading-overlay/loading-overlay.component';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, RouterLink, LoadingOverlayComponent],
  template: `
    <app-loading-overlay [visible]="loading()" message="Signing you in…" />
    <div class="auth-card__brand"><img src="/assets/images/khatadhari-logo.png" alt="KhataDhari ERP"></div>
    <header><h2>Welcome Back</h2><p>Sign in to continue</p></header>
    <form [formGroup]="form" (ngSubmit)="login()" novalidate>
      <mat-form-field appearance="outline">
        <mat-label>Username</mat-label><span matPrefix class="material-symbols-rounded" aria-hidden="true">person</span>
        <input matInput required autocomplete="username" aria-label="Username" formControlName="username">
        @if (username.touched && username.hasError('required')) { <mat-error>Enter your username.</mat-error> }
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Password</mat-label><span matPrefix class="material-symbols-rounded" aria-hidden="true">lock</span>
        <input matInput required [type]="showPassword() ? 'text' : 'password'" autocomplete="current-password" aria-label="Password" formControlName="password">
        <button mat-icon-button matSuffix type="button" [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'" (click)="togglePasswordVisibility()"><span class="material-symbols-rounded">{{ showPassword() ? 'visibility_off' : 'visibility' }}</span></button>
        @if (password.touched && password.hasError('required')) { <mat-error>Enter your password.</mat-error> }
      </mat-form-field>
      <div class="form-options"><mat-checkbox formControlName="rememberMe">Remember me</mat-checkbox><a routerLink="/forgot-password">Forgot password?</a></div>
      @if (error()) { <div class="auth-message auth-message--error" role="alert"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }
      <button class="sign-in-button" mat-flat-button color="primary" type="submit" [disabled]="loading()"><span>Sign In</span><span class="material-symbols-rounded">arrow_forward</span></button>
    </form>
    <p class="secure-note"><span class="material-symbols-rounded">verified_user</span> Secure access to your business workspace</p>
  `,
  styleUrl: './login.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  readonly username = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly password = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly rememberMe = new FormControl(false, { nonNullable: true });
  readonly form = new FormGroup({ username: this.username, password: this.password, rememberMe: this.rememberMe });
  readonly showPassword = signal(false);
  readonly error = signal('');
  readonly loading = signal(false);

  constructor(private readonly authentication: AuthenticationService, private readonly router: Router) {}

  togglePasswordVisibility(): void { this.showPassword.update(value => !value); }

  login(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.loading()) return;
    this.error.set('');
    this.loading.set(true);
    this.authentication.login(this.username.value.trim(), this.password.value).subscribe({
      next: () => void this.router.navigateByUrl('/dashboard'),
      error: (response: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(response.status === 401 ? 'Unable to sign in with the provided credentials.' : response.error?.detail ?? 'The server is unavailable. Confirm that the local API and SQL Server are running.');
      },
    });
  }
}
