import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class LayoutStateService {
  readonly sidebarCollapsed = signal(localStorage.getItem('wb.sidebar.collapsed') === 'true');
  readonly mobileOpen = signal(false);
  readonly sidebarWidth = computed(() => (this.sidebarCollapsed() ? 80 : 272));
  readonly darkMode = signal(localStorage.getItem('wb.theme') === 'dark' || (!localStorage.getItem('wb.theme') && matchMedia('(prefers-color-scheme: dark)').matches));

  constructor() { this.applyTheme(); }

  toggleSidebar(): void {
    this.sidebarCollapsed.update((value) => !value);
    localStorage.setItem('wb.sidebar.collapsed', String(this.sidebarCollapsed()));
  }

  toggleMobile(): void { this.mobileOpen.update((value) => !value); }
  closeMobile(): void { this.mobileOpen.set(false); }
  toggleTheme(): void { this.darkMode.update(value => !value); localStorage.setItem('wb.theme', this.darkMode() ? 'dark' : 'light'); this.applyTheme(); }
  private applyTheme(): void { document.documentElement.dataset['theme'] = this.darkMode() ? 'dark' : 'light'; }
}
