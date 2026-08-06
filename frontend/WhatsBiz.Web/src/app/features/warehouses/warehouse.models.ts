export interface WarehouseType {
  warehouseTypeId: string; typeCode: string; typeName: string; description?: string; isActive: boolean;
}
export interface WarehouseAddress {
  addressId?: string; addressLine1: string; addressLine2?: string; city: string; district?: string; state: string; country: string; postalCode: string;
}
export interface WarehouseContact extends Record<string, unknown> {
  contactId?: string; contactPerson: string; designation?: string; mobile?: string; email?: string; isPrimary: boolean;
}
export interface WarehouseBin extends Record<string, unknown> {
  binId?: string; binCode: string; binName: string; maximumCapacity: number; isActive: boolean;
}
export interface WarehouseZone extends Record<string, unknown> {
  zoneId?: string; zoneCode: string; zoneName: string; description?: string; isActive: boolean; bins: WarehouseBin[];
}
export interface WarehouseInput {
  warehouseCode: string; warehouseName: string; warehouseTypeId: string; branchId?: string; managerName?: string; email?: string; phone?: string; mobile?: string; capacity: number; isDefault: boolean; isActive: boolean; remarks?: string; address?: WarehouseAddress; contacts: WarehouseContact[]; zones: WarehouseZone[];
}
export interface Warehouse extends WarehouseInput { warehouseId: string; typeName: string; addressId?: string; }
export interface WarehouseList {
  warehouseId: string; warehouseCode: string; warehouseName: string; warehouseTypeId: string; typeName: string; managerName?: string; mobile?: string; capacity: number; isDefault: boolean; isActive: boolean;
}
export interface PagedWarehouses { items: WarehouseList[]; totalCount: number; pageNumber: number; pageSize: number; }
