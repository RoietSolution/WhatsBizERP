export interface CustomerList {
  customerId: string;
  customerCode: string;
  customerName: string;
  customerType: string;
  gstin?: string;
  mobile?: string;
  email?: string;
  currency: string;
  creditLimit: number;
  isActive: boolean;
}
export interface PagedCustomers {
  items: CustomerList[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface CustomerDropdown { customerId:string;customerCode:string;customerName:string; }
export interface Contact {
  contactId?: string;
  contactPerson: string;
  designation?: string;
  department?: string;
  mobile?: string;
  email?: string;
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
export interface CustomerDocument {
  documentId: string;
  documentType: string;
  fileName: string;
  contentType: string;
  uploadedOn: string;
}
export interface CustomerInput {
  customerCode: string;
  customerName: string;
  customerType: string;
  gstin?: string;
  pan?: string;
  email?: string;
  mobile?: string;
  telephone?: string;
  website?: string;
  currency: string;
  paymentTermId?: string;
  creditLimit: number;
  openingBalance: number;
  salesPersonId?: string;
  customerGroupId?: string;
  priceListId?: string;
  isGSTRegistered: boolean;
  isActive: boolean;
  remarks?: string;
  contacts: Contact[];
  addresses: Address[];
  bankAccounts: Bank[];
}
export interface Customer extends CustomerInput {
  customerId: string;
  paymentTermName?: string;
  documents: CustomerDocument[];
}
export interface PaymentTerm {
  paymentTermId: string;
  paymentTermName: string;
  dueDays: number;
  isDefault: boolean;
}
