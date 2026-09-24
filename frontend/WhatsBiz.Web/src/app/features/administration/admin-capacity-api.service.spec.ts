import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminApiService } from './admin-api.service';

describe('AdminApiService capacity',()=>{
  let service:AdminApiService;let http:HttpTestingController;
  beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});service=TestBed.inject(AdminApiService);http=TestBed.inject(HttpTestingController);});
  afterEach(()=>http.verify());
  it('reads only authenticated tenant capacity without a tenant id',()=>{service.capacity().subscribe();const request=http.expectOne('/api/capacity');expect(request.request.method).toBe('GET');request.flush({});});
  it('updates a tenant-scoped branch without a client tenant id',()=>{service.updateBranch('branch-1',{branchName:'Main',isActive:true}).subscribe();const request=http.expectOne('/api/admin/branches/branch-1');expect(request.request.body).toEqual({branchName:'Main',isActive:true});expect(request.request.body.tenantId).toBeUndefined();request.flush({});});
});
