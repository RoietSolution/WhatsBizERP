IF NOT EXISTS(SELECT 1 FROM inventory.InventorySettings)INSERT inventory.InventorySettings(NegativeStockAllowed,BatchTrackingEnabled,SerialTrackingEnabled,ValuationMethod)VALUES(0,1,1,'AVERAGE');
