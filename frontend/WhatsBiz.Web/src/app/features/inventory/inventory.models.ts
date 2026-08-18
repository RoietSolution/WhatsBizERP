export interface InventorySummary {
  totalQuantity: number;
  totalStockValue: number;
  reservedStock: number;
  lowStockProducts: number;
  outOfStockProducts: number;
}
export interface Balance {
  inventoryBalanceId: string;
  productId: string;
  productCode: string;
  productName: string;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  zoneId?: string;
  zoneCode?: string;
  binId?: string;
  binCode?: string;
  batchNo?: string;
  serialNo?: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  averageCost: number;
  lastPurchaseCost: number;
  stockValue: number;
  lastUpdated: string;
}
export interface PagedBalances {
  items: Balance[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface TransactionList {
  transactionId: string;
  transactionNo: string;
  transactionDate: string;
  transactionType: string;
  referenceType?: string;
  warehouseId: string;
  warehouseName: string;
  totalQuantity: number;
  totalCost: number;
}
export interface PagedTransactions {
  items: TransactionList[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface TransactionLine {
  transactionDetailId: string;
  productId: string;
  productCode: string;
  productName: string;
  batchNo?: string;
  serialNo?: string;
  quantity: number;
  unitCost: number;
  totalCost: number;
}
export interface InventoryTransaction extends TransactionList {
  referenceId?: string;
  remarks?: string;
  createdBy?: string;
  details: TransactionLine[];
}
export interface Reservation {
  stockReservationId: string;
  reservationNo: string;
  productId: string;
  warehouseId: string;
  quantity: number;
  releasedQuantity: number;
  reservationReason: string;
  referenceType?: string;
  referenceId?: string;
  status: string;
  createdOn: string;
}
export interface ProductOption {
  productId: string;
  productCode: string;
  productName: string;
}
export interface WarehouseOption {
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
}
export interface OperationResult {
  operationId: string;
  transactionId: string;
  number: string;
}
