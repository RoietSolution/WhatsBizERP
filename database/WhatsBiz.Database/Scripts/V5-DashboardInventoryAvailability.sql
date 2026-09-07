/* Include active products that have no balance row in the dashboard's out-of-stock KPI. */
ALTER PROCEDURE dashboard.Inventory_Get @TenantId UNIQUEIDENTIFIER = NULL AS
BEGIN
 SET NOCOUNT ON;
 SET @TenantId = COALESCE(@TenantId, TRY_CONVERT(UNIQUEIDENTIFIER, SESSION_CONTEXT(N'TenantId')));
 IF @TenantId IS NULL THROW 51000, 'Tenant context is required.', 1;
 EXEC inventory.RefreshStockControl;
 SELECT ISNULL(SUM(b.QuantityOnHand*p.AverageCost),0),
        COUNT(DISTINCT CASE WHEN b.QuantityAvailable>0 AND b.QuantityAvailable<=p.ReorderLevel THEN b.ProductId END),
        COUNT(DISTINCT CASE WHEN COALESCE(s.Available,0)<=0 THEN p.ProductId END),
        COUNT(DISTINCT CASE WHEN b.QuantityAvailable<0 THEN b.ProductId END),
        ISNULL((SELECT COUNT(*) FROM inventory.InventoryAlerts a JOIN master.Products ap ON ap.ProductId=a.ProductId WHERE a.Status='ACTIVE' AND a.AlertType='EXPIRING_SOON' AND ap.TenantId=@TenantId),0),
        ISNULL((SELECT COUNT(*) FROM inventory.InventoryAlerts a JOIN master.Products ap ON ap.ProductId=a.ProductId WHERE a.Status='ACTIVE' AND a.AlertType='EXPIRED_STOCK' AND ap.TenantId=@TenantId),0),
        ISNULL((SELECT COUNT(*) FROM inventory.ReorderSuggestions r JOIN master.Products rp ON rp.ProductId=r.ProductId WHERE r.Status='OPEN' AND rp.TenantId=@TenantId),0)
 FROM master.Products p
 LEFT JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId
 OUTER APPLY (SELECT SUM(x.QuantityAvailable) Available FROM inventory.InventoryBalances x WHERE x.ProductId=p.ProductId) s
 WHERE p.TenantId=@TenantId AND p.IsActive=1 AND p.IsDeleted=0;
 SELECT TOP(20) p.ProductCode, SUM(b.QuantityAvailable) FROM inventory.InventoryBalances b JOIN master.Products p ON p.ProductId=b.ProductId WHERE p.TenantId=@TenantId AND p.IsActive=1 AND p.IsDeleted=0 AND b.QuantityAvailable<=p.ReorderLevel GROUP BY p.ProductCode ORDER BY SUM(b.QuantityAvailable);
END;
