CREATE FUNCTION [purchase].[SupplierDisplayName](@SupplierCode NVARCHAR(50),@SupplierName NVARCHAR(250)) RETURNS NVARCHAR(310) AS BEGIN RETURN CONCAT(@SupplierCode,N' - ',@SupplierName); END;
