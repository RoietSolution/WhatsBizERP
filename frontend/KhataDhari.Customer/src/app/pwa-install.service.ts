import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { environment } from '../environments/environment';
import { Store } from './models/storefront.models';

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

@Injectable({ providedIn: 'root' })
export class PwaInstallService {
  readonly canInstall = signal(false);
  readonly iosInstallHint = signal(false);
  readonly installing = signal(false);
  private deferredPrompt: BeforeInstallPromptEvent | null = null;
  private installed = false;
  private currentStoreKey = '';
  private readonly browser: boolean;

  constructor(@Inject(DOCUMENT) private readonly document: Document, @Inject(PLATFORM_ID) platformId: object) {
    this.browser = isPlatformBrowser(platformId);
    if (!this.browser) return;
    window.addEventListener('beforeinstallprompt', this.capturePrompt as EventListener);
    window.addEventListener('appinstalled', this.handleInstalled);
    this.installed = this.isStandalone();
  }

  configure(store: Store): void {
    if (!this.browser) return;
    this.currentStoreKey = store.storeKey.toLowerCase();
    const manifest = this.document.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    if (manifest) {
      const origin = encodeURIComponent(window.location.origin);
      manifest.href = environment.apiBaseUrl + '/api/store/' + encodeURIComponent(this.currentStoreKey) + '/pwa/manifest.webmanifest?shopOrigin=' + origin;
    }
    this.document.title = store.name + ' | KhataDhari';
    const theme = this.document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
    if (theme) theme.content = store.accentColor;
    const icon = this.document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    if (icon && store.logoUrl) icon.href = store.logoUrl;
    const dismissed = localStorage.getItem(this.dismissKey()) === '1';
    this.canInstall.set(!this.installed && !dismissed && this.deferredPrompt !== null);
    this.iosInstallHint.set(!this.installed && this.isIos() && !dismissed);
  }

  async install(): Promise<void> {
    if (!this.deferredPrompt || this.installed) return;
    this.installing.set(true);
    const prompt = this.deferredPrompt;
    this.deferredPrompt = null;
    this.canInstall.set(false);
    try {
      await prompt.prompt();
      if ((await prompt.userChoice).outcome === 'dismissed') localStorage.setItem(this.dismissKey(), '1');
    } finally {
      this.installing.set(false);
    }
  }

  dismissInstall(): void {
    if (!this.browser) return;
    localStorage.setItem(this.dismissKey(), '1');
    this.canInstall.set(false);
    this.iosInstallHint.set(false);
  }

  private readonly capturePrompt = (event: Event): void => {
    event.preventDefault();
    this.deferredPrompt = event as BeforeInstallPromptEvent;
    const dismissed = this.currentStoreKey && localStorage.getItem(this.dismissKey()) === '1';
    this.canInstall.set(!this.installed && !dismissed);
  };

  private readonly handleInstalled = (): void => {
    this.installed = true;
    this.deferredPrompt = null;
    this.canInstall.set(false);
    this.iosInstallHint.set(false);
  };

  private isStandalone(): boolean {
    return window.matchMedia('(display-mode: standalone)').matches
      || (navigator as Navigator & { standalone?: boolean }).standalone === true;
  }

  private isIos(): boolean {
    return /iPhone|iPad|iPod/i.test(navigator.userAgent)
      || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  }

  private dismissKey(): string {
    return 'khatadhari.pwa-install-dismissed.' + this.currentStoreKey;
  }
}