import { Injectable, computed, signal } from '@angular/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter } from 'rxjs';

interface InstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

@Injectable({ providedIn: 'root' })
export class PwaService {
  readonly offline = signal(typeof navigator !== 'undefined' && !navigator.onLine);
  readonly standalone = signal(this.detectStandalone());
  readonly updateAvailable = signal(false);
  readonly installDismissed = signal(this.wasRecentlyDismissed());
  private readonly deferredPrompt = signal<InstallPromptEvent | null>(null);
  readonly installAvailable = computed(
    () => !!this.deferredPrompt() && !this.standalone() && !this.installDismissed(),
  );
  readonly iosInstallHint = computed(
    () => this.isIos && !this.standalone() && !this.installDismissed() && !this.installAvailable(),
  );
  private readonly isIos =
    typeof navigator !== 'undefined' && /iphone|ipad|ipod/i.test(navigator.userAgent);

  constructor(private readonly updates: SwUpdate) {
    if (typeof window !== 'undefined') {
      window.addEventListener('beforeinstallprompt', this.onBeforeInstallPrompt);
      window.addEventListener('appinstalled', this.onAppInstalled);
      window.addEventListener('online', this.onOnline);
      window.addEventListener('offline', this.onOffline);
      window.matchMedia('(display-mode: standalone)').addEventListener('change', this.onDisplayModeChange);
    }
    if (updates.isEnabled) {
      updates.versionUpdates
        .pipe(filter((event): event is VersionReadyEvent => event.type === 'VERSION_READY'))
        .subscribe(() => this.updateAvailable.set(true));
    }
  }

  async install(): Promise<void> {
    const prompt = this.deferredPrompt();
    if (!prompt) return;
    this.deferredPrompt.set(null);
    await prompt.prompt();
    const choice = await prompt.userChoice;
    if (choice.outcome === 'dismissed') this.dismissInstall();
  }

  dismissInstall(): void {
    this.installDismissed.set(true);
    try { localStorage.setItem('whatsbiz.pwa.install-dismissed-until', String(Date.now() + 7 * 86400000)); } catch {}
  }

  async update(): Promise<void> {
    if (!this.updates.isEnabled) return;
    try {
      await this.updates.activateUpdate();
      window.location.reload();
    } catch {
      this.updateAvailable.set(false);
    }
  }

  private readonly onBeforeInstallPrompt = (event: Event): void => {
    event.preventDefault();
    this.deferredPrompt.set(event as InstallPromptEvent);
  };
  private readonly onAppInstalled = (): void => {
    this.deferredPrompt.set(null);
    this.standalone.set(true);
  };
  private readonly onOnline = (): void => this.offline.set(false);
  private readonly onOffline = (): void => this.offline.set(true);
  private readonly onDisplayModeChange = (event: MediaQueryListEvent): void => this.standalone.set(event.matches || this.detectStandalone());
  private detectStandalone(): boolean {
    return typeof window !== 'undefined' &&
      (window.matchMedia('(display-mode: standalone)').matches ||
        (navigator as Navigator & { standalone?: boolean }).standalone === true);
  }
  private wasRecentlyDismissed(): boolean {
    try { return Number(localStorage.getItem('whatsbiz.pwa.install-dismissed-until') ?? 0) > Date.now(); } catch { return false; }
  }
}