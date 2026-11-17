import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { DeliveryApiService, DeliverySettings, MarketplaceFulfilmentGroup } from './delivery-api.service';
import { MarketplaceFulfilmentManagerComponent, MarketplaceParentOrder } from './marketplace-fulfilment-manager.component';

describe('MarketplaceFulfilmentManagerComponent', () => {
  let fixture: ComponentFixture<MarketplaceFulfilmentManagerComponent>;
  let api: jasmine.SpyObj<DeliveryApiService>;
  const allowed = new Set<string>();
  const parent: MarketplaceParentOrder = { id:'order-1',orderNumber:'GT10025',customerName:'Asha',customerMobile:'9000000000',total:2200,paymentMethod:'COD',paymentStatus:'COD_PENDING',status:'Confirmed' };
  const group = (overrides: Partial<MarketplaceFulfilmentGroup> = {}): MarketplaceFulfilmentGroup => ({
    fulfilmentGroupId:'group-1',orderId:'order-1',groupNumber:1,warehouseId:'warehouse-1',warehouseName:'Warehouse A',sellerKind:'OWNED',sellerName:'GuturGo inventory',
    status:'PREPARING',deliveryAgentId:'agent-1',deliveryAgentName:'Rider One',assignmentSource:'MANUAL',deliveryAddress:'12 Main Road',items:[{invoiceItemId:'item-1',productId:'product-1',productName:'Saree',quantity:1}],events:[],
    parentLifecycleStatus:'POSTED',parentPaymentStatus:'COD_PENDING',parentDeliveryStatus:'PARTIALLY_DELIVERED',parentRefundStatus:'NONE',parentCodStatus:'PENDING',codCollectionFulfilmentGroupId:'group-1',codExpectedAmount:2200,codCollectedAmount:0,...overrides,
  });
  const settings = (requireOtp:boolean):DeliverySettings => ({deliveryEnabled:true,automaticAssignmentEnabled:true,defaultAgentValid:true,requireDeliveryOtp:requireOtp,deliveryOtpExpiryMinutes:10,maxOtpAttempts:5,otpResendCooldownSeconds:30,maxOtpResends:3,notifyOnAssigned:false,notifyOnOutForDelivery:false,notifyOnDelivered:false,notifyOnDeliveryFailed:false,requireCodConfirmation:true});

  beforeEach(async () => {
    api = jasmine.createSpyObj<DeliveryApiService>('DeliveryApiService', [
      'marketplaceGroups','agents','settings','marketplaceVerificationRequirements','assignMarketplaceGroup','transitionMarketplaceGroup','requestMarketplaceGroupOtp',
      'confirmMarketplaceGroupOtp','confirmMarketplaceGroup','overrideMarketplaceGroup','cancelMarketplaceGroup','assignMarketplaceCod',
    ]);
    api.marketplaceGroups.and.returnValue(of([group(),group({fulfilmentGroupId:'group-2',groupNumber:2,warehouseId:'warehouse-2',warehouseName:'Warehouse B',sellerKind:'PARTNER',sellerName:'Retailer B',status:'OUT_FOR_DELIVERY',deliveryAgentId:'agent-2',deliveryAgentName:'Rider Two',parentDeliveryStatus:'PARTIALLY_DELIVERED',items:[{invoiceItemId:'item-2',productId:'product-2',productName:'Kitchen Rack',quantity:1}]})]));
    api.agents.and.returnValue(of([{deliveryAgentId:'agent-1',userId:'user-1',displayName:'Rider One',mobile:'',isActive:true,isAvailable:true,isDefault:false,activeDeliveries:0,deliveredToday:0}]));
    api.settings.and.returnValue(of(settings(false)));
    api.marketplaceVerificationRequirements.and.returnValue(of({requireDeliveryOtp:false,requireCodConfirmation:true}));
    const permission = { has: (value:string) => allowed.has(value) };
    await TestBed.configureTestingModule({imports:[MarketplaceFulfilmentManagerComponent],providers:[{provide:DeliveryApiService,useValue:api},{provide:PermissionService,useValue:permission}]}).compileComponents();
    fixture=TestBed.createComponent(MarketplaceFulfilmentManagerComponent);fixture.componentRef.setInput('parentOrder',parent);
  });

  it('shows one parent order with separate group items, statuses, and seller/warehouse attribution', async () => {
    fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    const text=(fixture.nativeElement as HTMLElement).textContent||'';
    expect(text).toContain('GT10025');expect(text).toContain('Partially Delivered');expect(text).toContain('Saree');expect(text).toContain('Kitchen Rack');
    expect(text).toContain('Warehouse A');expect(text).toContain('Retailer B');expect(api.marketplaceGroups).toHaveBeenCalledOnceWith('order-1');
  });

  it('hides all operational controls without delivery permissions', async () => {
    allowed.clear();fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    const text=(fixture.nativeElement as HTMLElement).textContent||'';
    expect(text).not.toContain('Assign agent');expect(text).not.toContain('Start preparing');expect(text).not.toContain('Request OTP');expect(text).not.toContain('Review and cancel group');
  });

  it('assigns only the selected group', async () => {
    allowed.clear();allowed.add('delivery.assign');api.assignMarketplaceGroup.and.returnValue(of(group()));
    fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    const button=Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(x=>x.textContent?.includes('Save assignment')) as HTMLButtonElement;
    button.click();await fixture.whenStable();fixture.detectChanges();
    expect(api.assignMarketplaceGroup).toHaveBeenCalledWith('order-1','group-1','agent-1',undefined);
  });

  it('requests OTP for the matching out-for-delivery group without showing any secret', async () => {
    allowed.clear();allowed.add('delivery.confirm');api.marketplaceVerificationRequirements.and.returnValue(of({requireDeliveryOtp:true,requireCodConfirmation:true}));api.requestMarketplaceGroupOtp.and.returnValue(of(void 0));
    fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    const button=Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(x=>x.textContent?.includes('Request OTP')) as HTMLButtonElement;
    button.click();await fixture.whenStable();fixture.detectChanges();
    expect(api.requestMarketplaceGroupOtp).toHaveBeenCalledOnceWith('order-1','group-2');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('1234');
  });

  it('reassigns the one parent COD responsibility only after its owner fails', async () => {
    allowed.clear();allowed.add('delivery.manage');
    const failedOwner=group({status:'DELIVERY_FAILED'});
    api.marketplaceGroups.and.returnValue(of([failedOwner,group({fulfilmentGroupId:'group-2',groupNumber:2,warehouseId:'warehouse-2',warehouseName:'Warehouse B',sellerKind:'PARTNER',sellerName:'Retailer B',deliveryAgentId:'agent-2',deliveryAgentName:'Rider Two',status:'PREPARING'})]));
    api.assignMarketplaceCod.and.returnValue(of({orderId:'order-1',status:'PENDING',amount:2200,fulfilmentGroupId:'group-2',isReplay:false}));
    fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    const component=fixture.componentInstance;component.codTargetGroupId='group-2';component.codReason='First delivery failed';
    await component.reassignCod();fixture.detectChanges();
    expect(api.assignMarketplaceCod).toHaveBeenCalledWith('order-1','group-2','First delivery failed',jasmine.any(String));
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No collection was recorded');
  });

  it('renders a refund review obligation as pending review, not as a completed refund', async () => {
    api.marketplaceGroups.and.returnValue(of([group({refundStatus:'REVIEW_REQUIRED',refundAmount:null,refundReviewReason:'Shared offer allocation needs review',parentRefundStatus:'REVIEW_REQUIRED'})]));
    fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    const text=(fixture.nativeElement as HTMLElement).textContent||'';
    expect(text).toContain('Review Required');expect(text).toContain('Shared offer allocation needs review');expect(text).not.toContain('Refund confirmed.');
  });
});
