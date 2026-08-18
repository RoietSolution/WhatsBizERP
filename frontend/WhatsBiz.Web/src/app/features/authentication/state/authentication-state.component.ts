import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, RouterLink } from '@angular/router';

type StateMode = 'locked' | 'expired' | 'denied';
@Component({
  selector: 'app-authentication-state',
  imports: [MatButtonModule, RouterLink],
  templateUrl: './authentication-state.component.html',
  styleUrl: './authentication-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthenticationStateComponent {
  private readonly route = inject(ActivatedRoute);
  readonly mode = (this.route.snapshot.data['mode'] as StateMode | undefined) ?? 'denied';
  readonly content = computed(
    () =>
      ({
        locked: {
          icon: 'lock',
          code: 'ACCOUNT LOCKED',
          title: 'Your account is temporarily locked',
          message:
            'Too many unsuccessful sign-in attempts were detected. Wait a few minutes or reset your password to regain access.',
          action: 'Return to Sign In',
          tone: 'warning',
        },
        expired: {
          icon: 'schedule',
          code: 'SESSION EXPIRED',
          title: 'Your session has expired',
          message:
            'For your security, you were signed out after a period of inactivity. Sign in again to continue where you left off.',
          action: 'Sign In Again',
          tone: 'info',
        },
        denied: {
          icon: 'shield_lock',
          code: 'ACCESS DENIED',
          title: 'You don’t have access to this page',
          message:
            'Your account does not have the required permission. Contact your administrator if you believe this is an error.',
          action: 'Return to Sign In',
          tone: 'danger',
        },
      })[this.mode],
  );
}
