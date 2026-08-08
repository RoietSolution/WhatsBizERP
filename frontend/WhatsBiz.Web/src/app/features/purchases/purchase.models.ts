export interface PurchaseList {
  purchaseInvoiceId: string;
  invoiceNumber: string;
  supplierInvoiceNo?: string;
  invoiceDate: string;
  supplierName: string;
  warehouseName: string;
  grandTotal: number;
  paidAmount: number;
  balanceAmount: number;
  status: string;
}
export interface PagedPurchases {
  items: PurchaseList[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface PurchaseItem {
  purchaseItemId: string;
  productId: string;
  productCode: string;
  productName: string;
  barcode?: string;
  batchNo?: string;
  expiryDate?: string;
  quantity: number;
  freeQuantity: number;
  returnedQuantity: number;
  purchasePrice: number;
  mrp: number;
  sellingPrice: number;
  discountPercentage: number;
  discountAmount: number;
  gstPercentage: number;
  gstAmount: number;
  lineTotal: number;
}
export interface Purchase extends PurchaseList {
  supplierId: string;
  warehouseId: string;
  dueDate?: string;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  expenseAmount: number;
  roundOff: number;
  remarks?: string;
  items: PurchaseItem[];
}
export interface PurchaseDashboard {
  todayPurchases: number;
  todayCount: number;
  outstanding: number;
  monthPurchases: number;
}
