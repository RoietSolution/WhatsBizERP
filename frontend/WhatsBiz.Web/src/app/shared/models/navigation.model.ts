export interface NavigationItem {
  label: string;
  icon: string;
  route?: string;
  permission?: string;
  feature?: string;
  children?: NavigationItem[];
}
