import { computed, Injectable, signal } from '@angular/core';
import { CartLine, Product } from '../models/storefront.models';

@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly activeStore = signal('');
  private activeCustomer = '';
  readonly storeKey = computed(() => this.activeStore());
  readonly lines = signal<CartLine[]>([]);
  readonly itemCount = computed(() => this.lines().reduce((sum, line) => sum + line.quantity, 0));
  readonly total = computed(() => this.lines().reduce((sum, line) => sum + line.product.sellingPrice * line.quantity, 0));

  useStore(storeKey: string): void {
    const normalized = this.normalizeStore(storeKey);
    if (this.activeStore() === normalized) return;
    this.activeStore.set(normalized);
    this.activeCustomer = '';
    this.lines.set(this.read(this.storageKey(normalized, '')));
  }

  switchCustomer(customerId: string | null): void {
    const storeKey = this.activeStore();
    if (!storeKey) return;
    const nextCustomer = customerId?.trim().toLowerCase() ?? '';
    if (this.activeCustomer === nextCustomer) return;
    const previousCustomer = this.activeCustomer;
    const guestLines = previousCustomer ? [] : this.lines();
    const ownedKey = this.storageKey(storeKey, nextCustomer);
    const ownedLines = nextCustomer ? this.read(ownedKey) : this.read(this.storageKey(storeKey, ''));
    const adoptedGuest = nextCustomer && !ownedLines.length ? guestLines : [];
    this.activeCustomer = nextCustomer;
    this.lines.set(ownedLines.length ? ownedLines : adoptedGuest);
    this.persist();
    if (nextCustomer && adoptedGuest.length) this.removeStorage(this.storageKey(storeKey, ''));
  }

  add(product: Product): boolean {
    if (!product.available) return false;
    this.lines.update((lines) => {
      const existing = lines.find((line) => line.product.id === product.id);
      return existing
        ? lines.map((line) => line.product.id === product.id ? { ...line, product, quantity: line.quantity + 1 } : line)
        : [...lines, { product, quantity: 1 }];
    });
    this.persist();
    return true;
  }

  adjust(productId: string, change: number): void {
    this.lines.update((lines) => lines
      .map((line) => line.product.id === productId ? { ...line, quantity: Math.max(0, line.quantity + change) } : line)
      .filter((line) => line.quantity > 0));
    this.persist();
  }

  refreshProducts(products: readonly Product[]): void {
    const current = new Map(products.map((product) => [product.id, product]));
    this.lines.update((lines) => lines.map((line) => ({
      ...line,
      product: current.get(line.product.id) ?? { ...line.product, available: false },
    })));
    this.persist();
  }

  remove(productId: string): void {
    this.lines.update((lines) => lines.filter((line) => line.product.id !== productId));
    this.persist();
  }

  clear(): void { this.lines.set([]); this.persist(); }

  private persist(): void {
    const storeKey = this.activeStore();
    if (!storeKey || typeof localStorage === 'undefined') return;
    try {
      localStorage.setItem(this.storageKey(storeKey, this.activeCustomer), JSON.stringify(this.lines()));
    } catch {
      // Shopping can continue in browsers that block storage or have no quota.
    }
  }

  private read(key: string): CartLine[] {
    if (typeof localStorage === 'undefined') return [];
    try {
      const data: unknown = JSON.parse(localStorage.getItem(key) ?? '[]');
      if (!Array.isArray(data)) return [];
      return data.filter((line): line is CartLine => this.isCartLine(line));
    } catch {
      return [];
    }
  }

  private isCartLine(value: unknown): value is CartLine {
    if (typeof value !== 'object' || value === null) return false;
    const line = value as Partial<CartLine>;
    return typeof line.quantity === 'number' && line.quantity > 0
      && typeof line.product === 'object' && line.product !== null
      && typeof line.product.id === 'string' && typeof line.product.sellingPrice === 'number';
  }

  private normalizeStore(storeKey: string): string { return storeKey.trim().toLowerCase(); }
  private storageKey(storeKey: string, customerId: string): string { return `khata-dhari-cart:${storeKey}:${customerId || 'guest'}`; }
  private removeStorage(key: string): void { try { localStorage.removeItem(key); } catch {} }
}