import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { AuthenticationService } from '../../core/services/authentication.service';
import { CurrentUserService } from '../../core/services/current-user.service';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { ProfilePhotoService } from '../../shared/services/profile-photo.service';

@Component({
  selector: 'app-user-profile',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    PageContainerComponent,
    PageHeaderComponent,
  ],
  templateUrl: './user-profile.component.html',
  styles: [
    ':host{display:block}.profile-card{max-width:760px;padding:32px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-lg);box-shadow:var(--wb-shadow-sm)}.identity{display:flex;align-items:center;gap:20px;padding-bottom:24px;margin-bottom:24px;border-bottom:1px solid var(--wb-border)}.identity h2,.identity p{margin:0}.identity p{margin-top:4px;color:var(--wb-text-secondary)}.avatar{position:relative;display:grid;width:88px;height:88px;padding:0;overflow:hidden;color:#1e3a8a;background:#dbeafe;border:0;border-radius:50%;font-size:1.5rem;font-weight:700;place-items:center;cursor:pointer}.avatar img{width:100%;height:100%;object-fit:cover}.avatar i{position:absolute;right:0;bottom:0;display:grid;width:30px;height:30px;color:#fff;background:var(--wb-primary);border:3px solid var(--wb-surface);border-radius:50%;font-size:16px;place-items:center}form{display:grid;grid-template-columns:1fr 1fr;gap:16px}.actions{display:flex;grid-column:1/-1;justify-content:flex-end;gap:12px}.actions a{display:inline-flex;gap:6px}@media(max-width:767px){.profile-card{padding:20px}form{grid-template-columns:1fr}.identity{align-items:flex-start}.actions{flex-direction:column-reverse}.actions>*{width:100%}}',
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserProfileComponent {
  private readonly currentUser = inject(CurrentUserService);
  private readonly authentication = inject(AuthenticationService);
  private readonly snack = inject(MatSnackBar);
  private readonly profilePhoto = inject(ProfilePhotoService);
  readonly user = this.currentUser.user;
  readonly saving = signal(false);
  readonly photo = this.profilePhoto.photo;
  readonly initials = computed(() => (this.user()?.username ?? 'U').slice(0, 2).toUpperCase());
  readonly email = new FormControl(this.user()?.email ?? '', {
    nonNullable: true,
    validators: [Validators.required, Validators.email],
  });
  save(): void {
    this.email.markAsTouched();
    if (this.email.invalid || this.saving()) return;
    this.saving.set(true);
    this.authentication.updateProfile(this.email.value).subscribe({
      next: () => {
        this.saving.set(false);
        this.snack.open('Profile updated successfully.', 'Close', {
          duration: 3500,
          panelClass: 'wb-success',
        });
      },
      error: () => {
        this.saving.set(false);
        this.snack.open('Profile could not be updated.', 'Close', {
          duration: 4500,
          panelClass: 'wb-error',
        });
      },
    });
  }
  selectPhoto(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file || file.size > 2_000_000) {
      if (file)
        this.snack.open('Choose an image smaller than 2 MB.', 'Close', {
          duration: 4000,
          panelClass: 'wb-warning',
        });
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const value = String(reader.result);
      this.profilePhoto.set(value);
      this.snack.open('Profile picture updated.', 'Close', {
        duration: 3000,
        panelClass: 'wb-success',
      });
    };
    reader.readAsDataURL(file);
  }
}
