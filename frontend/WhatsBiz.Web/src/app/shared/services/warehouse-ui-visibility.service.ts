import { Injectable } from '@angular/core';

export interface WarehouseChoice {
  warehouseId: string;
  isDefault?: boolean;
}

@Injectable({ providedIn: 'root' })
export class WarehouseUiVisibilityService {
  readonly showManagement = false;
  readonly showOptionalSelectors = false;

  resolveDefaultOrOnly<T extends WarehouseChoice>(items: readonly T[]): string | null {
    const defaults = items.filter((item) => item.isDefault);
    if (defaults.length === 1) return defaults[0].warehouseId;
    if (defaults.length > 1) return null;
    return items.length === 1 ? items[0].warehouseId : null;
  }

  needsRequiredFallback(warehouseId: string | null | undefined): boolean {
    return !this.showOptionalSelectors && !warehouseId;
  }
}
