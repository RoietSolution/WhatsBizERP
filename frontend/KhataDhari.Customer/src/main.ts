import { isDevMode } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideServiceWorker } from '@angular/service-worker';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { environment } from './environments/environment';

function attachTenantManifest(): void {
  const key = window.location.pathname.split('/').filter(Boolean)[0]?.toLowerCase() ?? '';
  if (!/^[a-z0-9_-]{1,100}$/.test(key)) return;
  const link = document.querySelector<HTMLLinkElement>('#tenant-manifest');
  if (!link) return;
  link.href = `${environment.apiBaseUrl}/api/store/${encodeURIComponent(key)}/pwa/manifest.webmanifest?shopOrigin=${encodeURIComponent(window.location.origin)}`;
}

attachTenantManifest();
bootstrapApplication(App, {
  ...appConfig,
  providers: [...appConfig.providers, provideServiceWorker('ngsw-worker.js', {
    enabled: !isDevMode(),
    registrationStrategy: 'registerWhenStable:30000',
  })],
}).catch((error: unknown) => console.error(error));
