import { isDevMode } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideServiceWorker } from '@angular/service-worker';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { environment } from './environments/environment';
import { setCustomStoreDomain } from './app/custom-store-domain';
import { markStorefrontStartup, measureStorefrontStartup } from './app/storefront-startup-performance';

async function resolveStoreDomain(): Promise<string | null> {
  if (environment.platformStorefrontHosts.includes(window.location.hostname.toLowerCase())) {
    markStorefrontStartup('custom-domain-resolution-skipped', { host: window.location.hostname });
    return null;
  }
  markStorefrontStartup('custom-domain-resolution-start', { host: window.location.hostname });
  try {
    const response = await fetch(`${environment.apiBaseUrl}/api/storefront-domain/resolve`, {
      headers: { Accept: 'application/json' },
      signal: AbortSignal.timeout(2500),
    });
    if (!response.ok) return null;
    const result: unknown = await response.json();
    if (typeof result !== 'object' || result === null || !('storeKey' in result) || typeof result.storeKey !== 'string'
      || !/^[a-z0-9_-]{1,100}$/i.test(result.storeKey)) return null;
    return result.storeKey.toLowerCase();
  } catch { return null; }
  finally {
    markStorefrontStartup('custom-domain-resolution-complete', { host: window.location.hostname });
    measureStorefrontStartup('custom-domain-resolution', 'custom-domain-resolution-start', 'custom-domain-resolution-complete');
  }
}

function attachTenantManifest(customStoreKey: string | null): void {
  const key = customStoreKey ?? window.location.pathname.split('/').filter(Boolean)[0]?.toLowerCase() ?? '';
  if (!/^[a-z0-9_-]{1,100}$/.test(key)) return;
  const link = document.querySelector<HTMLLinkElement>('#tenant-manifest');
  if (!link) return;
  link.href = `${environment.apiBaseUrl}/api/store/${encodeURIComponent(key)}/pwa/manifest.webmanifest?shopOrigin=${encodeURIComponent(window.location.origin)}`;
}

void resolveStoreDomain().then(customStoreKey => {
  setCustomStoreDomain(customStoreKey);
  attachTenantManifest(customStoreKey);
  return bootstrapApplication(App, {
  ...appConfig,
  providers: [...appConfig.providers, provideServiceWorker('ngsw-worker.js', {
    enabled: !isDevMode(),
    registrationStrategy: 'registerWhenStable:30000',
  })],
  });
}).catch((error: unknown) => console.error(error));
