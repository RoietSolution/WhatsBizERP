import { Injectable, OnDestroy, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class StorefrontCountdownService implements OnDestroy {
  readonly now = signal(Date.now());
  private readonly timer = setInterval(() => this.now.set(Date.now()), 1000);
  ngOnDestroy(): void { clearInterval(this.timer); }

  openingLabel(targetUtc?: string, timeZone?: string, serverNowUtc?: string, receivedAtUtc?: number): string {
    if (!targetUtc || !timeZone) return '';
    try {
      const target=new Date(targetUtc);
      const serverNow=serverNowUtc?Date.parse(serverNowUtc):NaN;
      const current=serverNow+(this.now()-(receivedAtUtc??this.now()));
      const dayFormat=new Intl.DateTimeFormat('en-CA',{timeZone,year:'numeric',month:'2-digit',day:'2-digit'});
      const sameDay=Number.isFinite(current)&&dayFormat.format(target)===dayFormat.format(new Date(current));
      return new Intl.DateTimeFormat('en-IN', { timeZone, ...(sameDay?{}:{weekday:'short' as const,month:'short' as const,day:'numeric' as const}),hour:'numeric',minute:'2-digit',hour12:true }).format(target).replace(/\b(am|pm)\b/gi,value=>value.toUpperCase());
    }
    catch { return ''; }
  }

  remaining(targetUtc?: string, serverNowUtc?: string, receivedAtUtc?: number): number | null {
    if (!targetUtc || !serverNowUtc) return null;
    const target = Date.parse(targetUtc);
    const serverAtReceive = Date.parse(serverNowUtc);
    if (!Number.isFinite(target) || !Number.isFinite(serverAtReceive)) return null;
    const clientAtReceive = receivedAtUtc ?? this.now();
    return Math.max(0, target - serverAtReceive - (this.now() - clientAtReceive));
  }

  format(milliseconds: number | null): string | null {
    if (milliseconds === null) return null;
    const total = Math.ceil(milliseconds / 1000);
    const days = Math.floor(total / 86400);
    const hours = Math.floor((total % 86400) / 3600);
    const minutes = Math.floor((total % 3600) / 60);
    const seconds = total % 60;
    return days > 0
      ? `${days}d ${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
      : `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
  }
}
