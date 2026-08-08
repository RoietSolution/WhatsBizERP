import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CurrentUserService } from '../../../core/services/current-user.service';
import { PermissionService } from '../../../core/services/permission.service';
import { NavigationItem } from '../../models/navigation.model';

const navigation: NavigationItem[] = [
  { label: 'Overview', icon: 'space_dashboard', route: '/dashboard', permission: 'dashboard.view' },
  {
    label: 'Point of Sale',
    icon: 'point_of_sale',
    permission: 'pos.view',
    children: [
      { label: 'New Sale', icon: 'add_shopping_cart', route: '/pos', permission: 'pos.create' },
      { label: 'Today’s Sales', icon: 'today', route: '/pos/today', permission: 'pos.view' },
      {
        label: 'Invoice History',
        icon: 'receipt_long',
        route: '/pos/history',
        permission: 'pos.view',
      },
      {
        label: 'Returns',
        icon: 'assignment_return',
        route: '/pos/returns',
        permission: 'pos.return',
      },
    ],
  },
  {
    label: 'Purchases',
    icon: 'shopping_cart',
    permission: 'purchase.view',
    children: [
      {
        label: 'Dashboard',
        icon: 'monitoring',
        route: '/purchases/dashboard',
        permission: 'purchase.view',
      },
      {
        label: 'Purchase List',
        icon: 'list_alt',
        route: '/purchases',
        permission: 'purchase.view',
      },
      {
        label: 'Create Purchase',
        icon: 'add_box',
        route: '/purchases/create',
        permission: 'purchase.create',
      },
    ],
  },
  {
    label: 'Products',
    icon: 'inventory_2',
    permission: 'product.view',
    children: [
      { label: 'Product List', icon: 'category', route: '/products', permission: 'product.view' },
      {
        label: 'Categories',
        icon: 'account_tree',
        route: '/product-categories',
        permission: 'product.view',
      },
      { label: 'Brands', icon: 'sell', route: '/brands', permission: 'product.view' },
      { label: 'Units', icon: 'straighten', route: '/units', permission: 'product.view' },
    ],
  },
  {
    label: 'Inventory',
    icon: 'warehouse',
    permission: 'inventory.view',
    children: [
      { label: 'Overview', icon: 'dashboard', route: '/inventory', permission: 'inventory.view' },
      {
        label: 'Stock Balance',
        icon: 'inventory',
        route: '/inventory/balance',
        permission: 'inventory.view',
      },
      {
        label: 'Transactions',
        icon: 'swap_horiz',
        route: '/inventory/transactions',
        permission: 'inventory.view',
      },
      {
        label: 'Adjustments',
        icon: 'tune',
        route: '/inventory/adjustment',
        permission: 'inventory.adjust',
      },
      {
        label: 'Transfers',
        icon: 'move_up',
        route: '/inventory/transfer',
        permission: 'inventory.transfer',
      },
      {
        label: 'Alerts',
        icon: 'notification_important',
        route: '/inventory/alerts',
        permission: 'inventory.alerts',
      },
    ],
  },
  {
    label: 'Parties',
    icon: 'groups',
    children: [
      { label: 'Customers', icon: 'person', route: '/customers', permission: 'customer.view' },
      {
        label: 'Suppliers',
        icon: 'local_shipping',
        route: '/suppliers',
        permission: 'supplier.view',
      },
    ],
  },
  {
    label: 'Finance',
    icon: 'account_balance',
    permission: 'ledger.view',
    children: [
      {
        label: 'Day Book',
        icon: 'menu_book',
        route: '/finance/daybook',
        permission: 'ledger.view',
      },
      {
        label: 'Cash Book',
        icon: 'payments',
        route: '/finance/cashbook',
        permission: 'cashbook.view',
      },
      {
        label: 'Bank Book',
        icon: 'account_balance',
        route: '/finance/bankbook',
        permission: 'bankbook.view',
      },
      {
        label: 'Receipts',
        icon: 'call_received',
        route: '/finance/receipt',
        permission: 'receipt.create',
      },
      {
        label: 'Payments',
        icon: 'call_made',
        route: '/finance/payment',
        permission: 'payment.create',
      },
    ],
  },
  { label: 'Reports', icon: 'assessment', route: '/reports', permission: 'gst.view' },
  { label: 'GST', icon: 'percent', route: '/gst', permission: 'gst.view' },
  {
    label: 'Analytics',
    icon: 'query_stats',
    permission: 'analytics.view',
    children: [
      {
        label: 'Sales',
        icon: 'trending_up',
        route: '/analytics/sales',
        permission: 'analytics.view',
      },
      {
        label: 'Purchases',
        icon: 'shopping_bag',
        route: '/analytics/purchase',
        permission: 'analytics.view',
      },
      {
        label: 'Inventory',
        icon: 'inventory',
        route: '/analytics/inventory',
        permission: 'analytics.view',
      },
      {
        label: 'Finance',
        icon: 'currency_rupee',
        route: '/analytics/finance',
        permission: 'analytics.view',
      },
    ],
  },
  { label: 'Warehouses', icon: 'factory', route: '/warehouses', permission: 'warehouse.view' },
  {
    label: 'Administration',
    icon: 'admin_panel_settings',
    permission: 'admin.view',
    children: [
      { label: 'Admin Center', icon: 'dashboard', route: '/admin', permission: 'admin.view' },
      {
        label: 'Company Profile',
        icon: 'business',
        route: '/admin/company',
        permission: 'admin.company',
      },
      { label: 'Branches', icon: 'lan', route: '/admin/branches', permission: 'admin.settings' },
      {
        label: 'Settings',
        icon: 'settings',
        route: '/admin/settings',
        permission: 'admin.settings',
      },
      { label: 'Audit Log', icon: 'history', route: '/admin/audit', permission: 'admin.audit' },
      { label: 'Backup', icon: 'backup', route: '/admin/backup', permission: 'admin.backup' },
    ],
  },
];

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SidebarComponent {
  readonly collapsed = input(false);
  readonly collapseToggle = output<void>();
  readonly navigate = output<void>();
  private readonly permissions = inject(PermissionService);
  private readonly currentUser = inject(CurrentUserService);
  readonly user = this.currentUser.user;
  readonly expanded = signal(new Set<string>(['Products']));
  readonly items = computed(() =>
    navigation
      .map((item) => ({
        ...item,
        children: item.children?.filter(
          (child) => !child.permission || this.permissions.has(child.permission),
        ),
      }))
      .filter(
        (item) =>
          (!item.permission || this.permissions.has(item.permission)) &&
          (!item.children || item.children.length > 0),
      ),
  );
  readonly initials = computed(() => (this.user()?.username ?? 'U').slice(0, 2).toUpperCase());
  toggle(label: string): void {
    if (this.collapsed()) {
      this.collapseToggle.emit();
    }
    this.expanded.update((current) => {
      const next = new Set(current);
      next.has(label) ? next.delete(label) : next.add(label);
      return next;
    });
  }
  isExpanded(label: string): boolean {
    return this.expanded().has(label);
  }
}
