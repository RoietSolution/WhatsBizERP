import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { permissionGuard } from './core/guards/permission.guard';
import { featureGuard } from './core/guards/feature.guard';
const productView = { permission: 'product.view' },
  productCreate = { permission: 'product.create' },
  productEdit = { permission: 'product.edit' };
const supplierView = { permission: 'supplier.view' },
  supplierCreate = { permission: 'supplier.create' },
  supplierEdit = { permission: 'supplier.edit' };
const customerView = { permission: 'customer.view' },
  customerCreate = { permission: 'customer.create' },
  customerEdit = { permission: 'customer.edit' };
const warehouseView = { permission: 'warehouse.view' },
  warehouseCreate = { permission: 'warehouse.create' },
  warehouseEdit = { permission: 'warehouse.edit' };
const inventoryView = { permission: 'inventory.view' },
  inventoryAdjust = { permission: 'inventory.adjust' },
  inventoryTransfer = { permission: 'inventory.transfer' },
  inventoryReserve = { permission: 'inventory.reserve' },
  inventoryVerify = { permission: 'inventory.verify' },
  inventoryReorder = { permission: 'inventory.reorder' },
  inventoryAlerts = { permission: 'inventory.alerts' };
const posView = { permission: 'pos.view' },
  posCreate = { permission: 'pos.create' },
  posEdit = { permission: 'pos.edit' },
  posReturn = { permission: 'pos.return' };
const purchaseView = { permission: 'purchase.view' },
  purchaseCreate = { permission: 'purchase.create' },
  purchaseEdit = { permission: 'purchase.edit' },
  purchaseReturn = { permission: 'purchase.return' },
  purchasePayment = { permission: 'purchase.payment' };
const ledgerView = { permission: 'ledger.view' },
  receiptView = { permission: 'receipt.view' },
  receiptCreate = { permission: 'receipt.create' },
  paymentView = { permission: 'payment.view' },
  paymentCreate = { permission: 'payment.create' },
  customerOutstanding = { permission: 'customer.outstanding.view' },
  supplierOutstanding = { permission: 'supplier.outstanding.view' },
  cashbookView = { permission: 'cashbook.view' },
  bankbookView = { permission: 'bankbook.view' };
const dashboardView = { permission: 'dashboard.view' },
  analyticsView = { permission: 'analytics.view' };
const gstView = { permission: 'gst.view' },
  gstConfiguration = { permission: 'gst.configuration' };
const printView = { permission: 'print.view' },
  printDocument = { permission: 'print.document' },
  printBarcode = { permission: 'print.barcode' },
  printSettings = { permission: 'print.settings' };
const adminView = { permission: 'admin.view' },
  adminSettings = { permission: 'admin.settings' },
  adminCompany = { permission: 'admin.company' },
  adminBackup = { permission: 'admin.backup' },
  adminRestore = { permission: 'admin.restore' },
  adminAudit = { permission: 'admin.audit' };
export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./layout/authentication-layout/authentication-layout.component').then(
        (m) => m.AuthenticationLayoutComponent,
      ),
    children: [
      {
        path: 'login',
        title: 'Sign In | KhataDhari ERP',
        loadComponent: () =>
          import('./features/authentication/login/login.component').then((m) => m.LoginComponent),
      },
      {
        path: 'forgot-password',
        title: 'Forgot Password | KhataDhari ERP',
        data: { mode: 'forgot' },
        loadComponent: () =>
          import('./features/authentication/password/password-workflow.component').then(
            (m) => m.PasswordWorkflowComponent,
          ),
      },
      {
        path: 'reset-password',
        title: 'Reset Password | KhataDhari ERP',
        data: { mode: 'reset' },
        loadComponent: () =>
          import('./features/authentication/password/password-workflow.component').then(
            (m) => m.PasswordWorkflowComponent,
          ),
      },
      {
        path: 'change-password',
        title: 'Change Password | KhataDhari ERP',
        canActivate: [authGuard],
        data: { mode: 'change' },
        loadComponent: () =>
          import('./features/authentication/password/password-workflow.component').then(
            (m) => m.PasswordWorkflowComponent,
          ),
      },
      {
        path: 'account-locked',
        title: 'Account Locked | KhataDhari ERP',
        data: { mode: 'locked' },
        loadComponent: () =>
          import('./features/authentication/state/authentication-state.component').then(
            (m) => m.AuthenticationStateComponent,
          ),
      },
      {
        path: 'session-expired',
        title: 'Session Expired | KhataDhari ERP',
        data: { mode: 'expired' },
        loadComponent: () =>
          import('./features/authentication/state/authentication-state.component').then(
            (m) => m.AuthenticationStateComponent,
          ),
      },
      {
        path: '403',
        title: 'Access Denied | KhataDhari ERP',
        data: { mode: 'denied' },
        loadComponent: () =>
          import('./features/authentication/state/authentication-state.component').then(
            (m) => m.AuthenticationStateComponent,
          ),
      },
      {
        path: 'unauthorized',
        title: 'Access Denied | KhataDhari ERP',
        data: { mode: 'denied' },
        loadComponent: () =>
          import('./features/authentication/state/authentication-state.component').then(
            (m) => m.AuthenticationStateComponent,
          ),
      },
    ],
  },
  {
    path: '401',
    data: { mode: '401' },
    loadComponent: () =>
      import('./features/system-state/system-state.component').then((m) => m.SystemStateComponent),
  },
  {
    path: '404',
    data: { mode: '404' },
    loadComponent: () =>
      import('./features/system-state/system-state.component').then((m) => m.SystemStateComponent),
  },
  {
    path: '500',
    data: { mode: '500' },
    loadComponent: () =>
      import('./features/system-state/system-state.component').then((m) => m.SystemStateComponent),
  },
  {
    path: 'offline',
    data: { mode: 'offline' },
    loadComponent: () =>
      import('./features/system-state/system-state.component').then((m) => m.SystemStateComponent),
  },
  {
    path: 'maintenance',
    data: { mode: 'maintenance' },
    loadComponent: () =>
      import('./features/system-state/system-state.component').then((m) => m.SystemStateComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./layout/main-layout/main-layout.component').then((m) => m.MainLayoutComponent),
    children: [
      {
        path: 'profile',
        title: 'My Profile | KhataDhari ERP',
        loadComponent: () =>
          import('./features/account/user-profile.component').then((m) => m.UserProfileComponent),
      },
      {
        path: 'preferences',
        title: 'My Preferences | KhataDhari ERP',
        loadComponent: () =>
          import('./features/account/user-preferences.component').then(
            (m) => m.UserPreferencesComponent,
          ),
      },
      {
        path: 'dashboard',
        canActivate: [permissionGuard],
        data: dashboardView,
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'analytics/sales',
        canActivate: [permissionGuard],
        data: { ...analyticsView, mode: 'sales', title: 'Sales Analytics' },
        loadComponent: () =>
          import('./features/dashboard/analytics-page.component').then(
            (m) => m.AnalyticsPageComponent,
          ),
      },
      {
        path: 'analytics/purchase',
        canActivate: [permissionGuard],
        data: { ...analyticsView, mode: 'purchase', title: 'Purchase Analytics' },
        loadComponent: () =>
          import('./features/dashboard/analytics-page.component').then(
            (m) => m.AnalyticsPageComponent,
          ),
      },
      {
        path: 'analytics/inventory',
        canActivate: [permissionGuard],
        data: { ...analyticsView, mode: 'inventory', title: 'Inventory Analytics' },
        loadComponent: () =>
          import('./features/dashboard/analytics-page.component').then(
            (m) => m.AnalyticsPageComponent,
          ),
      },
      {
        path: 'analytics/finance',
        canActivate: [permissionGuard],
        data: { ...analyticsView, mode: 'finance', title: 'Finance Analytics' },
        loadComponent: () =>
          import('./features/dashboard/analytics-page.component').then(
            (m) => m.AnalyticsPageComponent,
          ),
      },
      {
        path: 'reports',
        canActivate: [permissionGuard],
        data: gstView,
        loadComponent: () =>
          import('./features/reports/reports-center.component').then(
            (m) => m.ReportsCenterComponent,
          ),
      },
      {
        path: 'gst',
        canActivate: [permissionGuard],
        data: gstView,
        loadComponent: () =>
          import('./features/gst/gst-dashboard.component').then((m) => m.GstDashboardComponent),
      },
      {
        path: 'gst/sales-register',
        canActivate: [permissionGuard],
        data: { ...gstView, report: 'sales-register', title: 'GST Sales Register' },
        loadComponent: () =>
          import('./features/gst/gst-report.component').then((m) => m.GstReportComponent),
      },
      {
        path: 'gst/purchase-register',
        canActivate: [permissionGuard],
        data: { ...gstView, report: 'purchase-register', title: 'GST Purchase Register' },
        loadComponent: () =>
          import('./features/gst/gst-report.component').then((m) => m.GstReportComponent),
      },
      {
        path: 'gst/hsn-summary',
        canActivate: [permissionGuard],
        data: { ...gstView, report: 'hsn-summary', title: 'HSN Summary' },
        loadComponent: () =>
          import('./features/gst/gst-report.component').then((m) => m.GstReportComponent),
      },
      {
        path: 'gst/gstr1',
        canActivate: [permissionGuard],
        data: { ...gstView, report: 'gstr1', title: 'GSTR-1 Summary' },
        loadComponent: () =>
          import('./features/gst/gst-report.component').then((m) => m.GstReportComponent),
      },
      {
        path: 'gst/gstr3b',
        canActivate: [permissionGuard],
        data: { ...gstView, report: 'gstr3b', title: 'GSTR-3B Summary' },
        loadComponent: () =>
          import('./features/gst/gst-report.component').then((m) => m.GstReportComponent),
      },
      {
        path: 'gst/tax-summary',
        canActivate: [permissionGuard],
        data: { ...gstView, report: 'tax-summary', title: 'GST Tax Summary' },
        loadComponent: () =>
          import('./features/gst/gst-report.component').then((m) => m.GstReportComponent),
      },
      {
        path: 'gst/configuration',
        canActivate: [permissionGuard],
        data: gstConfiguration,
        loadComponent: () =>
          import('./features/gst/gst-configuration.component').then(
            (m) => m.GstConfigurationComponent,
          ),
      },
      {
        path: 'print/preview',
        canActivate: [permissionGuard],
        data: printDocument,
        loadComponent: () =>
          import('./features/printing/print-preview.component').then(
            (m) => m.PrintPreviewComponent,
          ),
      },
      {
        path: 'print/labels',
        canActivate: [permissionGuard],
        data: printDocument,
        loadComponent: () =>
          import('./features/printing/label-designer.component').then(
            (m) => m.LabelDesignerComponent,
          ),
      },
      {
        path: 'print/barcodes',
        canActivate: [permissionGuard],
        data: printBarcode,
        loadComponent: () =>
          import('./features/printing/barcode-generator.component').then(
            (m) => m.BarcodeGeneratorComponent,
          ),
      },
      {
        path: 'print/printers',
        canActivate: [permissionGuard],
        data: printSettings,
        loadComponent: () =>
          import('./features/printing/printer-configuration.component').then(
            (m) => m.PrinterConfigurationComponent,
          ),
      },
      {
        path: 'print/templates',
        canActivate: [permissionGuard],
        data: printView,
        loadComponent: () =>
          import('./features/printing/template-manager.component').then(
            (m) => m.TemplateManagerComponent,
          ),
      },
      {
        path: 'admin',
        canActivate: [permissionGuard],
        data: adminView,
        loadComponent: () =>
          import('./features/administration/administration-hub.component').then(
            (m) => m.AdministrationHubComponent,
          ),
      },
      { path: 'printing/templates', redirectTo: 'print/templates', pathMatch: 'full' },
      { path: 'printing/labels', redirectTo: 'print/labels', pathMatch: 'full' },
      {
        path: 'admin/users',
        canActivate: [permissionGuard],
        data: { permission: 'user.manage', mode: 'users' },
        loadComponent: () =>
          import('./features/administration/identity-administration.component').then(
            (m) => m.IdentityAdministrationComponent,
          ),
      },
      {
        path: 'admin/roles',
        canActivate: [permissionGuard],
        data: { permission: 'role.manage', mode: 'roles' },
        loadComponent: () =>
          import('./features/administration/identity-administration.component').then(
            (m) => m.IdentityAdministrationComponent,
          ),
      },
      {
        path: 'admin/company',
        canActivate: [permissionGuard],
        data: adminCompany,
        loadComponent: () =>
          import('./features/administration/company-profile.component').then(
            (m) => m.CompanyProfileComponent,
          ),
      },
      {
        path: 'admin/branches',
        canActivate: [permissionGuard],
        data: adminSettings,
        loadComponent: () =>
          import('./features/administration/branch-management.component').then(
            (m) => m.BranchManagementComponent,
          ),
      },
      {
        path: 'admin/financial-years',
        canActivate: [permissionGuard],
        data: adminSettings,
        loadComponent: () =>
          import('./features/administration/financial-year.component').then(
            (m) => m.FinancialYearComponent,
          ),
      },
      {
        path: 'admin/settings',
        canActivate: [permissionGuard],
        data: { ...adminSettings, title: 'Application Settings' },
        loadComponent: () =>
          import('./features/administration/application-settings.component').then(
            (m) => m.ApplicationSettingsComponent,
          ),
      },
      {
        path: 'admin/customer-notifications',
        canActivate: [permissionGuard],
        data: { ...adminSettings, title: 'Customer Notifications' },
        loadComponent: () =>
          import('./features/administration/customer-notifications.component').then(
            (m) => m.CustomerNotificationsComponent,
          ),
      },
      {
        path: 'admin/whatsapp',
        canActivate: [permissionGuard, featureGuard],
        data: { ...adminSettings, feature: 'WHATSAPP_COMMERCE', title: 'WhatsApp Business Connection' },
        loadComponent: () =>
          import('./features/whatsapp/whatsapp-configuration.component').then(
            (m) => m.WhatsAppConfigurationComponent,
          ),
      },
      {
        path: 'admin/whatsapp-demo',
        canActivate: [permissionGuard, featureGuard],
        data: { permission: 'pos.view', feature: 'WHATSAPP_COMMERCE', title: 'WhatsApp Commerce Demo' },
        loadComponent: () =>
          import('./features/whatsapp/whatsapp-commerce-demo.component').then(
            (m) => m.WhatsAppCommerceDemoComponent,
          ),
      },
      {
        path: 'admin/printers',
        canActivate: [permissionGuard],
        data: { ...printSettings, title: 'Printer Configuration' },
        loadComponent: () =>
          import('./features/printing/printer-configuration.component').then(
            (m) => m.PrinterConfigurationComponent,
          ),
      },
      {
        path: 'admin/backup',
        canActivate: [permissionGuard],
        data: adminBackup,
        loadComponent: () =>
          import('./features/administration/backup-restore.component').then(
            (m) => m.BackupRestoreComponent,
          ),
      },
      {
        path: 'admin/restore',
        canActivate: [permissionGuard],
        data: { ...adminRestore, restore: true },
        loadComponent: () =>
          import('./features/administration/backup-restore.component').then(
            (m) => m.BackupRestoreComponent,
          ),
      },
      {
        path: 'admin/audit',
        canActivate: [permissionGuard],
        data: adminAudit,
        loadComponent: () =>
          import('./features/administration/audit-history.component').then(
            (m) => m.AuditHistoryComponent,
          ),
      },
      {
        path: 'admin/login-history',
        canActivate: [permissionGuard],
        data: { ...adminAudit, login: true },
        loadComponent: () =>
          import('./features/administration/audit-history.component').then(
            (m) => m.AuditHistoryComponent,
          ),
      },
      {
        path: 'admin/preferences',
        canActivate: [permissionGuard],
        data: { ...adminView, title: 'User Preferences' },
        loadComponent: () =>
          import('./features/administration/application-settings.component').then(
            (m) => m.ApplicationSettingsComponent,
          ),
      },
      {
        path: 'products',
        canActivate: [permissionGuard],
        data: productView,
        loadComponent: () =>
          import('./features/products/product-list.component').then((m) => m.ProductListComponent),
      },
      {
        path: 'products/new',
        canActivate: [permissionGuard],
        data: productCreate,
        loadComponent: () =>
          import('./features/products/product-form.component').then((m) => m.ProductFormComponent),
      },
      {
        path: 'products/:id/edit',
        canActivate: [permissionGuard],
        data: productEdit,
        loadComponent: () =>
          import('./features/products/product-form.component').then((m) => m.ProductFormComponent),
      },
      {
        path: 'products/:id',
        canActivate: [permissionGuard],
        data: productView,
        loadComponent: () =>
          import('./features/products/product-view.component').then((m) => m.ProductViewComponent),
      },
      {
        path: 'product-categories',
        canActivate: [permissionGuard],
        data: productView,
        loadComponent: () =>
          import('./features/products/category-management.component').then(
            (m) => m.CategoryManagementComponent,
          ),
      },
      {
        path: 'brands',
        canActivate: [permissionGuard],
        data: productView,
        loadComponent: () =>
          import('./features/products/brand-management.component').then(
            (m) => m.BrandManagementComponent,
          ),
      },
      {
        path: 'units',
        canActivate: [permissionGuard],
        data: productView,
        loadComponent: () =>
          import('./features/products/unit-management.component').then(
            (m) => m.UnitManagementComponent,
          ),
      },
      {
        path: 'suppliers',
        canActivate: [permissionGuard],
        data: supplierView,
        loadComponent: () =>
          import('./features/suppliers/supplier-list.component').then(
            (m) => m.SupplierListComponent,
          ),
      },
      {
        path: 'suppliers/new',
        canActivate: [permissionGuard],
        data: supplierCreate,
        loadComponent: () =>
          import('./features/suppliers/supplier-form.component').then(
            (m) => m.SupplierFormComponent,
          ),
      },
      {
        path: 'suppliers/:id/edit',
        canActivate: [permissionGuard],
        data: supplierEdit,
        loadComponent: () =>
          import('./features/suppliers/supplier-form.component').then(
            (m) => m.SupplierFormComponent,
          ),
      },
      {
        path: 'suppliers/:id',
        canActivate: [permissionGuard],
        data: supplierView,
        loadComponent: () =>
          import('./features/suppliers/supplier-view.component').then(
            (m) => m.SupplierViewComponent,
          ),
      },
      {
        path: 'customers',
        canActivate: [permissionGuard],
        data: customerView,
        loadComponent: () =>
          import('./features/customers/customer-list.component').then(
            (m) => m.CustomerListComponent,
          ),
      },
      {
        path: 'customers/new',
        canActivate: [permissionGuard],
        data: customerCreate,
        loadComponent: () =>
          import('./features/customers/customer-form.component').then(
            (m) => m.CustomerFormComponent,
          ),
      },
      {
        path: 'customers/:id/edit',
        canActivate: [permissionGuard],
        data: customerEdit,
        loadComponent: () =>
          import('./features/customers/customer-form.component').then(
            (m) => m.CustomerFormComponent,
          ),
      },
      {
        path: 'customers/:id',
        canActivate: [permissionGuard],
        data: customerView,
        loadComponent: () =>
          import('./features/customers/customer-view.component').then(
            (m) => m.CustomerViewComponent,
          ),
      },
      {
        path: 'warehouses',
        canActivate: [permissionGuard],
        data: warehouseView,
        loadComponent: () =>
          import('./features/warehouses/warehouse-list.component').then(
            (m) => m.WarehouseListComponent,
          ),
      },
      {
        path: 'warehouses/new',
        canActivate: [permissionGuard],
        data: warehouseCreate,
        loadComponent: () =>
          import('./features/warehouses/warehouse-form.component').then(
            (m) => m.WarehouseFormComponent,
          ),
      },
      {
        path: 'warehouses/:id/edit',
        canActivate: [permissionGuard],
        data: warehouseEdit,
        loadComponent: () =>
          import('./features/warehouses/warehouse-form.component').then(
            (m) => m.WarehouseFormComponent,
          ),
      },
      {
        path: 'warehouses/:id',
        canActivate: [permissionGuard],
        data: warehouseView,
        loadComponent: () =>
          import('./features/warehouses/warehouse-view.component').then(
            (m) => m.WarehouseViewComponent,
          ),
      },
      {
        path: 'warehouse-types',
        canActivate: [permissionGuard],
        data: warehouseView,
        loadComponent: () =>
          import('./features/warehouses/warehouse-type-management.component').then(
            (m) => m.WarehouseTypeManagementComponent,
          ),
      },
      {
        path: 'inventory',
        canActivate: [permissionGuard],
        data: inventoryView,
        loadComponent: () =>
          import('./features/inventory/inventory-dashboard.component').then(
            (m) => m.InventoryDashboardComponent,
          ),
      },
      {
        path: 'inventory/balance',
        canActivate: [permissionGuard],
        data: inventoryView,
        loadComponent: () =>
          import('./features/inventory/stock-balance.component').then(
            (m) => m.StockBalanceComponent,
          ),
      },
      {
        path: 'inventory/transactions',
        canActivate: [permissionGuard],
        data: inventoryView,
        loadComponent: () =>
          import('./features/inventory/inventory-transactions.component').then(
            (m) => m.InventoryTransactionsComponent,
          ),
      },
      {
        path: 'inventory/adjustment',
        canActivate: [permissionGuard],
        data: inventoryAdjust,
        loadComponent: () =>
          import('./features/inventory/stock-adjustment.component').then(
            (m) => m.StockAdjustmentComponent,
          ),
      },
      {
        path: 'inventory/transfer',
        canActivate: [permissionGuard],
        data: inventoryTransfer,
        loadComponent: () =>
          import('./features/inventory/stock-transfer.component').then(
            (m) => m.StockTransferComponent,
          ),
      },
      {
        path: 'inventory/reservation',
        canActivate: [permissionGuard],
        data: inventoryReserve,
        loadComponent: () =>
          import('./features/inventory/stock-reservation.component').then(
            (m) => m.StockReservationComponent,
          ),
      },
      {
        path: 'inventory/verification',
        canActivate: [permissionGuard],
        data: inventoryVerify,
        loadComponent: () =>
          import('./features/inventory/physical-stock-verification.component').then(
            (m) => m.PhysicalStockVerificationComponent,
          ),
      },
      {
        path: 'inventory/reorder',
        canActivate: [permissionGuard],
        data: { ...inventoryReorder, mode: 'reorder', title: 'Reorder Suggestions' },
        loadComponent: () =>
          import('./features/inventory/stock-control-list.component').then(
            (m) => m.StockControlListComponent,
          ),
      },
      {
        path: 'inventory/alerts',
        canActivate: [permissionGuard],
        data: { ...inventoryAlerts, mode: 'alerts', title: 'Inventory Alerts' },
        loadComponent: () =>
          import('./features/inventory/stock-control-list.component').then(
            (m) => m.StockControlListComponent,
          ),
      },
      {
        path: 'inventory/movement-history',
        canActivate: [permissionGuard],
        data: { ...inventoryView, mode: 'movement', title: 'Stock Movement History' },
        loadComponent: () =>
          import('./features/inventory/stock-control-list.component').then(
            (m) => m.StockControlListComponent,
          ),
      },
      {
        path: 'pos',
        canActivate: [permissionGuard],
        data: posCreate,
        loadComponent: () =>
          import('./features/pos/pos-screen.component').then((m) => m.POSScreenComponent),
      },
      {
        path: 'pos/today',
        canActivate: [permissionGuard],
        data: posView,
        loadComponent: () =>
          import('./features/pos/today-sales.component').then((m) => m.TodaySalesComponent),
      },
      {
        path: 'pos/holds',
        canActivate: [permissionGuard],
        data: posEdit,
        loadComponent: () =>
          import('./features/pos/hold-bills.component').then((m) => m.HoldBillsComponent),
      },
      {
        path: 'pos/resume',
        canActivate: [permissionGuard],
        data: posEdit,
        loadComponent: () =>
          import('./features/pos/hold-bills.component').then((m) => m.HoldBillsComponent),
      },
      {
        path: 'pos/history',
        canActivate: [permissionGuard],
        data: posView,
        loadComponent: () =>
          import('./features/pos/invoice-history.component').then((m) => m.InvoiceHistoryComponent),
      },
      {
        path: 'pos/returns',
        canActivate: [permissionGuard],
        data: posReturn,
        loadComponent: () =>
          import('./features/pos/return-screen.component').then((m) => m.ReturnScreenComponent),
      },
      {
        path: 'purchases/dashboard',
        canActivate: [permissionGuard],
        data: purchaseView,
        loadComponent: () =>
          import('./features/purchases/purchase-dashboard.component').then(
            (m) => m.PurchaseDashboardComponent,
          ),
      },
      {
        path: 'purchases',
        canActivate: [permissionGuard],
        data: purchaseView,
        loadComponent: () =>
          import('./features/purchases/purchase-list.component').then(
            (m) => m.PurchaseListComponent,
          ),
      },
      {
        path: 'purchases/create',
        canActivate: [permissionGuard],
        data: purchaseCreate,
        loadComponent: () =>
          import('./features/purchases/purchase-form.component').then(
            (m) => m.PurchaseFormComponent,
          ),
      },
      {
        path: 'purchases/:id/edit',
        canActivate: [permissionGuard],
        data: purchaseEdit,
        loadComponent: () =>
          import('./features/purchases/purchase-form.component').then(
            (m) => m.PurchaseFormComponent,
          ),
      },
      {
        path: 'purchases/:id/payment',
        canActivate: [permissionGuard],
        data: purchasePayment,
        loadComponent: () =>
          import('./features/purchases/purchase-payment.component').then(
            (m) => m.PurchasePaymentComponent,
          ),
      },
      {
        path: 'purchases/:id/return',
        canActivate: [permissionGuard],
        data: purchaseReturn,
        loadComponent: () =>
          import('./features/purchases/purchase-return.component').then(
            (m) => m.PurchaseReturnComponent,
          ),
      },
      {
        path: 'purchases/:id',
        canActivate: [permissionGuard],
        data: purchaseView,
        loadComponent: () =>
          import('./features/purchases/purchase-details.component').then(
            (m) => m.PurchaseDetailsComponent,
          ),
      },
      {
        path: 'finance/customer-ledger',
        canActivate: [permissionGuard],
        data: { ...ledgerView, party: 'customer' },
        loadComponent: () =>
          import('./features/finance/party-ledger.component').then((m) => m.PartyLedgerComponent),
      },
      {
        path: 'finance/supplier-ledger',
        canActivate: [permissionGuard],
        data: { ...ledgerView, party: 'supplier' },
        loadComponent: () =>
          import('./features/finance/party-ledger.component').then((m) => m.PartyLedgerComponent),
      },
      {
        path: 'finance/cashbook',
        canActivate: [permissionGuard],
        data: { ...cashbookView, kind: 'cash' },
        loadComponent: () =>
          import('./features/finance/book.component').then((m) => m.FinanceBookComponent),
      },
      {
        path: 'finance/bankbook',
        canActivate: [permissionGuard],
        data: { ...bankbookView, kind: 'bank' },
        loadComponent: () =>
          import('./features/finance/book.component').then((m) => m.FinanceBookComponent),
      },
      {
        path: 'finance/daybook',
        canActivate: [permissionGuard],
        data: { ...ledgerView, kind: 'day' },
        loadComponent: () =>
          import('./features/finance/book.component').then((m) => m.FinanceBookComponent),
      },
      {
        path: 'finance/receipt',
        canActivate: [permissionGuard],
        data: { ...receiptCreate, kind: 'receipt' },
        loadComponent: () =>
          import('./features/receivables/receivable-payable-entry.component').then(
            (m) => m.ReceivablePayableEntryComponent,
          ),
      },
      {
        path: 'finance/payment',
        canActivate: [permissionGuard],
        data: { ...paymentCreate, kind: 'payment' },
        loadComponent: () =>
          import('./features/receivables/receivable-payable-entry.component').then(
            (m) => m.ReceivablePayableEntryComponent,
          ),
      },
      {
        path: 'finance/customer-outstanding',
        canActivate: [permissionGuard],
        data: { ...customerOutstanding, party: 'customer' },
        loadComponent: () =>
          import('./features/receivables/outstanding-ageing.component').then(
            (m) => m.OutstandingAgeingComponent,
          ),
      },
      {
        path: 'finance/supplier-outstanding',
        canActivate: [permissionGuard],
        data: { ...supplierOutstanding, party: 'supplier' },
        loadComponent: () =>
          import('./features/receivables/outstanding-ageing.component').then(
            (m) => m.OutstandingAgeingComponent,
          ),
      },
      {
        path: 'finance/customer-ageing',
        canActivate: [permissionGuard],
        data: { ...customerOutstanding, party: 'customer', ageing: true },
        loadComponent: () =>
          import('./features/receivables/outstanding-ageing.component').then(
            (m) => m.OutstandingAgeingComponent,
          ),
      },
      {
        path: 'finance/supplier-ageing',
        canActivate: [permissionGuard],
        data: { ...supplierOutstanding, party: 'supplier', ageing: true },
        loadComponent: () =>
          import('./features/receivables/outstanding-ageing.component').then(
            (m) => m.OutstandingAgeingComponent,
          ),
      },
      {
        path: 'finance/collection-followup',
        canActivate: [permissionGuard],
        data: customerOutstanding,
        loadComponent: () =>
          import('./features/receivables/collection-followup.component').then(
            (m) => m.CollectionFollowUpComponent,
          ),
      },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
  {
    path: '**',
    data: { mode: '404' },
    loadComponent: () =>
      import('./features/system-state/system-state.component').then((m) => m.SystemStateComponent),
  },
];
