import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, RouterLink } from '@angular/router';

type StateMode = 'locked' | 'expired' | 'denied';
@Component({
  selector: 'app-authentication-state',
  imports: [MatButtonModule, RouterLink],
  template: '<div class="state-icon" [attr.data-tone]="content().tone"><span class="material-symbols-rounded">{{ content().icon }}</span></div><p class="state-code">{{ content().code }}</p><h2>{{ content().title }}</h2><p class="state-message">{{ content().message }}</p><div class="state-actions"><a mat-flat-button color="primary" routerLink="/login">{{ content().action }}</a>@if (mode === "locked") { <a mat-button routerLink="/forgot-password">Reset password</a> }</div><p class="state-support"><span class="material-symbols-rounded">support_agent</span> Need help? Contact your system administrator.</p>',
  styleUrl: './authentication-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthenticationStateComponent {
  private readonly route = inject(ActivatedRoute);
  readonly mode = (this.route.snapshot.data['mode'] as StateMode | undefined) ?? 'denied';
  readonly content = computed(() => ({
    locked: { icon: 'lock', code: 'ACCOUNT LOCKED', title: 'Your account is temporarily locked', message: 'Too many unsuccessful sign-in attempts were detected. Wait a few minutes or reset your password to regain access.', action: 'Return to Sign In', tone: 'warning' },
    expired: { icon: 'schedule', code: 'SESSION EXPIRED', title: 'Your session has expired', message: 'For your security, you were signed out after a period of inactivity. Sign in again to continue where you left off.', action: 'Sign In Again', tone: 'info' },
    denied: { icon: 'shield_lock', code: 'ACCESS DENIED', title: 'You don’t have access to this page', message: 'Your account does not have the required permission. Contact your administrator if you believe this is an error.', action: 'Return to Sign In', tone: 'danger' },
  })[this.mode]);
}
