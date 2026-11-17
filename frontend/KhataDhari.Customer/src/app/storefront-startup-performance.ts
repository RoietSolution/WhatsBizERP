const prefix = 'guturgo:startup:';

export function markStorefrontStartup(name: string, detail?: unknown): void {
  if (typeof performance === 'undefined') return;
  try {
    performance.mark(prefix + name, detail === undefined ? undefined : { detail });
  } catch {
    performance.mark(prefix + name);
  }
}

export function measureStorefrontStartup(name: string, start: string, end: string): void {
  if (typeof performance === 'undefined') return;
  try { performance.measure(prefix + name, prefix + start, prefix + end); } catch { /* A repeated route may not have both marks. */ }
}

export function scheduleStorefrontIdle(callback: () => void): void {
  if (typeof window === 'undefined') return;
  const idleWindow = window as Window & { requestIdleCallback?: (callback: () => void, options?: { timeout: number }) => number };
  if (idleWindow.requestIdleCallback) idleWindow.requestIdleCallback(callback, { timeout: 2000 });
  else window.setTimeout(callback, 0);
}
