import { Injectable, signal } from '@angular/core';
import { LayoutStateService } from './layout-state.service';

export interface UserPreferences { theme: 'light' | 'dark'; language: string; dateFormat: string; timeFormat: string; currency: string; gridDensity: string; defaultDashboard: string; emailNotifications: boolean; pushNotifications: boolean; }
const defaults: UserPreferences = { theme: 'light', language: 'English', dateFormat: 'dd/MM/yyyy', timeFormat: '12-hour', currency: 'INR', gridDensity: 'Comfortable', defaultDashboard: 'Executive Dashboard', emailNotifications: true, pushNotifications: true };

@Injectable({ providedIn: 'root' })
export class UserPreferencesService {
  private readonly key = 'khatadhari.user.preferences';
  readonly preferences = signal<UserPreferences>(this.read());
  constructor(private readonly layout: LayoutStateService) {}
  save(value: UserPreferences): void { this.preferences.set(value); localStorage.setItem(this.key, JSON.stringify(value)); if ((value.theme === 'dark') !== this.layout.darkMode()) this.layout.toggleTheme(); }
  private read(): UserPreferences { try { return { ...defaults, ...JSON.parse(localStorage.getItem(this.key) ?? '{}'), theme: localStorage.getItem('wb.theme') === 'dark' ? 'dark' : 'light' }; } catch { return defaults; } }
}
