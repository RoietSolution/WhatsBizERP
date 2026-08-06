export interface NavigationItem {
  label: string;
  icon: string;
  route?: string;
  permission?: string;
  children?: NavigationItem[];
}
