import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class LayoutStateService {
  readonly sidebarCollapsed = signal(localStorage.getItem('wb.sidebar.collapsed') === 'true');
  readonly mobileOpen = signal(false);
  readonly sidebarWidth = computed(() => (this.sidebarCollapsed() ? 80 : 272));

  toggleSidebar(): void {
    this.sidebarCollapsed.update((value) => !value);
    localStorage.setItem('wb.sidebar.collapsed', String(this.sidebarCollapsed()));
  }

  toggleMobile(): void { this.mobileOpen.update((value) => !value); }
  closeMobile(): void { this.mobileOpen.set(false); }
}
