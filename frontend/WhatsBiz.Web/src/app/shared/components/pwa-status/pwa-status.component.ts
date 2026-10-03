import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { PwaService } from '../../../core/services/pwa.service';

@Component({
  selector: 'app-pwa-status',
  template: `
    @if (pwa.offline()) {
      <div class="pwa-notice pwa-notice--offline" role="status" aria-live="polite">
        <span class="material-symbols-rounded" aria-hidden="true">cloud_off</span>
        <span>No internet connection. Reconnect to continue.</span>
      </div>
    }
    @if (pwa.updateAvailable()) {
      <div class="pwa-notice pwa-notice--update" role="status" aria-live="polite">
        <span class="material-symbols-rounded" aria-hidden="true">system_update</span>
        <span>A new version of KhataDhari ERP is available.</span>
        <button type="button" (click)="update()">Update now</button>
      </div>
    }
    @if (pwa.installAvailable()) {
      <div class="pwa-notice pwa-notice--install" role="status">
        <span class="material-symbols-rounded" aria-hidden="true">install_mobile</span>
        <span>Install KhataDhari ERP</span>
        <button type="button" (click)="install()">Install</button>
        <button class="pwa-dismiss" type="button" aria-label="Dismiss install prompt" (click)="dismiss()">&times;</button>
      </div>
    } @else if (pwa.iosInstallHint()) {
      <div class="pwa-notice pwa-notice--install" role="status">
        <span class="material-symbols-rounded" aria-hidden="true">ios_share</span>
        <span>Share <strong>&rarr; Add to Home Screen</strong> to install KhataDhari ERP.</span>
        <button class="pwa-dismiss" type="button" aria-label="Dismiss install instructions" (click)="dismiss()">&times;</button>
      </div>
    }
  `,
  styles: `
    :host{display:grid;justify-items:center;gap:8px;padding:8px 12px;pointer-events:none}
    .pwa-notice{display:flex;max-width:min(720px,100%);min-height:40px;padding:8px 12px;align-items:center;gap:9px;color:var(--wb-text);background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:999px;box-shadow:var(--wb-shadow-md);font-size:.78rem;pointer-events:auto}
    .pwa-notice .material-symbols-rounded{font-size:19px}.pwa-notice button{border:0;border-radius:7px;padding:6px 10px;color:#fff;background:var(--wb-primary);font-size:.74rem;font-weight:700;cursor:pointer}.pwa-notice--offline{color:#fff;background:#7f1d1d;border-color:#991b1b}.pwa-notice--update{border-color:var(--wb-info)}.pwa-notice--install{border-color:var(--wb-primary)}.pwa-dismiss{padding:2px 6px!important;color:var(--wb-text)!important;background:transparent!important;font-size:1.1rem!important}
    @media(max-width:480px){:host{padding:8px}.pwa-notice{width:100%;border-radius:14px;font-size:.72rem}.pwa-notice>span:nth-child(2){flex:1}.pwa-notice button{min-height:34px}.pwa-notice--offline{justify-content:center}}
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PwaStatusComponent {
  readonly pwa = inject(PwaService);
  install(): void { void this.pwa.install(); }
  update(): void { void this.pwa.update(); }
  dismiss(): void { this.pwa.dismissInstall(); }
}
