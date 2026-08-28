import { Injectable, signal } from '@angular/core';

interface RuntimeConfiguration {
  apiBaseUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class RuntimeConfigurationService {
  readonly apiBaseUrl = signal('');

  async load(): Promise<void> {
    const response = await fetch('/runtime-config.json', { cache: 'no-store' });
    if (!response.ok) throw new Error('Runtime configuration could not be loaded.');

    const configuration = (await response.json()) as RuntimeConfiguration;
    this.apiBaseUrl.set(this.normalizeApiBaseUrl(configuration.apiBaseUrl));
  }

  private normalizeApiBaseUrl(value: string | undefined): string {
    const candidate = value?.trim().replace(/\/$/, '') ?? '';
    if (!candidate) return '';

    const url = new URL(candidate);
    if (
      !['http:', 'https:'].includes(url.protocol) ||
      url.username ||
      url.password ||
      url.pathname !== '/' ||
      url.search ||
      url.hash
    ) {
      throw new Error('apiBaseUrl must be an HTTP(S) origin without credentials or a path.');
    }
    return url.origin;
  }
}
