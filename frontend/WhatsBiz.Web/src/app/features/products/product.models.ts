export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface ProductListItem {
  productId: string;
  productCode: string;
  barcode?: string;
  productName: string;
  categoryName: string;
  brandName: string;
  unitName: string;
  purchasePrice: number;
  sellingPrice: number;
  gstPercentage: number;
  isActive: boolean;
  imageUrl?: string;
}
export interface Product extends ProductInput {
  productId: string;
  categoryName: string;
  brandName: string;
  unitName: string;
  imageUrl?: string;
}
export interface ProductImage { productImageId: string; productId: string; fileName: string; contentType: string; isPrimary: boolean; url: string; }
export interface ProductHistory {
  id: number;
  action: string;
  details: string;
  userName?: string;
  succeeded: boolean;
  occurredOn: string;
}
export interface ProductInput {
  productCode: string;
  barcode: string | null;
  barcodeType: string;
  additionalBarcodes: ProductBarcodeInput[];
  productName: string;
  shortDescription: string | null;
  longDescription: string | null;
  categoryId: string;
  brandId: string;
  unitId: string;
  hsnCode: string | null;
  sacCode: string | null;
  gstPercentage: number;
  purchasePrice: number;
  sellingPrice: number;
  mrp: number;
  minimumStock: number;
  maximumStock: number;
  reorderLevel: number;
  weight: number | null;
  length: number | null;
  width: number | null;
  height: number | null;
  isBatchManaged: boolean;
  isSerialManaged: boolean;
  isActive: boolean;
}
export interface ProductBarcodeInput {
  barcode: string;
  barcodeType: string;
}
export interface ProductBarcode extends ProductBarcodeInput {
  productBarcodeId: string;
}
export interface Category {
  productCategoryId: string;
  categoryCode: string;
  categoryName: string;
  description?: string;
  displayOrder: number;
  parentCategoryId?: string;
  isActive: boolean;
  children: Category[];
}
export interface Brand {
  brandId: string;
  brandCode: string;
  brandName: string;
  description?: string;
  logo?: string;
  isActive: boolean;
}
export interface UnitOfMeasure {
  unitId: string;
  unitCode: string;
  unitName: string;
  shortName: string;
  decimalPlaces: number;
  isActive: boolean;
}
export interface ImportResult {
  importedCount: number;
  errors: string[];
}
