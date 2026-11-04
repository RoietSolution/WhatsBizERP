export interface QuickAccessLink {
  label: string;
  icon: string;
  route: string;
  permission: string;
  feature: string;
}

export const mobileQuickAccessLinks: QuickAccessLink[] = [
  { label: 'Home', icon: 'home', route: '/dashboard', permission: 'dashboard.view', feature: 'DASHBOARD' },
  { label: 'Billing', icon: 'point_of_sale', route: '/pos', permission: 'pos.create', feature: 'POS' },
  { label: 'Products', icon: 'inventory_2', route: '/products', permission: 'product.view', feature: 'PRODUCTS' },
  { label: 'Inventory', icon: 'inventory', route: '/inventory', permission: 'inventory.view', feature: 'INVENTORY' },
  { label: 'Purchase', icon: 'shopping_cart', route: '/purchases/dashboard', permission: 'purchase.view', feature: 'PURCHASE' },
];
export function visibleQuickAccessLinks(
  links: readonly QuickAccessLink[],
  hasPermission: (permission: string) => boolean,
  hasFeature: (feature: string) => boolean,
): QuickAccessLink[] {
  return links.filter((link) => hasPermission(link.permission) && hasFeature(link.feature));
}
