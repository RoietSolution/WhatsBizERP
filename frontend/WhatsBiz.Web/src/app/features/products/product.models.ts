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
export interface ProductInput {
  productCode: string;
  barcode: string | null;
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
