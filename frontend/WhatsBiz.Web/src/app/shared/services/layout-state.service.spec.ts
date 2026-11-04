import { LayoutStateService } from './layout-state.service';

describe('LayoutStateService theme initialization', () => {
  beforeEach(() => {
    localStorage.removeItem('wb.theme');
    document.documentElement.removeAttribute('data-theme');
  });

  it('starts in light mode when there is no saved theme preference', () => {
    const service = new LayoutStateService();

    expect(service.darkMode()).toBeFalse();
    expect(document.documentElement.dataset['theme']).toBe('light');
  });

  it('restores an explicitly selected dark theme and persists subsequent toggles', () => {
    localStorage.setItem('wb.theme', 'dark');
    const service = new LayoutStateService();

    expect(service.darkMode()).toBeTrue();
    expect(document.documentElement.dataset['theme']).toBe('dark');

    service.toggleTheme();

    expect(service.darkMode()).toBeFalse();
    expect(localStorage.getItem('wb.theme')).toBe('light');
  });
});
