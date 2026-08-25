import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController,provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DeliveryApiService } from './delivery-api.service';

describe('DeliveryApiService',()=>{
 let service:DeliveryApiService;let http:HttpTestingController;
 beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});service=TestBed.inject(DeliveryApiService);http=TestBed.inject(HttpTestingController)});
 afterEach(()=>http.verify());
 it('derives my-deliveries identity on the server without an agent id',()=>{service.my('OUT_FOR_DELIVERY').subscribe();const request=http.expectOne('/api/delivery/my-deliveries?status=OUT_FOR_DELIVERY');expect(request.request.method).toBe('GET');expect(request.request.params.has('deliveryAgentId')).toBeFalse();request.flush([])});
 it('sends manual assignment only for the selected delivery',()=>{service.assign('delivery-1','agent-2','coverage').subscribe();const request=http.expectOne('/api/deliveries/delivery-1/assign');expect(request.request.body).toEqual({deliveryAgentId:'agent-2',reason:'coverage'});request.flush({})});
 it('uses controlled COD endpoint',()=>{service.action('delivery-1','cod-collected',{paymentMethod:'CASH',amount:1250}).subscribe();const request=http.expectOne('/api/deliveries/delivery-1/cod-collected');expect(request.request.method).toBe('POST');request.flush({})});
});
