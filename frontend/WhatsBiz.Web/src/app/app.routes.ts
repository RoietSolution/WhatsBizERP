import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { permissionGuard } from './core/guards/permission.guard';
const productView={permission:'product.view'},productCreate={permission:'product.create'},productEdit={permission:'product.edit'};
const supplierView={permission:'supplier.view'},supplierCreate={permission:'supplier.create'},supplierEdit={permission:'supplier.edit'};
const customerView={permission:'customer.view'},customerCreate={permission:'customer.create'},customerEdit={permission:'customer.edit'};
const warehouseView={permission:'warehouse.view'},warehouseCreate={permission:'warehouse.create'},warehouseEdit={permission:'warehouse.edit'};
const inventoryView={permission:'inventory.view'},inventoryAdjust={permission:'inventory.adjust'},inventoryTransfer={permission:'inventory.transfer'},inventoryReserve={permission:'inventory.reserve'};
const posView={permission:'pos.view'},posCreate={permission:'pos.create'},posEdit={permission:'pos.edit'},posReturn={permission:'pos.return'};
export const routes:Routes=[
 {path:'login',loadComponent:()=>import('./features/authentication/login/login.component').then(m=>m.LoginComponent)},
 {path:'403',loadComponent:()=>import('./features/forbidden/forbidden.component').then(m=>m.ForbiddenComponent)},
 {path:'unauthorized',loadComponent:()=>import('./features/unauthorized/unauthorized.component').then(m=>m.UnauthorizedComponent)},
 {path:'404',loadComponent:()=>import('./features/not-found/not-found.component').then(m=>m.NotFoundComponent)},
 {path:'',canActivate:[authGuard],loadComponent:()=>import('./layout/main-layout/main-layout.component').then(m=>m.MainLayoutComponent),children:[
  {path:'dashboard',loadComponent:()=>import('./features/dashboard/dashboard.component').then(m=>m.DashboardComponent)},
  {path:'products',canActivate:[permissionGuard],data:productView,loadComponent:()=>import('./features/products/product-list.component').then(m=>m.ProductListComponent)},
  {path:'products/new',canActivate:[permissionGuard],data:productCreate,loadComponent:()=>import('./features/products/product-form.component').then(m=>m.ProductFormComponent)},
  {path:'products/:id/edit',canActivate:[permissionGuard],data:productEdit,loadComponent:()=>import('./features/products/product-form.component').then(m=>m.ProductFormComponent)},
  {path:'products/:id',canActivate:[permissionGuard],data:productView,loadComponent:()=>import('./features/products/product-view.component').then(m=>m.ProductViewComponent)},
  {path:'product-categories',canActivate:[permissionGuard],data:productView,loadComponent:()=>import('./features/products/category-management.component').then(m=>m.CategoryManagementComponent)},
  {path:'brands',canActivate:[permissionGuard],data:productView,loadComponent:()=>import('./features/products/brand-management.component').then(m=>m.BrandManagementComponent)},
  {path:'units',canActivate:[permissionGuard],data:productView,loadComponent:()=>import('./features/products/unit-management.component').then(m=>m.UnitManagementComponent)},
  {path:'suppliers',canActivate:[permissionGuard],data:supplierView,loadComponent:()=>import('./features/suppliers/supplier-list.component').then(m=>m.SupplierListComponent)},
  {path:'suppliers/new',canActivate:[permissionGuard],data:supplierCreate,loadComponent:()=>import('./features/suppliers/supplier-form.component').then(m=>m.SupplierFormComponent)},
  {path:'suppliers/:id/edit',canActivate:[permissionGuard],data:supplierEdit,loadComponent:()=>import('./features/suppliers/supplier-form.component').then(m=>m.SupplierFormComponent)},
  {path:'suppliers/:id',canActivate:[permissionGuard],data:supplierView,loadComponent:()=>import('./features/suppliers/supplier-view.component').then(m=>m.SupplierViewComponent)},
  {path:'customers',canActivate:[permissionGuard],data:customerView,loadComponent:()=>import('./features/customers/customer-list.component').then(m=>m.CustomerListComponent)},
  {path:'customers/new',canActivate:[permissionGuard],data:customerCreate,loadComponent:()=>import('./features/customers/customer-form.component').then(m=>m.CustomerFormComponent)},
  {path:'customers/:id/edit',canActivate:[permissionGuard],data:customerEdit,loadComponent:()=>import('./features/customers/customer-form.component').then(m=>m.CustomerFormComponent)},
  {path:'customers/:id',canActivate:[permissionGuard],data:customerView,loadComponent:()=>import('./features/customers/customer-view.component').then(m=>m.CustomerViewComponent)},
  {path:'warehouses',canActivate:[permissionGuard],data:warehouseView,loadComponent:()=>import('./features/warehouses/warehouse-list.component').then(m=>m.WarehouseListComponent)},
  {path:'warehouses/new',canActivate:[permissionGuard],data:warehouseCreate,loadComponent:()=>import('./features/warehouses/warehouse-form.component').then(m=>m.WarehouseFormComponent)},
  {path:'warehouses/:id/edit',canActivate:[permissionGuard],data:warehouseEdit,loadComponent:()=>import('./features/warehouses/warehouse-form.component').then(m=>m.WarehouseFormComponent)},
  {path:'warehouses/:id',canActivate:[permissionGuard],data:warehouseView,loadComponent:()=>import('./features/warehouses/warehouse-view.component').then(m=>m.WarehouseViewComponent)},
  {path:'warehouse-types',canActivate:[permissionGuard],data:warehouseView,loadComponent:()=>import('./features/warehouses/warehouse-type-management.component').then(m=>m.WarehouseTypeManagementComponent)},
  {path:'inventory',canActivate:[permissionGuard],data:inventoryView,loadComponent:()=>import('./features/inventory/inventory-dashboard.component').then(m=>m.InventoryDashboardComponent)},
  {path:'inventory/balance',canActivate:[permissionGuard],data:inventoryView,loadComponent:()=>import('./features/inventory/stock-balance.component').then(m=>m.StockBalanceComponent)},
  {path:'inventory/transactions',canActivate:[permissionGuard],data:inventoryView,loadComponent:()=>import('./features/inventory/inventory-transactions.component').then(m=>m.InventoryTransactionsComponent)},
  {path:'inventory/adjustment',canActivate:[permissionGuard],data:inventoryAdjust,loadComponent:()=>import('./features/inventory/stock-adjustment.component').then(m=>m.StockAdjustmentComponent)},
  {path:'inventory/transfer',canActivate:[permissionGuard],data:inventoryTransfer,loadComponent:()=>import('./features/inventory/stock-transfer.component').then(m=>m.StockTransferComponent)},
  {path:'inventory/reservation',canActivate:[permissionGuard],data:inventoryReserve,loadComponent:()=>import('./features/inventory/stock-reservation.component').then(m=>m.StockReservationComponent)},
  {path:'pos',canActivate:[permissionGuard],data:posCreate,loadComponent:()=>import('./features/pos/pos-screen.component').then(m=>m.POSScreenComponent)},
  {path:'pos/today',canActivate:[permissionGuard],data:posView,loadComponent:()=>import('./features/pos/today-sales.component').then(m=>m.TodaySalesComponent)},
  {path:'pos/holds',canActivate:[permissionGuard],data:posEdit,loadComponent:()=>import('./features/pos/hold-bills.component').then(m=>m.HoldBillsComponent)},
  {path:'pos/resume',canActivate:[permissionGuard],data:posEdit,loadComponent:()=>import('./features/pos/hold-bills.component').then(m=>m.HoldBillsComponent)},
  {path:'pos/history',canActivate:[permissionGuard],data:posView,loadComponent:()=>import('./features/pos/invoice-history.component').then(m=>m.InvoiceHistoryComponent)},
  {path:'pos/returns',canActivate:[permissionGuard],data:posReturn,loadComponent:()=>import('./features/pos/return-screen.component').then(m=>m.ReturnScreenComponent)},
  {path:'',pathMatch:'full',redirectTo:'dashboard'}]},
 {path:'**',loadComponent:()=>import('./features/not-found/not-found.component').then(m=>m.NotFoundComponent)}];
