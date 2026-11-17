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
import { FeatureService } from '../../../core/services/feature.service';
import { NavigationItem } from '../../models/navigation.model';
import { ProfilePhotoService } from '../../services/profile-photo.service';
import { WarehouseUiVisibilityService } from '../../services/warehouse-ui-visibility.service';

export const navigation: NavigationItem[] = [
  { label: 'Overview', icon: 'space_dashboard', route: '/dashboard', permission: 'dashboard.view', feature: 'DASHBOARD' },
  {
    label: 'Point of Sale',
    icon: 'point_of_sale',
    permission: 'pos.view',
    feature: 'POS',
    children: [
      { label: 'New Sale', icon: 'add_shopping_cart', route: '/pos', permission: 'pos.create' },
      { label: 'Order Inbox', icon: 'inbox', route: '/order-inbox', permission: 'pos.view', feature: 'MULTI_CHANNEL_ORDER_INTAKE' },
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
    feature: 'PURCHASE',
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
    feature: 'PRODUCTS',
    children: [
      { label: 'Product List', icon: 'category', route: '/products', permission: 'product.view' },
      { label: 'Collections', icon: 'collections_bookmark', route: '/products/collections', permission: 'product.view', feature: 'COMMERCE_COLLECTIONS' },
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
    feature: 'INVENTORY',
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
      { label: 'Import / Update Stock', icon: 'upload_file', route: '/inventory/import-stock', permission: 'inventory.adjust' },
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
    label: 'Printing',
    icon: 'print',
    permission: 'print.view',
    feature: 'PRINTING',
    children: [
      {
        label: 'Label Designer',
        icon: 'label',
        route: '/print/labels',
        permission: 'print.document',
      },
      {
        label: 'Barcode & QR Generator',
        icon: 'qr_code_2',
        route: '/print/barcodes',
        permission: 'print.barcode',
      },
      {
        label: 'Print Templates',
        icon: 'description',
        route: '/print/templates',
        permission: 'print.view',
      },
      {
        label: 'Printer Configuration',
        icon: 'print',
        route: '/print/printers',
        permission: 'print.settings',
      },
    ],
  },
  {
    label: 'Parties',
    icon: 'groups',
    children: [
      { label: 'Customers', icon: 'person', route: '/customers', permission: 'customer.view', feature: 'CUSTOMERS' },
      { label: 'WhatsApp Contacts', icon: 'chat', route: '/customers/whatsapp-contacts', permission: 'customer.view', feature: 'WHATSAPP_COMMERCE' },
      { label: 'Customer Groups', icon: 'group_work', route: '/customer-groups', permission: 'customer.view', feature: 'CUSTOMERS' },
      {
        label: 'Suppliers',
        icon: 'local_shipping',
        route: '/suppliers',
        permission: 'supplier.view',
        feature: 'SUPPLIERS',
      },
    ],
  },
  {
    label: 'Storefront',
    icon: 'storefront',
    children: [
      { label: 'Storefront Dashboard', icon: 'space_dashboard', route: '/dashboard', permission: 'dashboard.view', feature: 'DASHBOARD' },
      { label: 'Storefront Customers', icon: 'people', route: '/storefront/customers', permission: 'customer.view', feature: 'CUSTOMER_DEMAND' },
      { label: 'Visitors & Customer Activity', icon: 'visibility', route: '/storefront/activity', permission: 'analytics.view', feature: 'ANALYTICS' },
      { label: 'Guest Visitor Analytics', icon: 'person_search', route: '/storefront/visitor-analytics', permission: 'analytics.view', feature: 'GUEST_VISITOR_ANALYTICS' },
      { label: 'Search Analytics', icon: 'manage_search', route: '/storefront/search-analytics', permission: 'analytics.view', feature: 'CUSTOMER_DEMAND' },
      { label: 'Storefront Orders', icon: 'shopping_cart', route: '/orders/storefront', permission: 'pos.view' },
      { label: 'Feedback & Complaints', icon: 'feedback', route: '/storefront/feedback', permission: 'customer.feedback.manage', feature: 'STOREFRONT_FEEDBACK' },
      { label: 'Blocked Customers', icon: 'block', route: '/storefront/blocked-customers', permission: 'customer.blacklist.manage', feature: 'STOREFRONT_CUSTOMER_BLACKLIST' },
      { label: 'Customer Demand', icon: 'search', route: '/storefront/customer-demand', permission: 'analytics.view', feature: 'CUSTOMER_DEMAND' },
    ],
  },
  {
    label: 'Finance',
    icon: 'account_balance',
    permission: 'ledger.view',
    feature: 'FINANCE',
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
  { label: 'Reports', icon: 'assessment', route: '/reports', permission: 'gst.view', feature: 'REPORTS' },
  { label: 'GST', icon: 'percent', route: '/gst', permission: 'gst.view', feature: 'GST' },
  {
    label: 'Analytics',
    icon: 'query_stats',
    permission: 'analytics.view',
    feature: 'ANALYTICS',
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
  { label: 'Warehouses', icon: 'factory', route: '/warehouses', permission: 'warehouse.view', feature: 'WAREHOUSES' },
  { label: 'My Deliveries', icon: 'two_wheeler', route: '/delivery', permission: 'delivery.view', feature: 'DELIVERY_MANAGEMENT' },
  { label: 'Delivery Management', icon: 'local_shipping', route: '/orders/deliveries', permission: 'delivery.manage', feature: 'DELIVERY_MANAGEMENT' },
  {
    label: 'Application Owner',
    icon: 'shield_person',
    role: 'ApplicationOwner',
    children: [
      { label: 'Owner Center', icon: 'dashboard', route: '/application-owner', role: 'ApplicationOwner' },
      { label: 'Marketplace Visitor Analytics', icon: 'visibility', route: '/application-owner/visitor-analytics', permission: 'feature.manage', role: 'ApplicationOwner' },
      { label: 'Tenant Features', icon: 'account_tree', route: '/admin/features', permission: 'feature.manage', role: 'ApplicationOwner' },
      { label: 'Demo Requests', icon: 'campaign', route: '/admin/demo-requests', role: 'ApplicationOwner' },
      { label: 'WhatsApp Platform', icon: 'hub', route: '/admin/whatsapp-platform', permission: 'feature.manage', role: 'ApplicationOwner' },
      { label: 'WhatsApp Business', icon: 'chat', route: '/application-owner/whatsapp-business', permission: 'feature.manage', role: 'ApplicationOwner' },
      { label: 'WhatsApp Ecommerce Demo', icon: 'forum', route: '/application-owner/whatsapp-demo', permission: 'feature.manage', role: 'ApplicationOwner' },
      { label: 'Payments', icon: 'receipt', route: '/application-owner/payments', permission: 'feature.manage', role: 'ApplicationOwner' },
      { label: 'Payment Settings', icon: 'payments', route: '/application-owner/payment-settings', permission: 'feature.manage', role: 'ApplicationOwner' },
      { label: 'Backup & Restore', icon: 'backup', route: '/admin/backup', role: 'ApplicationOwner' },
      { label: 'Audit Log', icon: 'history', route: '/admin/audit', role: 'ApplicationOwner' },
      { label: 'Login History', icon: 'login', route: '/admin/login-history', role: 'ApplicationOwner' },
      { label: 'System Logs & Errors', icon: 'troubleshoot', route: '/admin/system-logs', role: 'ApplicationOwner' },
    ],
  },
  {
    label: 'Administration',
    icon: 'admin_panel_settings',
    permission: 'admin.view',
    feature: 'ADMINISTRATION',
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
        label: 'Retailer Settings',
        icon: 'settings',
        route: '/admin/settings',
        permission: 'admin.settings',
      },
      { label: 'Storefront Branding', icon: 'storefront', route: '/admin/storefront', permission: 'admin.settings' },
      { label: 'Scheduled Selling Windows', icon: 'schedule', route: '/admin/storefront/schedules', permission: 'admin.settings', feature: 'STOREFRONT_SCHEDULED_SELLING' },
      { label: 'Storefront Popup Campaigns', icon: 'campaign', route: '/admin/storefront/campaigns', permission: 'admin.settings', feature: 'STOREFRONT_POPUP_CAMPAIGNS' },
      { label: 'Storefront Notifications', icon: 'notifications', route: '/admin/storefront/notifications', permission: 'admin.settings', feature: 'STOREFRONT_NOTIFICATIONS' },
      { label: 'Delivery Agents', icon: 'two_wheeler', route: '/admin/delivery-agents', permission: 'delivery.agent-manage', feature: 'DELIVERY_MANAGEMENT' },
      { label: 'Delivery Settings', icon: 'local_shipping', route: '/admin/delivery-settings', permission: 'delivery.settings', feature: 'DELIVERY_MANAGEMENT' },
      { label: 'WhatsApp Business', icon: 'chat', route: '/admin/whatsapp', permission: 'admin.settings', feature: 'WHATSAPP_COMMERCE' },
      { label: 'WhatsApp Ecommerce Demo', icon: 'forum', route: '/admin/whatsapp-demo', permission: 'pos.view', feature: 'WHATSAPP_COMMERCE' },
      { label: 'WhatsApp Usage & Billing', icon: 'receipt_long', route: '/admin/whatsapp-usage-billing', permission: 'admin.view', feature: 'WHATSAPP_COMMERCE' },
      { label: 'Payments', icon: 'receipt', route: '/admin/payments', permission: 'payment.view', feature: 'WHATSAPP_COMMERCE' },
      {
        label: 'Printer Configuration',
        icon: 'print',
        route: '/admin/printers',
        permission: 'print.settings',
      },
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
  private readonly features = inject(FeatureService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly profilePhoto = inject(ProfilePhotoService);
  private readonly warehouseUi = inject(WarehouseUiVisibilityService);
  readonly user = this.currentUser.user;
  readonly expanded = signal(new Set<string>(['Products']));
  readonly items = computed(() =>
    navigation
      .map((item) => ({
        ...item,
        children: item.children?.filter(
          (child) => (!child.permission || this.permissions.has(child.permission)) && (!child.feature || this.features.hasFeature(child.feature)) && (!child.role || this.hasRole(child.role)),
        ),
      }))
      .filter(
        (item) =>
          (!item.permission || this.permissions.has(item.permission)) &&
          (!item.feature || this.features.hasFeature(item.feature)) &&
          (!item.role || this.hasRole(item.role)) &&
          (this.warehouseUi.showManagement || !item.route?.startsWith('/warehouses')) &&
          (!item.children || item.children.length > 0),
      ),
  );
  private hasRole(role: string): boolean {
    return this.user()?.roles.includes(role) ?? false;
  }
  readonly initials = computed(() => (this.user()?.username ?? 'U').slice(0, 2).toUpperCase());
  readonly photo = this.profilePhoto.photo;
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
