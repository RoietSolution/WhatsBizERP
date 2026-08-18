/* Replace generic inventory/dashboard notification text with actionable business details. */
ALTER PROCEDURE inventory.RefreshStockControl AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRAN;

    UPDATE inventory.InventoryAlerts
    SET Status = 'RESOLVED', ResolvedOn = SYSUTCDATETIME()
    WHERE Status = 'ACTIVE';

    INSERT inventory.InventoryAlerts(ProductId, WarehouseId, AlertType, CurrentQuantity, SuggestedQuantity, Detail)
    SELECT p.ProductId, w.WarehouseId,
        CASE WHEN ISNULL(s.Qty, 0) < 0 THEN 'NEGATIVE_STOCK'
             WHEN ISNULL(s.Qty, 0) = 0 THEN 'OUT_OF_STOCK'
             WHEN ISNULL(s.Qty, 0) <= p.ReorderLevel THEN 'LOW_STOCK'
             WHEN p.MaximumStock > 0 AND ISNULL(s.Qty, 0) > p.MaximumStock THEN 'OVER_STOCK' END,
        ISNULL(s.Qty, 0),
        CASE WHEN p.MaximumStock > ISNULL(s.Qty, 0) THEN p.MaximumStock - ISNULL(s.Qty, 0) ELSE 0 END,
        CONCAT('Product: ', p.ProductName, ' | Product code: ', p.ProductCode,
               ' | Current stock: ', FORMAT(ISNULL(s.Qty, 0), '0.##'),
               ' | Reorder level: ', FORMAT(p.ReorderLevel, '0.##'),
               ' | Warehouse: ', w.WarehouseName,
               ' | Action: replenish stock before the available quantity reaches zero.')
    FROM master.Products p
    CROSS JOIN inventory.Warehouses w
    OUTER APPLY (SELECT SUM(QuantityAvailable) Qty FROM inventory.InventoryBalances b WHERE b.ProductId = p.ProductId AND b.WarehouseId = w.WarehouseId) s
    WHERE p.IsActive = 1 AND p.IsDeleted = 0 AND w.IsActive = 1
      AND (ISNULL(s.Qty, 0) <= p.ReorderLevel OR p.MaximumStock > 0 AND ISNULL(s.Qty, 0) > p.MaximumStock);

    INSERT inventory.InventoryAlerts(ProductId, WarehouseId, BatchNo, AlertType, CurrentQuantity, SuggestedQuantity, Detail)
    SELECT b.ProductId, b.WarehouseId, b.BatchNo,
        CASE WHEN MIN(i.ExpiryDate) < CAST(SYSUTCDATETIME() AS date) THEN 'EXPIRED_STOCK' ELSE 'EXPIRING_SOON' END,
        SUM(b.QuantityAvailable), 0,
        CONCAT('Product: ', p.ProductName, ' | Product code: ', p.ProductCode,
               ' | Batch: ', COALESCE(NULLIF(b.BatchNo, ''), 'Unbatched'),
               ' | Expiry date: ', FORMAT(MIN(i.ExpiryDate), 'dd MMM yyyy'),
               ' | Available quantity: ', FORMAT(SUM(b.QuantityAvailable), '0.##'),
               ' | Warehouse: ', w.WarehouseName,
               ' | Action: ', CASE WHEN MIN(i.ExpiryDate) < CAST(SYSUTCDATETIME() AS date) THEN 'remove expired stock from sale immediately.' ELSE 'prioritise this batch for sale and plan replenishment.' END)
    FROM inventory.InventoryBalances b
    JOIN master.Products p ON p.ProductId = b.ProductId
    JOIN inventory.Warehouses w ON w.WarehouseId = b.WarehouseId
    JOIN purchase.PurchaseInvoiceItems i ON i.ProductId = b.ProductId AND ISNULL(i.BatchNo, '') = ISNULL(b.BatchNo, '')
    WHERE b.QuantityAvailable > 0 AND i.ExpiryDate IS NOT NULL
      AND i.ExpiryDate <= DATEADD(day, 30, CAST(SYSUTCDATETIME() AS date))
    GROUP BY b.ProductId, p.ProductName, p.ProductCode, b.WarehouseId, w.WarehouseName, b.BatchNo;

    DELETE inventory.ReorderSuggestions WHERE Status = 'OPEN';
    INSERT inventory.ReorderSuggestions(ProductId, WarehouseId, CurrentStock, PendingPurchase, SalesVelocity, SuggestedQuantity)
    SELECT p.ProductId, w.WarehouseId, ISNULL(s.Qty, 0), ISNULL(pp.Qty, 0), ISNULL(v.Qty, 0) / 30.0,
        CASE WHEN p.MaximumStock > ISNULL(s.Qty, 0) + ISNULL(pp.Qty, 0) THEN p.MaximumStock - ISNULL(s.Qty, 0) - ISNULL(pp.Qty, 0)
             ELSE p.ReorderLevel + ISNULL(v.Qty, 0) - ISNULL(s.Qty, 0) - ISNULL(pp.Qty, 0) END
    FROM master.Products p CROSS JOIN inventory.Warehouses w
    OUTER APPLY (SELECT SUM(QuantityAvailable) Qty FROM inventory.InventoryBalances b WHERE b.ProductId = p.ProductId AND b.WarehouseId = w.WarehouseId) s
    OUTER APPLY (SELECT SUM(i.Quantity + i.FreeQuantity - i.ReturnedQuantity) Qty FROM purchase.PurchaseInvoiceItems i JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId = i.PurchaseInvoiceId WHERE i.ProductId = p.ProductId AND h.WarehouseId = w.WarehouseId AND h.Status IN ('DRAFT', 'PENDING')) pp
    OUTER APPLY (SELECT SUM(i.Quantity - i.ReturnedQuantity) Qty FROM sales.SalesInvoiceItems i JOIN sales.SalesInvoices h ON h.InvoiceId = i.InvoiceId WHERE i.ProductId = p.ProductId AND h.WarehouseId = w.WarehouseId AND h.InvoiceDate >= DATEADD(day, -30, SYSUTCDATETIME()) AND h.Status IN ('POSTED', 'PAID', 'PARTIAL')) v
    WHERE p.IsActive = 1 AND p.IsDeleted = 0 AND w.IsActive = 1 AND ISNULL(s.Qty, 0) + ISNULL(pp.Qty, 0) <= p.ReorderLevel;
    COMMIT;
END;
GO

ALTER PROCEDURE dashboard.Notifications_Get @Top int = 50 AS
BEGIN
    SET NOCOUNT ON;
    EXEC inventory.RefreshStockControl;
    EXEC finance.RefreshOutstanding;
    DELETE dashboard.DashboardNotifications WHERE ReferenceType IN ('INVENTORY_ALERT', 'CUSTOMER_OUTSTANDING', 'SUPPLIER_OUTSTANDING') AND IsDismissed = 0;
    INSERT dashboard.DashboardNotifications(NotificationType, Severity, Title, Message, ReferenceType, ReferenceId)
    SELECT AlertType,
        CASE WHEN AlertType IN ('NEGATIVE_STOCK', 'EXPIRED_STOCK') THEN 'CRITICAL' ELSE 'WARNING' END,
        CASE AlertType WHEN 'EXPIRED_STOCK' THEN 'Expired stock requires immediate action' WHEN 'EXPIRING_SOON' THEN 'Stock approaching expiry' WHEN 'LOW_STOCK' THEN 'Stock below reorder level' WHEN 'OUT_OF_STOCK' THEN 'Product out of stock' WHEN 'NEGATIVE_STOCK' THEN 'Negative stock detected' ELSE REPLACE(AlertType, '_', ' ') END,
        Detail, 'INVENTORY_ALERT', InventoryAlertId
    FROM inventory.InventoryAlerts WHERE Status = 'ACTIVE';
    INSERT dashboard.DashboardNotifications(NotificationType, Severity, Title, Message, ReferenceType, ReferenceId)
    SELECT 'PENDING_COLLECTION', 'WARNING', 'Customer payment pending', CONCAT('Invoice: ', InvoiceNumber, ' | Outstanding amount: ', FORMAT(OutstandingAmount, 'N2'), ' | Action: follow up with the customer.'), 'CUSTOMER_OUTSTANDING', InvoiceId FROM finance.CustomerOutstanding WHERE OutstandingAmount > 0;
    INSERT dashboard.DashboardNotifications(NotificationType, Severity, Title, Message, ReferenceType, ReferenceId)
    SELECT 'PENDING_PAYMENT', 'WARNING', 'Supplier payment pending', CONCAT('Invoice: ', InvoiceNumber, ' | Outstanding amount: ', FORMAT(OutstandingAmount, 'N2'), ' | Action: review and schedule supplier payment.'), 'SUPPLIER_OUTSTANDING', PurchaseInvoiceId FROM finance.SupplierOutstanding WHERE OutstandingAmount > 0;
    SELECT TOP (@Top) DashboardNotificationId, NotificationType, Severity, Title, Message, ReferenceType, ReferenceId, IsRead, GeneratedOn
    FROM dashboard.DashboardNotifications WHERE IsDismissed = 0 AND (ExpiresOn IS NULL OR ExpiresOn > SYSUTCDATETIME())
    ORDER BY CASE Severity WHEN 'CRITICAL' THEN 1 WHEN 'WARNING' THEN 2 ELSE 3 END, GeneratedOn DESC;
END;
GO
