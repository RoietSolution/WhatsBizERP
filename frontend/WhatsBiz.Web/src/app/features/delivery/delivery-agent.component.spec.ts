import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { DeliveryApiService } from './delivery-api.service';
import { DeliveryAgentComponent } from './delivery-agent.component';

describe('DeliveryAgentComponent marketplace deliveries', () => {
  let fixture: ComponentFixture<DeliveryAgentComponent>;
  let api: jasmine.SpyObj<DeliveryApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<DeliveryApiService>('DeliveryApiService', [
      'my', 'marketplaceAssignedOrders', 'marketplaceGroups', 'marketplaceVerificationRequirements',
    ]);
    api.my.and.returnValue(of([]));
    api.marketplaceAssignedOrders.and.returnValue(of([{
      orderId: 'order-1', orderNumber: 'GT10025', orderDate: '2026-11-09T10:00:00Z',
      customerName: 'Asha', total: 2200, paymentMethod: 'COD', paymentStatus: 'COD pending', assignedGroupCount: 2,
    }]));
    api.marketplaceGroups.and.returnValue(of([]));
    api.marketplaceVerificationRequirements.and.returnValue(of({ requireDeliveryOtp: false, requireCodConfirmation: true }));

    await TestBed.configureTestingModule({
      imports: [DeliveryAgentComponent],
      providers: [
        { provide: DeliveryApiService, useValue: api },
        { provide: PermissionService, useValue: { has: () => false } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DeliveryAgentComponent);
  });

  it('shows assigned marketplace parent orders with one order-level payment summary', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent || '';
    expect(text).toContain('Marketplace deliveries');
    expect(text).toContain('GT10025');
    expect(text).toContain('2 assigned deliveries');
    expect(text).toContain('COD pending');
    expect(api.marketplaceAssignedOrders).toHaveBeenCalledTimes(1);
  });

  it('opens only the assigned parent order group view', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(x => x.textContent?.includes('View assigned delivery groups')) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(api.marketplaceGroups).toHaveBeenCalledOnceWith('order-1');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Only deliveries assigned to you are shown here.');
  });
});
