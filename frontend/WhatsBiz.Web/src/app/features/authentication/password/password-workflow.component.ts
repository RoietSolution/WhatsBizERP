import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { AuthenticationService } from '../../../core/services/authentication.service';
import { LoadingOverlayComponent } from '../../../shared/components/loading-overlay/loading-overlay.component';

type PasswordMode = 'forgot' | 'reset' | 'change';

@Component({
  selector: 'app-password-workflow',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, RouterLink, LoadingOverlayComponent],
  template: '<app-loading-overlay [visible]="loading()" message="Securing your account…" /><div class="workflow-icon"><span class="material-symbols-rounded">{{ content().icon }}</span></div><header><h2>{{ content().title }}</h2><p>{{ content().subtitle }}</p></header>@if (success()) { <div class="success-state" role="status" aria-live="polite"><span class="material-symbols-rounded">check_circle</span><h3>{{ content().successTitle }}</h3><p>{{ successMessage() || content().successMessage }}</p>@if (resetLink()) { <a mat-flat-button color="primary" [routerLink]="resetLink()" [queryParams]="resetParams()">Continue to Reset Password</a> } @else { <a mat-flat-button color="primary" routerLink="/login">Return to Sign In</a> }</div> } @else { <form [formGroup]="form" (ngSubmit)="submit()" novalidate>@if (mode === "forgot") { <mat-form-field appearance="outline"><mat-label>Email or username</mat-label><span matPrefix class="material-symbols-rounded">alternate_email</span><input matInput required autocomplete="username" formControlName="identifier" aria-label="Email or username"><mat-error>Enter your registered email or username.</mat-error></mat-form-field> } @if (mode === "change") { <mat-form-field appearance="outline"><mat-label>Current password</mat-label><span matPrefix class="material-symbols-rounded">lock</span><input matInput required type="password" autocomplete="current-password" formControlName="currentPassword" aria-label="Current password"><mat-error>Enter your current password.</mat-error></mat-form-field> } @if (mode !== "forgot") { <mat-form-field appearance="outline"><mat-label>New password</mat-label><span matPrefix class="material-symbols-rounded">password</span><input matInput required type="password" autocomplete="new-password" formControlName="newPassword" aria-label="New password"><mat-hint>10+ characters with upper, lower, number and symbol.</mat-hint><mat-error>Use 10+ characters with upper, lower, number and symbol.</mat-error></mat-form-field><mat-form-field appearance="outline"><mat-label>Confirm new password</mat-label><span matPrefix class="material-symbols-rounded">verified</span><input matInput required type="password" autocomplete="new-password" formControlName="confirmPassword" aria-label="Confirm new password"><mat-error>Confirm your new password.</mat-error></mat-form-field>@if (passwordMismatch()) { <div class="form-error" role="alert">Passwords do not match.</div> } } @if (error()) { <div class="form-error" role="alert">{{ error() }}</div> }<button mat-flat-button color="primary" type="submit" [disabled]="loading()"><span>{{ content().action }}</span><span class="material-symbols-rounded">arrow_forward</span></button></form><a class="back-link" [routerLink]="mode === `change` ? `/profile` : `/login`"><span class="material-symbols-rounded">arrow_back</span>{{ mode === "change" ? "Back to Profile" : "Back to Sign In" }}</a> }',
  styleUrl: './password-workflow.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasswordWorkflowComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly authentication = inject(AuthenticationService);
  readonly mode = (this.route.snapshot.data['mode'] as PasswordMode | undefined) ?? 'forgot';
  readonly success = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly successMessage = signal('');
  readonly resetLink = signal<string | null>(null);
  readonly resetParams = signal<Record<string, string>>({});
  readonly form = new FormGroup({
    identifier: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    currentPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    newPassword: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(10), Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).+$/)] }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  readonly content = computed(() => ({
    forgot: { icon: 'mark_email_read', title: 'Forgot Password?', subtitle: 'Verify your registered email or username to regain access.', action: 'Continue Securely', successTitle: 'Request verified', successMessage: 'If the account exists, reset instructions will be sent shortly.' },
    reset: { icon: 'password', title: 'Reset Password', subtitle: 'Create a strong new password for your KhataDhari account.', action: 'Reset Password', successTitle: 'Password reset', successMessage: 'Your password has been updated. You can now sign in securely.' },
    change: { icon: 'manage_accounts', title: 'Change Password', subtitle: 'Confirm your current password before choosing a new one.', action: 'Change Password', successTitle: 'Password changed', successMessage: 'Your password has been updated successfully.' },
  })[this.mode]);

  passwordMismatch(): boolean { return this.mode !== 'forgot' && this.form.controls.confirmPassword.touched && this.form.controls.newPassword.value !== this.form.controls.confirmPassword.value; }

  submit(): void {
    const controls = this.form.controls;
    const invalid = this.mode === 'forgot' ? controls.identifier.invalid : (this.mode === 'change' && controls.currentPassword.invalid) || controls.newPassword.invalid || controls.confirmPassword.invalid;
    if (invalid || this.passwordMismatchValues()) { this.form.markAllAsTouched(); return; }
    this.loading.set(true); this.error.set('');
    const request: Observable<unknown> = this.mode === 'forgot'
      ? this.authentication.forgotPassword(controls.identifier.value)
      : this.mode === 'reset'
        ? this.authentication.resetPassword(this.route.snapshot.queryParamMap.get('userId') ?? '', this.route.snapshot.queryParamMap.get('token') ?? '', controls.newPassword.value)
        : this.authentication.changePassword(controls.currentPassword.value, controls.newPassword.value);
    request.subscribe({ next: (response) => { this.loading.set(false); if (this.mode === 'forgot') { const result = response as { message: string; resetToken?: string; userId?: string }; this.successMessage.set(result.message); if (result.resetToken && result.userId) { this.resetLink.set('/reset-password'); this.resetParams.set({ token: result.resetToken, userId: result.userId }); } } this.success.set(true); }, error: (response: HttpErrorResponse) => { this.loading.set(false); if (response.status === 401 && this.mode === 'change') { this.authentication.clearSession(); return; } this.error.set(response.error?.detail ?? 'The request could not be completed. Verify the information and try again.'); } });
  }

  private passwordMismatchValues(): boolean { return this.mode !== 'forgot' && this.form.controls.newPassword.value !== this.form.controls.confirmPassword.value; }
}
