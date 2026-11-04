import { WarehouseUiVisibilityService } from './warehouse-ui-visibility.service';

describe('WarehouseUiVisibilityService', () => {
  const service = new WarehouseUiVisibilityService();

  it('uses the configured default or a sole location and never guesses between multiple locations', () => {
    expect(service.resolveDefaultOrOnly([{ warehouseId: 'a', isDefault: true }, { warehouseId: 'b' }])).toBe('a');
    expect(service.resolveDefaultOrOnly([{ warehouseId: 'only' }])).toBe('only');
    expect(service.resolveDefaultOrOnly([{ warehouseId: 'a' }, { warehouseId: 'b' }])).toBeNull();
  });

  it('keeps standalone warehouse management hidden', () => {
    expect(service.showManagement).toBeFalse();
  });
});
