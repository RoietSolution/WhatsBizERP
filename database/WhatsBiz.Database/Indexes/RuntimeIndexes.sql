-- Persistent indexes promoted from the RCDEV008 runtime snapshot into the
-- canonical SQL project model. Keep RCDEV008's compatibility block guarded.
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_UserId] ON [core].[RefreshTokens]([UserId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_RoleClaims_RoleId] ON [core].[RoleClaims]([RoleId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_UserClaims_UserId] ON [core].[UserClaims]([UserId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_UserLogins_UserId] ON [core].[UserLogins]([UserId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_UserRoles_RoleId] ON [core].[UserRoles]([RoleId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_InventoryBalances_BinId] ON [inventory].[InventoryBalances]([BinId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_InventoryBalances_ZoneId] ON [inventory].[InventoryBalances]([ZoneId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_InventoryTransactionDetails_TransactionId] ON [inventory].[InventoryTransactionDetails]([TransactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_InventoryTransactions_WarehouseId] ON [inventory].[InventoryTransactions]([WarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_InventoryValuation_WarehouseId] ON [inventory].[InventoryValuation]([WarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockAdjustments_TransactionId] ON [inventory].[StockAdjustments]([TransactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockReservations_BinId] ON [inventory].[StockReservations]([BinId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockReservations_ProductId] ON [inventory].[StockReservations]([ProductId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockReservations_WarehouseId] ON [inventory].[StockReservations]([WarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockReservations_ZoneId] ON [inventory].[StockReservations]([ZoneId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockTransfers_DestinationTransactionId] ON [inventory].[StockTransfers]([DestinationTransactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockTransfers_DestinationWarehouseId] ON [inventory].[StockTransfers]([DestinationWarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockTransfers_SourceTransactionId] ON [inventory].[StockTransfers]([SourceTransactionId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_StockTransfers_SourceWarehouseId] ON [inventory].[StockTransfers]([SourceWarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Warehouses_AddressId] ON [inventory].[Warehouses]([AddressId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Warehouses_WarehouseTypeId] ON [inventory].[Warehouses]([WarehouseTypeId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_ProductBarcodes_ProductId] ON [master].[ProductBarcodes]([ProductId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_ProductCategories_ParentCategoryId] ON [master].[ProductCategories]([ParentCategoryId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_ProductImages_ProductId] ON [master].[ProductImages]([ProductId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_ProductPrices_ProductId] ON [master].[ProductPrices]([ProductId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Products_BrandId] ON [master].[Products]([BrandId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Products_CategoryId] ON [master].[Products]([CategoryId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Products_UnitId] ON [master].[Products]([UnitId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_ProductTaxMappings_ProductId] ON [master].[ProductTaxMappings]([ProductId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_PurchaseAttachments_PurchaseInvoiceId] ON [purchase].[PurchaseAttachments]([PurchaseInvoiceId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_PurchaseExpenses_PurchaseInvoiceId] ON [purchase].[PurchaseExpenses]([PurchaseInvoiceId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_PurchaseItems_Dashboard] ON [purchase].[PurchaseInvoiceItems]([PurchaseInvoiceId] ASC, [ProductId] ASC) INCLUDE([Quantity], [FreeQuantity], [ReturnedQuantity], [LineTotal], [PurchasePrice], [SellingPrice]);
GO
CREATE NONCLUSTERED INDEX [IX_PurchaseInvoices_DashboardDate] ON [purchase].[PurchaseInvoices]([InvoiceDate] ASC, [Status] ASC, [IsDeleted] ASC) INCLUDE([PurchaseInvoiceId], [SupplierId], [GrandTotal], [PaidAmount], [TaxAmount], [ExpenseAmount]);
GO
CREATE NONCLUSTERED INDEX [IX_PurchaseInvoices_WarehouseId] ON [purchase].[PurchaseInvoices]([WarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_PurchasePayments_PaymentMethodId] ON [purchase].[PurchasePayments]([PaymentMethodId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_PurchaseReturns_PurchaseItemId] ON [purchase].[PurchaseReturns]([PurchaseItemId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Suppliers_PaymentTermId] ON [purchase].[Suppliers]([PaymentTermId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_CashDrawer_ShiftId] ON [sales].[CashDrawer]([ShiftId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_Customers_PaymentTermId] ON [sales].[Customers]([PaymentTermId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_POSCounters_InvoiceSeriesId] ON [sales].[POSCounters]([InvoiceSeriesId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_POSCounters_WarehouseId] ON [sales].[POSCounters]([WarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_POSShift_CounterId] ON [sales].[POSShift]([CounterId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesDiscounts_InvoiceId] ON [sales].[SalesDiscounts]([InvoiceId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesDiscounts_InvoiceItemId] ON [sales].[SalesDiscounts]([InvoiceItemId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceItems_Dashboard] ON [sales].[SalesInvoiceItems]([InvoiceId] ASC, [ProductId] ASC) INCLUDE([Quantity], [ReturnedQuantity], [LineTotal], [UnitPrice], [DiscountAmount], [TaxAmount]);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceItems_ProductId] ON [sales].[SalesInvoiceItems]([ProductId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceReturns_InvoiceId] ON [sales].[SalesInvoiceReturns]([InvoiceId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoiceReturns_InvoiceItemId] ON [sales].[SalesInvoiceReturns]([InvoiceItemId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoices_CounterId] ON [sales].[SalesInvoices]([CounterId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoices_CustomerId] ON [sales].[SalesInvoices]([CustomerId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoices_DashboardDate] ON [sales].[SalesInvoices]([InvoiceDate] ASC, [Status] ASC) INCLUDE([InvoiceId], [CustomerId], [GrandTotal], [PaidAmount], [TaxAmount], [DiscountAmount]);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoices_OriginalInvoiceId] ON [sales].[SalesInvoices]([OriginalInvoiceId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoices_ShiftId] ON [sales].[SalesInvoices]([ShiftId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesInvoices_WarehouseId] ON [sales].[SalesInvoices]([WarehouseId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesPayments_DashboardDate] ON [sales].[SalesPayments]([PaymentDate] ASC, [Status] ASC, [PaymentMethodId] ASC) INCLUDE([InvoiceId], [Amount]);
GO
CREATE NONCLUSTERED INDEX [IX_SalesPayments_InvoiceId] ON [sales].[SalesPayments]([InvoiceId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesPayments_PaymentMethodId] ON [sales].[SalesPayments]([PaymentMethodId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesTaxes_InvoiceId] ON [sales].[SalesTaxes]([InvoiceId] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_SalesTaxes_InvoiceItemId] ON [sales].[SalesTaxes]([InvoiceItemId] ASC);
