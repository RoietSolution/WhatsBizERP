MERGE inventory.WarehouseTypes AS target
USING (VALUES
('GENERAL','General Warehouse','General-purpose storage facility'),
('DISTRIBUTION','Distribution Center','High-throughput distribution facility'),
('COLD','Cold Storage','Temperature-controlled storage facility'),
('BONDED','Bonded Warehouse','Customs-controlled bonded facility'),
('TRANSIT','Transit Warehouse','Short-term transit storage facility')) AS source(TypeCode,TypeName,Description)
ON target.TypeCode=source.TypeCode
WHEN MATCHED THEN UPDATE SET TypeName=source.TypeName,Description=source.Description,IsActive=1
WHEN NOT MATCHED THEN INSERT(TypeCode,TypeName,Description) VALUES(source.TypeCode,source.TypeName,source.Description);
