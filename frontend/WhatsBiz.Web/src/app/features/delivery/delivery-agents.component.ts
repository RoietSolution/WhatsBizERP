import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { forkJoin } from 'rxjs';
import { DeliveryAgent, DeliveryApiService, DeliveryUser } from './delivery-api.service';

@Component({
  imports: [CommonModule, FormsModule, MatButtonModule],
  template: `
    <section class="page">
      <header>
        <div>
          <small>Administration</small>
          <h1>Delivery Agents</h1>
          <p>Choose an existing active tenant user and configure their delivery profile.</p>
        </div>
        <a mat-stroked-button href="/admin/delivery-settings">Settings</a>
      </header>

      <form #agentForm="ngForm" (ngSubmit)="create()">
        <select
          [(ngModel)]="draft.userId"
          name="user"
          required
          aria-label="Existing tenant user"
          (ngModelChange)="selectUser($event)"
        >
          <option value="" disabled>Select existing user</option>
          <option *ngFor="let user of eligibleUsers()" [value]="user.userId">
            {{ user.userName }}{{ user.email ? ' · ' + user.email : '' }}
          </option>
        </select>
        <input [(ngModel)]="draft.displayName" name="name" placeholder="Display name" required />
        <input [(ngModel)]="draft.mobile" name="mobile" placeholder="Mobile" required />
        <button mat-flat-button color="primary" [disabled]="agentForm.invalid || saving()">
          {{ saving() ? 'Adding...' : 'Add agent' }}
        </button>
      </form>

      <p class="hint" *ngIf="!eligibleUsers().length">
        No eligible users are available. Create or activate a tenant user first, or check whether
        all users are already delivery agents.
      </p>
      <p class="message" *ngIf="message()">{{ message() }}</p>

      <div class="agents">
        <article *ngFor="let agent of agents()">
          <div>
            <strong>{{ agent.displayName }} <span *ngIf="agent.isDefault">Default</span></strong>
            <small>{{ agent.mobile }}</small>
          </div>
          <label><input type="checkbox" [ngModel]="agent.isActive" (ngModelChange)="update(agent, { isActive: $event })" /> Active</label>
          <label><input type="checkbox" [ngModel]="agent.isAvailable" (ngModelChange)="update(agent, { isAvailable: $event })" /> Available</label>
          <div><b>{{ agent.activeDeliveries }}</b><small>Active</small></div>
          <div><b>{{ agent.deliveredToday }}</b><small>Today</small></div>
          <button mat-stroked-button *ngIf="!agent.isDefault && agent.isActive" (click)="default(agent)">Set default</button>
          <button mat-stroked-button *ngIf="agent.isDefault" (click)="removeDefault()">Remove default</button>
        </article>
        <p class="empty" *ngIf="!agents().length">No delivery agents have been configured yet.</p>
      </div>
    </section>
  `,
  styles: [
    `.page{padding:20px}header{display:flex;justify-content:space-between}h1{margin:2px 0}header p,small,.hint,.empty{color:#667085}form{display:flex;gap:8px;flex-wrap:wrap;margin:18px 0}input,select{min-height:42px;border:1px solid #cbd5e1;border-radius:8px;padding:0 10px;background:#fff}select{min-width:280px}.agents{display:grid;gap:8px;margin-top:16px}.agents article{display:grid;grid-template-columns:2fr 1fr 1fr .6fr .6fr auto;align-items:center;gap:12px;padding:14px;background:white;border:1px solid #dde4eb;border-radius:12px}.agents strong,.agents small{display:block}.agents span{font-size:.7rem;background:#dff3e9;color:#08634f;padding:3px 6px;border-radius:10px}.message{color:#8a4b08}.empty{padding:28px;text-align:center;background:#fff;border:1px dashed #cbd5e1;border-radius:12px}@media(max-width:760px){.agents article{grid-template-columns:1fr 1fr}.agents article>div:first-child{grid-column:1/-1}.page{padding:12px}select{min-width:100%}}`,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeliveryAgentsComponent {
  private readonly api = inject(DeliveryApiService);
  readonly agents = signal<DeliveryAgent[]>([]);
  readonly eligibleUsers = signal<DeliveryUser[]>([]);
  readonly message = signal('');
  readonly saving = signal(false);
  draft = { userId: '', displayName: '', mobile: '' };

  constructor() {
    this.load();
  }

  load(): void {
    forkJoin({ agents: this.api.agents(), users: this.api.eligibleUsers() }).subscribe({
      next: ({ agents, users }) => {
        this.agents.set(agents);
        this.eligibleUsers.set(users);
      },
      error: (error) => this.message.set(this.errorMessage(error, 'Unable to load delivery agents.')),
    });
  }

  selectUser(userId: string): void {
    const user = this.eligibleUsers().find((item) => item.userId === userId);
    if (!user) return;
    this.draft.displayName = user.userName;
    this.draft.mobile = user.mobile ?? '';
  }

  create(): void {
    this.message.set('');
    this.saving.set(true);
    this.api.saveAgent(this.draft).subscribe({
      next: () => {
        this.draft = { userId: '', displayName: '', mobile: '' };
        this.saving.set(false);
        this.message.set('Delivery agent added.');
        this.load();
      },
      error: (error) => {
        this.saving.set(false);
        this.message.set(this.errorMessage(error, 'Unable to add agent.'));
      },
    });
  }

  update(agent: DeliveryAgent, patch: Partial<DeliveryAgent>): void {
    this.api.saveAgent({ ...agent, ...patch }, agent.deliveryAgentId).subscribe({
      next: () => this.load(),
      error: (error) => this.message.set(this.errorMessage(error, 'Update failed.')),
    });
  }

  default(agent: DeliveryAgent): void {
    this.api.setDefault(agent.deliveryAgentId).subscribe(() => this.load());
  }

  removeDefault(): void {
    this.api.setDefault().subscribe(() => this.load());
  }

  private errorMessage(error: any, fallback: string): string {
    const validation = error.error?.errors as Record<string, string[]> | undefined;
    return error.error?.detail ?? (validation ? Object.values(validation).flat()[0] : undefined) ?? error.error?.title ?? fallback;
  }
}
