export interface POSProduct {
  productId: string;
  productCode: string;
  barcode?: string;
  productName: string;
  categoryId: string;
  brandId: string;
  sellingPrice: number;
  mrp: number;
  gstPercentage: number;
  isBatchManaged: boolean;
  isSerialManaged: boolean;
  availableQuantity?: number | null;
  negativeStockAllowed: boolean;
}
export interface POSCategory {
  productCategoryId: string;
  categoryName: string;
  children: POSCategory[];
}
export interface POSBrand {
  brandId: string;
  brandName: string;
}
export interface POSCustomer {
  customerId: string;
  customerCode: string;
  customerName: string;
  mobile?: string;
  gstin?: string;
}
export interface CartItem extends POSProduct {
  quantity: number;
  unitPrice: number;
  discountPercentage: number;
  discountAmount: number;
  taxPercentage: number;
}
export interface Payment {
  methodCode: string;
  amount: number;
  referenceNumber?: string;
}
export interface PaymentMethod {
  paymentMethodId: string;
  methodCode: string;
  methodName: string;
  requiresReference: boolean;
}
export interface InvoiceList {
  invoiceId: string;
  invoiceNumber: string;
  invoiceDate: string;
  customerName?: string;
  grandTotal: number;
  paidAmount: number;
  balanceAmount: number;
    status: string;
    sourceChannel?: string;
}
export interface PagedInvoices {
  items: InvoiceList[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface InvoiceItem {
  invoiceItemId: string;
  productId: string;
  productCode: string;
  productName: string;
  quantity: number;
  returnedQuantity: number;
  unitPrice: number;
  lineTotal: number;
}
export interface Invoice extends InvoiceList {
  customerId?: string;
  warehouseId: string;
  warehouseName: string;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  roundOff: number;
  remarks?: string;
  items: InvoiceItem[];
  payments: Payment[];
}
export interface TodaySales {
  grossSales: number;
  collections: number;
  invoiceCount: number;
  cash: number;
  upi: number;
  card: number;
}
