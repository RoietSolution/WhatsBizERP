import { ColDef } from 'ag-grid-community';

export type MasterAction =
  | 'new'
  | 'edit'
  | 'delete'
  | 'refresh'
  | 'recent'
  | 'template'
  | 'import'
  | 'export'
  | 'print'
  | 'bulk'
  | 'columns'
  | 'view'
  | 'duplicate'
  | 'history';
export interface MasterPageConfig<T extends object> {
  title: string;
  singular: string;
  description: string;
  icon: string;
  newRoute: string;
  rowId: keyof T & string;
  rowName: keyof T & string;
  columns: ColDef<T>[];
  detailFields: MasterDetailField<T>[];
  templateEnabled?: boolean;
  templateLabel?: string;
  importEnabled?: boolean;
  exportEnabled?: boolean;
  printEnabled?: boolean;
  cardViewEnabled?: boolean;
  cardImageEnabled?: boolean;
  cardSubtitleField?: keyof T & string;
  cardPriceField?: keyof T & string;
  cardFields?: MasterDetailField<T>[];
  viewRoute?: string;
  recentEnabled?: boolean;
}
export interface MasterDetailField<T extends object> {
  label: string;
  key: keyof T & string;
}
export interface MasterSummary {
  label: string;
  value: string | number;
  subtitle: string;
  icon: string;
  tone: 'primary' | 'success' | 'warning' | 'danger' | 'info';
}
export interface MasterActionEvent<T extends object> {
  action: MasterAction;
  row?: T;
  rows?: T[];
}
export interface MasterPageEvent {
  pageIndex: number;
  pageSize: number;
}
export interface MasterSortEvent {
  field: string;
  direction: 'asc' | 'desc' | '';
}
