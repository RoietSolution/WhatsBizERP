import { mobileQuickAccessLinks, QuickAccessLink, visibleQuickAccessLinks } from './quick-access-links';

describe('visibleQuickAccessLinks', () => {
  const links: QuickAccessLink[] = [
    { label: 'Sales', icon: 'point_of_sale', route: '/pos', permission: 'pos.create', feature: 'POS' },
    { label: 'Products', icon: 'inventory_2', route: '/products', permission: 'product.view', feature: 'PRODUCTS' },
  ];

  it('maps the mobile shortcuts to the requested destinations in order', () => {
    expect(mobileQuickAccessLinks.map((item) => [item.label, item.route])).toEqual([
      ['Home', '/dashboard'],
      ['Billing', '/pos'],
      ['Products', '/products'],
      ['Inventory', '/inventory'],
      ['Purchase', '/purchases/dashboard'],
    ]);
  });
  it('shows only links allowed by both permission and effective feature', () => {
    const result = visibleQuickAccessLinks(
      links,
      (permission) => permission === 'product.view',
      (feature) => feature === 'PRODUCTS',
    );
    expect(result.map((item) => item.label)).toEqual(['Products']);
  });

  it('does not expose links when the permission is present but the feature is disabled', () => {
    expect(visibleQuickAccessLinks(links, () => true, () => false)).toEqual([]);
  });
});
