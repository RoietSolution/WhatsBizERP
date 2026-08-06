import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';

type PasswordMode = 'forgot' | 'reset' | 'change';

@Component({
  selector: 'app-password-workflow',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, RouterLink],
  template: '<div class="workflow-icon"><span class="material-symbols-rounded">{{ content().icon }}</span></div><header><h2>{{ content().title }}</h2><p>{{ content().subtitle }}</p></header>@if (success()) { <div class="success-state" role="status" aria-live="polite"><span class="material-symbols-rounded">check_circle</span><h3>{{ content().successTitle }}</h3><p>{{ content().successMessage }}</p><a mat-flat-button color="primary" routerLink="/login">Return to Sign In</a></div> } @else { <form [formGroup]="form" (ngSubmit)="submit()" novalidate>@if (mode === "forgot") { <mat-form-field appearance="outline"><mat-label>Email address</mat-label><span matPrefix class="material-symbols-rounded">mail</span><input matInput type="email" autocomplete="email" formControlName="email" aria-label="Email address"><mat-error>Enter a valid email address.</mat-error></mat-form-field> } @if (mode === "change") { <mat-form-field appearance="outline"><mat-label>Current password</mat-label><span matPrefix class="material-symbols-rounded">lock</span><input matInput type="password" autocomplete="current-password" formControlName="currentPassword" aria-label="Current password"><mat-error>Enter your current password.</mat-error></mat-form-field> } @if (mode !== "forgot") { <mat-form-field appearance="outline"><mat-label>New password</mat-label><span matPrefix class="material-symbols-rounded">password</span><input matInput type="password" autocomplete="new-password" formControlName="newPassword" aria-label="New password"><mat-hint>Use at least 10 characters.</mat-hint><mat-error>Password must contain at least 10 characters.</mat-error></mat-form-field><mat-form-field appearance="outline"><mat-label>Confirm new password</mat-label><span matPrefix class="material-symbols-rounded">verified</span><input matInput type="password" autocomplete="new-password" formControlName="confirmPassword" aria-label="Confirm new password"><mat-error>Confirm your new password.</mat-error></mat-form-field>@if (passwordMismatch()) { <div class="form-error" role="alert">Passwords do not match.</div> } }<button mat-flat-button color="primary" type="submit"><span>{{ content().action }}</span><span class="material-symbols-rounded">arrow_forward</span></button></form><a class="back-link" routerLink="/login"><span class="material-symbols-rounded">arrow_back</span>Back to Sign In</a> }',
  styleUrl: './password-workflow.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasswordWorkflowComponent {
  private readonly route = inject(ActivatedRoute);
  readonly mode = (this.route.snapshot.data['mode'] as PasswordMode | undefined) ?? 'forgot';
  readonly success = signal(false);
  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    currentPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    newPassword: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(10)] }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  readonly content = computed(() => ({
    forgot: { icon: 'mark_email_read', title: 'Forgot Password?', subtitle: 'Enter your registered email address and we’ll help you regain access.', action: 'Send Reset Instructions', successTitle: 'Check your inbox', successMessage: 'If an account exists for that email, reset instructions will be sent shortly.' },
    reset: { icon: 'password', title: 'Reset Password', subtitle: 'Create a strong new password for your KhataDhari account.', action: 'Reset Password', successTitle: 'Password reset', successMessage: 'Your password has been updated. You can now sign in securely.' },
    change: { icon: 'manage_accounts', title: 'Change Password', subtitle: 'Update your password to keep your account secure.', action: 'Change Password', successTitle: 'Password changed', successMessage: 'Your password has been updated successfully.' },
  })[this.mode]);

  passwordMismatch(): boolean { return this.mode !== 'forgot' && this.form.controls.confirmPassword.touched && this.form.controls.newPassword.value !== this.form.controls.confirmPassword.value; }

  submit(): void {
    const controls = this.form.controls;
    const relevantInvalid = this.mode === 'forgot' ? controls.email.invalid : (this.mode === 'change' && controls.currentPassword.invalid) || controls.newPassword.invalid || controls.confirmPassword.invalid;
    if (relevantInvalid || (this.mode !== 'forgot' && controls.newPassword.value !== controls.confirmPassword.value)) { this.form.markAllAsTouched(); return; }
    this.success.set(true);
  }
}
