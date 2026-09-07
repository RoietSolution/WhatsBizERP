import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { CustomerGroup } from './customer-group.models';
import { PagedCustomers } from './customer.models';
@Injectable({ providedIn: 'root' })
export class CustomerGroupApiService {
  constructor(private readonly http: HttpClient) {}
  list() { return this.http.get<CustomerGroup[]>('/api/customer-groups'); }
  create(input: { groupCode: string; groupName: string; isActive: boolean }) { return this.http.post<CustomerGroup>('/api/customer-groups', input); }
  update(id: string, input: { groupCode: string; groupName: string; isActive: boolean }) { return this.http.put<CustomerGroup>(`/api/customer-groups/${id}`, input); }
  delete(id: string) { return this.http.delete(`/api/customer-groups/${id}`); }
  customers(id: string) { return this.http.get<PagedCustomers>(`/api/customer-groups/${id}/customers`); }
}
