import { Injectable, signal } from '@angular/core';
@Injectable({ providedIn: 'root' })
export class RuntimeConfigurationService {
  readonly apiBaseUrl = signal('/api');
}
