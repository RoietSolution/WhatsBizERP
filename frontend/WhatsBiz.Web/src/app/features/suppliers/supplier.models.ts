export interface SupplierList {
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  supplierType: string;
  gstin?: string;
  mobile?: string;
  email?: string;
  currency: string;
  creditLimit: number;
  isActive: boolean;
}
export interface PagedSuppliers {
  items: SupplierList[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface Contact {
  contactId?: string;
  contactPerson: string;
  designation?: string;
  mobile?: string;
  email?: string;
  department?: string;
  isPrimary: boolean;
}
export interface Address {
  addressId?: string;
  addressType: string;
  addressLine1: string;
  addressLine2?: string;
  city: string;
  district?: string;
  state: string;
  country: string;
  postalCode: string;
}
export interface Bank {
  bankAccountId?: string;
  bankName: string;
  branch?: string;
  accountNumber: string;
  ifscCode?: string;
  upiId?: string;
}
export interface Document {
  documentId: string;
  documentType: string;
  fileName: string;
  contentType: string;
  uploadedOn: string;
}
export interface SupplierInput {
  supplierCode: string;
  supplierName: string;
  supplierType: string;
  gstin?: string;
  pan?: string;
  msmeRegistrationNumber?: string;
  email?: string;
  mobile?: string;
  telephone?: string;
  website?: string;
  currency: string;
  paymentTermId?: string;
  creditLimit: number;
  openingBalance: number;
  isGSTRegistered: boolean;
  isTDSApplicable: boolean;
  isActive: boolean;
  remarks?: string;
  contacts: Contact[];
  addresses: Address[];
  bankAccounts: Bank[];
}
export interface Supplier extends SupplierInput {
  supplierId: string;
  paymentTermName?: string;
  documents: Document[];
}
export interface PaymentTerm {
  paymentTermId: string;
  paymentTermCode: string;
  paymentTermName: string;
  dueDays: number;
  isDefault: boolean;
}
