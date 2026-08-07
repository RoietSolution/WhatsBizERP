import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import {
  UserPreferences,
  UserPreferencesService,
} from '../../shared/services/user-preferences.service';

@Component({
  selector: 'app-user-preferences',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatSlideToggleModule,
    PageContainerComponent,
    PageHeaderComponent,
  ],
  templateUrl: './user-preferences.component.html',
  styles: [
    ':host{display:block}.preferences-card{max-width:900px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-lg);box-shadow:var(--wb-shadow-sm)}section{padding:24px 28px;border-bottom:1px solid var(--wb-border)}h2{display:flex;margin:0 0 20px;align-items:center;gap:8px;font-size:1rem}h2 span{color:var(--wb-primary)}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px 16px}.toggles{display:flex;flex-wrap:wrap;gap:24px}.actions{display:flex;padding:20px 28px;justify-content:flex-end}@media(max-width:767px){section{padding:20px}.grid{grid-template-columns:1fr}.actions{padding:20px}.actions button{width:100%}}',
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserPreferencesComponent {
  private readonly preferences = inject(UserPreferencesService);
  private readonly snack = inject(MatSnackBar);
  readonly saving = signal(false);
  readonly form = new FormGroup(
    Object.fromEntries(
      Object.entries(this.preferences.preferences()).map(([key, value]) => [
        key,
        new FormControl(value, { nonNullable: true }),
      ]),
    ) as { [K in keyof UserPreferences]: FormControl<UserPreferences[K]> },
  );
  save(): void {
    this.saving.set(true);
    this.preferences.save(this.form.getRawValue());
    this.saving.set(false);
    this.snack.open('Preferences saved successfully.', 'Close', {
      duration: 3500,
      panelClass: 'wb-success',
    });
  }
}
