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
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    LoadingOverlayComponent,
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  readonly username = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly password = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly rememberMe = new FormControl(false, { nonNullable: true });
  readonly form = new FormGroup({
    username: this.username,
    password: this.password,
    rememberMe: this.rememberMe,
  });
  readonly showPassword = signal(false);
  readonly error = signal('');
  readonly loading = signal(false);

  constructor(
    private readonly authentication: AuthenticationService,
    private readonly router: Router,
  ) {}

  togglePasswordVisibility(): void {
    this.showPassword.update((value) => !value);
  }

  login(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.loading()) return;
    this.error.set('');
    this.loading.set(true);
    this.authentication.login(this.username.value.trim(), this.password.value).subscribe({
      next: (session) =>
        void this.router.navigateByUrl(
          session.user.permissions.includes('feature.manage') ? '/admin/features' : '/dashboard',
        ),
      error: (response: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(
          response.status === 401
            ? 'Unable to sign in with the provided credentials.'
            : (response.error?.detail ??
                'The server is unavailable. Confirm that the local API and SQL Server are running.'),
        );
      },
    });
  }
}
