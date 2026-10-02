import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class StorefrontNotificationService {
  readonly message = signal('');
  private timer: ReturnType<typeof setTimeout> | undefined;
  show(message: string): void { this.message.set(message); if (this.timer) clearTimeout(this.timer); this.timer = setTimeout(() => this.message.set(''), 2600); }
}
