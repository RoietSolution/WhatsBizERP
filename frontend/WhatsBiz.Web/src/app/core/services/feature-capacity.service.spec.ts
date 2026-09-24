import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CurrentUserService } from './current-user.service';
import { FeatureService } from './feature.service';

describe('FeatureService tenant capacity',()=>{
  let service:FeatureService;let http:HttpTestingController;
  beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting(),{provide:CurrentUserService,useValue:{user:()=>null}}]});service=TestBed.inject(FeatureService);http=TestBed.inject(HttpTestingController);});
  afterEach(()=>http.verify());
  it('uses the platform tenant route to read capacity',()=>{service.capacity('tenant-1').subscribe();const request=http.expectOne('/api/system/tenants/tenant-1/capacity');expect(request.request.method).toBe('GET');request.flush({});});
  it('sends explicit finite and unlimited semantics',()=>{service.updateCapacity('tenant-1',{configured:true,unlimited:false,limit:5},{configured:true,unlimited:true}).subscribe();const request=http.expectOne('/api/system/tenants/tenant-1/capacity');expect(request.request.method).toBe('PUT');expect(request.request.body).toEqual({users:{configured:true,unlimited:false,limit:5},branches:{configured:true,unlimited:true}});request.flush({});});
});
