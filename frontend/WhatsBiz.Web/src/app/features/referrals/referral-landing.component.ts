import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import {
  ReferralResolution,
  ReferralRewardsApiService,
} from '../administration/referral-rewards-api.service';

@Component({
  selector: 'app-referral-landing',
  imports: [MatButtonModule, RouterLink],
  template: `
    <main class="referral-card">
      <div class="badge">REFER & EARN</div>
      @if (busy()) {
        <h1>Checking your referral…</h1>
        <p>Please wait while we validate the invitation.</p>
      } @else if (resolution(); as referral) {
        @if (referral.isEnabled) {
          <h1>You’ve been invited to {{ referral.retailerName }}</h1>
          <p>Use this referral code when you register or place your first qualifying order.</p>
          <strong class="code">{{ referral.code }}</strong>
          <p class="hint">You can also send <b>REF {{ referral.code }}</b> to the retailer on WhatsApp.</p>
        } @else {
          <h1>This referral program is currently paused</h1>
          <p>{{ referral.retailerName }} is not accepting new referral rewards right now.</p>
        }
      } @else {
        <h1>This referral link is not valid</h1>
        <p>Ask the person who invited you for a new referral link.</p>
      }
      <a mat-flat-button routerLink="/login">Continue to sign in</a>
    </main>
  `,
  styles: [`
    :host{display:block;width:min(620px,100%)}
    .referral-card{display:grid;justify-items:center;gap:18px;padding:42px;text-align:center;border:1px solid var(--wb-border);border-radius:20px;background:var(--wb-surface);box-shadow:0 18px 50px rgb(15 23 42 / 12%)}
    .badge{padding:7px 12px;border-radius:999px;background:var(--wb-primary-soft);color:var(--wb-primary);font-weight:800;letter-spacing:.12em}
    h1,p{margin:0}.code{padding:14px 22px;border:2px dashed var(--wb-primary);border-radius:12px;font-size:30px;letter-spacing:.18em}.hint{font-size:14px;opacity:.8}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReferralLandingComponent {
  readonly busy = signal(true);
  readonly resolution = signal<ReferralResolution | null>(null);

  constructor(route: ActivatedRoute, api: ReferralRewardsApiService) {
    const code = route.snapshot.paramMap.get('code')?.trim() ?? '';
    if (!code) {
      this.busy.set(false);
      return;
    }
    api.resolve(code).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (value) => this.resolution.set(value),
      error: () => this.resolution.set(null),
    });
  }
}
