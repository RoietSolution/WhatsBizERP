import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { DeliveryAgent, DeliveryApiService, DeliveryVerificationRequirements, MarketplaceFulfilmentGroup } from './delivery-api.service';

export interface MarketplaceParentOrder {
  id: string; orderNumber: string; customerName: string; customerMobile?: string; total: number;
  paymentMethod: string; paymentStatus: string; status: string;
}

@Component({
  selector: 'app-marketplace-fulfilment-manager',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="manager" aria-label="Marketplace fulfilment groups">
      <header class="manager-head">
        <div>
          <p class="eyebrow">Marketplace order</p>
          <h3>{{ parentOrder.orderNumber }} <span class="parent-status">{{ label(parentStatus()) }}</span></h3>
          <p class="parent-line">{{ parentOrder.customerName }} <span *ngIf="parentOrder.customerMobile">· {{ parentOrder.customerMobile }}</span></p>
          <p class="parent-line">Payment: <strong>{{ label(parentOrder.paymentStatus) }}</strong> · {{ parentOrder.paymentMethod }} · {{ parentOrder.total | currency:'INR' }}</p>
          <p class="parent-line">Parent delivery: <strong>{{ label(parentStatus()) }}</strong> · COD collection: <strong>{{ label(parentCodStatus()) }}</strong></p>
        </div>
        <button class="quiet" type="button" (click)="closed.emit()">Close</button>
      </header>

      <p class="notice" *ngIf="groups().length > 1">This order is fulfilled in separate deliveries. Each group can progress independently.</p>
      <p class="notice" *ngIf="settings()?.requireDeliveryOtp">Delivery OTP is required. Codes are not displayed here; request or resend a code for the selected group.</p>
      <p class="state" *ngIf="loading()" role="status">Loading fulfilment groups…</p>
      <p class="feedback success" *ngIf="success()" role="status">{{ success() }}</p>
      <p class="feedback error" *ngIf="error()" role="alert">{{ error() }}</p>

      <section class="cod-panel" *ngIf="groups().length && parentCodStatus() !== 'NONE'" aria-label="Parent COD collection">
        <div><strong>One parent-order COD collection</strong><span>{{ label(parentCodStatus()) }} · {{ codAmount() | currency:'INR' }}</span>
          <small *ngIf="codOwner() as owner">Collection responsibility: Delivery {{ owner.groupNumber }} · {{ owner.deliveryAgentName || 'Agent not assigned' }}</small>
          <small *ngIf="groups()[0].codCollectedAt as collected">Collected {{ collected | date:'medium' }}</small>
        </div>
        <div class="cod-reassign" *ngIf="canReassignCod()">
          <label>Reassign collection to<select [(ngModel)]="codTargetGroupId" aria-label="Reassign COD collection group">
            <option value="">Select an eligible delivery</option>
            <option *ngFor="let g of codTargets()" [value]="g.fulfilmentGroupId">Delivery {{ g.groupNumber }} · {{ g.deliveryAgentName }}</option>
          </select></label>
          <label>Reason<input [(ngModel)]="codReason" maxlength="500" placeholder="Required after a failed delivery"></label>
          <button type="button" [disabled]="!codTargetGroupId || !codReason.trim() || busyGroup()" (click)="reassignCod()">Reassign collection</button>
        </div>
      </section>

      <div class="groups" *ngIf="!loading()">
        <article class="group-card" *ngFor="let group of groups(); trackBy: trackGroup">
          <header class="group-head">
            <div><span class="group-kicker">Delivery {{ group.groupNumber }}</span><h4>{{ group.warehouseName }}</h4>
              <p>{{ group.sellerName || sellerFallback(group) }} <small>· {{ label(group.sellerKind) }}</small></p>
            </div>
            <span class="status" [ngClass]="statusClass(group.status)">{{ label(group.status) }}</span>
          </header>

          <div class="facts">
            <div><small>Assigned agent</small><strong>{{ group.deliveryAgentName || 'Unassigned' }}</strong><span>{{ label(group.assignmentSource || 'MANUAL ASSIGNMENT') }}</span></div>
            <div><small>Tracking reference</small><strong>{{ group.trackingReference || 'Not provided' }}</strong><span *ngIf="group.dispatchedAt">Dispatched {{ group.dispatchedAt | date:'medium' }}</span></div>
            <div><small>Delivery progress</small><strong>{{ label(group.parentDeliveryStatus) }}</strong><span *ngIf="group.deliveredAt">Delivered {{ group.deliveredAt | date:'medium' }}</span></div>
            <div><small>Refund obligation</small><strong>{{ label(group.refundStatus || group.parentRefundStatus) }}</strong>
              <span *ngIf="group.refundAmount != null">{{ group.refundAmount | currency:'INR' }}</span>
              <span *ngIf="group.refundReviewReason" class="review-reason">Review required: {{ group.refundReviewReason }}</span>
              <span *ngIf="group.refundStatus === 'REFUND_PENDING'">Refund pending confirmation; not marked complete.</span>
              <span *ngIf="group.refundStatus === 'REFUNDED'">Refund confirmed.</span>
            </div>
          </div>

          <section class="items" aria-label="Allocated products"><strong>Allocated items</strong>
            <ul><li *ngFor="let item of group.items"><span>{{ item.productName }}</span><b>× {{ item.quantity }}</b></li></ul>
          </section>
          <p class="address"><strong>Delivery address</strong><span>{{ group.deliveryAddress }}</span></p>

          <details class="events"><summary>Group event history ({{ group.events.length }})</summary>
            <ol><li *ngFor="let event of group.events"><span>{{ label(event.eventType) }}</span><time>{{ event.createdAt | date:'medium' }}</time><small *ngIf="event.notes">{{ event.notes }}</small></li></ol>
          </details>

          <div class="controls" *ngIf="permissions.has('delivery.assign') && canAssign(group)">
            <label>Assign delivery agent<select [(ngModel)]="agentByGroup[group.fulfilmentGroupId]" [attr.aria-label]="'Agent for delivery ' + group.groupNumber">
              <option value="">Unassigned</option>
              <option *ngFor="let agent of activeAgents()" [value]="agent.deliveryAgentId">{{ agent.displayName }}{{ agent.isDefault ? ' (Default)' : '' }}</option>
            </select></label>
            <label>Assignment note<input [(ngModel)]="assignmentReason[group.fulfilmentGroupId]" maxlength="500" placeholder="Optional note"></label>
            <button type="button" [disabled]="busyGroup() === group.fulfilmentGroupId" (click)="assign(group)">{{ group.deliveryAgentId ? 'Save assignment' : 'Assign agent' }}</button>
          </div>

          <div class="controls status-controls" *ngIf="permissions.has('delivery.update-status')">
            <label *ngIf="canFail(group)">Failure reason<input [(ngModel)]="failureReason[group.fulfilmentGroupId]" maxlength="500" placeholder="Required to record a failed attempt"></label>
            <label *ngIf="group.status === 'READY_FOR_DISPATCH'">Tracking/reference<input [(ngModel)]="trackingByGroup[group.fulfilmentGroupId]" maxlength="160" placeholder="Optional dispatch reference"></label>
            <button type="button" *ngFor="let next of nextStatuses(group)" [disabled]="busyGroup() === group.fulfilmentGroupId || !canRunTransition(group,next)" (click)="transition(group,next)">{{ transitionLabel(next) }}</button>
            <button type="button" *ngIf="canFail(group)" [disabled]="busyGroup() === group.fulfilmentGroupId || !failureReason[group.fulfilmentGroupId]?.trim()" (click)="transition(group,'DELIVERY_FAILED')">Record failed delivery</button>
          </div>

          <p class="feedback error" *ngIf="group.status === 'OUT_FOR_DELIVERY' && permissions.has('delivery.confirm') && !settings()">Delivery settings could not be loaded; confirmation controls are disabled.</p>
          <p class="notice" *ngIf="group.status === 'OUT_FOR_DELIVERY' && parentOrder.paymentMethod.toUpperCase() === 'COD' && !canConfirmDelivery()">The full parent-order COD amount must be recorded before any delivery can be confirmed.</p>
          <section class="proof" *ngIf="group.status === 'OUT_FOR_DELIVERY' && permissions.has('delivery.confirm') && settings()" aria-label="Delivery confirmation">
            <strong>Delivery confirmation</strong>
            <ng-container *ngIf="settings()?.requireDeliveryOtp; else manualProof">
              <div class="controls">
                <button type="button" [disabled]="busyGroup() === group.fulfilmentGroupId" (click)="requestOtp(group)">{{ hasOtpRequest(group) ? 'Resend OTP' : 'Request OTP' }}</button>
                <label>Customer OTP<input [(ngModel)]="otpByGroup[group.fulfilmentGroupId]" type="password" inputmode="numeric" autocomplete="one-time-code" maxlength="4" pattern="[0-9]{4}" aria-label="Delivery OTP for this group"></label>
                <button type="button" [disabled]="busyGroup() === group.fulfilmentGroupId || !validOtp(group) || !canConfirmDelivery()" (click)="confirmOtp(group)">Verify and deliver</button>
              </div>
              <div class="controls override" *ngIf="permissions.has('delivery.override-otp')">
                <label>Override reason<input [(ngModel)]="overrideReason[group.fulfilmentGroupId]" maxlength="500" placeholder="Required and audited"></label>
                <button type="button" class="danger" [disabled]="busyGroup() === group.fulfilmentGroupId || !overrideReason[group.fulfilmentGroupId]?.trim() || !canConfirmDelivery()" (click)="overrideDelivery(group)">Confirm without OTP</button>
              </div>
            </ng-container>
            <ng-template #manualProof><div class="controls"><label>Confirmation reason<input [(ngModel)]="manualReason[group.fulfilmentGroupId]" maxlength="500" placeholder="Required for manual confirmation"></label><button type="button" [disabled]="busyGroup() === group.fulfilmentGroupId || !manualReason[group.fulfilmentGroupId]?.trim() || !canConfirmDelivery()" (click)="confirmWithoutOtp(group)">Confirm delivered</button></div></ng-template>
          </section>

          <section class="cancel" *ngIf="permissions.has('delivery.manage') && canCancel(group)">
            <strong>Cancel this delivery group</strong><p>Cancellation is audited. Any refund obligation shown above remains a review/settlement state, not a completed refund.</p>
            <div class="controls"><label>Cancellation reason<input [(ngModel)]="cancelReason[group.fulfilmentGroupId]" maxlength="500" placeholder="Required"></label>
              <label *ngIf="requiresRestock(group)">Approved stock-return transaction ID<input [(ngModel)]="restockByGroup[group.fulfilmentGroupId]" placeholder="Required after inventory posting"></label>
              <button type="button" class="danger" [disabled]="busyGroup() === group.fulfilmentGroupId || !cancelReason[group.fulfilmentGroupId]?.trim() || requiresRestock(group) && !isGuid(restockByGroup[group.fulfilmentGroupId])" (click)="cancelGroup(group)">Review and cancel group</button>
            </div>
          </section>
        </article>
      </div>
      <p class="empty" *ngIf="!loading() && !groups().length">No marketplace fulfilment groups were returned for this order.</p>
    </section>
  `,
  styles: [`
    :host{display:block;grid-column:1/-1;min-width:0}.manager{display:grid;gap:14px;margin:14px 0 2px;padding:16px;border:1px solid var(--wb-border,#d8e0e8);border-radius:14px;background:var(--wb-surface-muted,#f8fafb);color:var(--wb-text,#1d2939)}
    .manager-head,.group-head{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}.manager-head{padding-bottom:12px;border-bottom:1px solid var(--wb-border,#d8e0e8)}
    .eyebrow,.group-kicker{margin:0;color:var(--wb-primary,#146c43);font-size:.72rem;font-weight:800;letter-spacing:.06em;text-transform:uppercase}.manager h3,.group-card h4{margin:2px 0 6px}.parent-status{display:inline-block;margin-left:5px;padding:4px 8px;border-radius:999px;background:#e8f3ee;color:#125d43;font-size:.68rem;vertical-align:middle}
    .parent-line{margin:3px 0;color:var(--wb-text-secondary,#667085);font-size:.84rem}.quiet,.controls button{min-height:38px;border:1px solid var(--wb-border,#cbd5e1);border-radius:8px;padding:7px 11px;background:var(--wb-surface,#fff);color:var(--wb-text,#344054);font:inherit;font-weight:650;cursor:pointer}.quiet:hover,.controls button:hover:not(:disabled){border-color:var(--wb-primary,#146c43);color:var(--wb-primary,#146c43)}
    .notice,.feedback,.empty,.state{margin:0;padding:10px 12px;border-radius:9px;font-size:.86rem}.notice{border:1px solid #bfdbfe;background:#eff6ff;color:#1e3a5f}.feedback.success{background:#e9f7ef;color:#146c43}.feedback.error{background:#fff1f0;color:#b42318}.state,.empty{background:#fff;color:var(--wb-text-secondary,#667085)}
    .cod-panel,.group-card{background:var(--wb-surface,#fff);border:1px solid var(--wb-border,#d8e0e8);border-radius:12px}.cod-panel{display:flex;justify-content:space-between;gap:14px;align-items:start;padding:13px}.cod-panel>div:first-child{display:grid;gap:4px}.cod-panel span,.cod-panel small{display:block;color:var(--wb-text-secondary,#667085)}.cod-reassign{display:flex;flex-wrap:wrap;gap:8px;align-items:end}.cod-reassign label,.controls label{display:grid;gap:5px;min-width:160px;flex:1;font-size:.76rem;font-weight:700}.cod-reassign input,.cod-reassign select,.controls input,.controls select{width:100%;min-height:38px;border:1px solid #cbd5e1;border-radius:8px;padding:7px 9px;background:#fff;color:#1d2939;font:inherit}
    .groups{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,470px),1fr));gap:12px;min-width:0}.group-card{display:grid;align-content:start;gap:12px;padding:14px;min-width:0}.group-head{align-items:center}.group-head p{margin:0;color:var(--wb-text-secondary,#667085);font-size:.86rem;overflow-wrap:anywhere}.status{padding:5px 9px;border-radius:999px;font-size:.72rem;font-weight:800;white-space:nowrap}.status.good{background:#e9f7ef;color:#146c43}.status.progress{background:#eaf2ff;color:#174ea6}.status.attention{background:#fff4e5;color:#8a4b08}.status.closed{background:#eef0f3;color:#475467}
    .facts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}.facts>div{display:grid;align-content:start;gap:3px;min-width:0;padding:9px;border:1px solid #edf0f2;border-radius:9px;background:#fafbfc}.facts small,.facts span{color:var(--wb-text-secondary,#667085);font-size:.73rem;overflow-wrap:anywhere}.facts strong{font-size:.85rem;overflow-wrap:anywhere}.review-reason{color:#8a4b08!important}.items{padding:10px;border-radius:9px;background:#f8fafb}.items>strong,.address>strong{font-size:.8rem}.items ul{list-style:none;margin:6px 0 0;padding:0}.items li{display:flex;justify-content:space-between;gap:10px;padding:5px 0;border-top:1px solid #e8ecef;font-size:.84rem}.items li span{overflow-wrap:anywhere}.address{display:grid;gap:4px;margin:0;font-size:.8rem}.address span{color:var(--wb-text-secondary,#667085);overflow-wrap:anywhere}.events{border-top:1px solid #edf0f2;padding-top:10px}.events summary{cursor:pointer;font-size:.82rem;font-weight:700}.events ol{display:grid;gap:8px;max-height:220px;overflow:auto;margin:9px 0 0;padding-left:20px}.events li{padding-left:3px}.events li span{font-size:.8rem;font-weight:700}.events time,.events small{display:block;color:var(--wb-text-secondary,#667085);font-size:.72rem}.controls{display:flex;align-items:end;flex-wrap:wrap;gap:8px;padding-top:10px;border-top:1px solid #edf0f2}.controls button{background:#f9fafb}.controls button:disabled,.quiet:disabled{opacity:.5;cursor:not-allowed}.status-controls button{background:#fff}.proof,.cancel{display:grid;gap:8px;padding:11px;border:1px solid #d8e7dd;border-radius:9px;background:#fbfefc}.proof>strong,.cancel>strong{font-size:.85rem}.cancel{border-color:#f2d6c9;background:#fffaf8}.cancel p{margin:0;color:var(--wb-text-secondary,#667085);font-size:.78rem}.danger{border-color:#e6a89d!important;background:#fff4f2!important;color:#a3291d!important}.danger:hover:not(:disabled){background:#a3291d!important;color:#fff!important}.override{border-top:0;padding-top:0}
    @media(max-width:760px){.groups{grid-template-columns:1fr}.cod-panel{flex-direction:column}.cod-reassign{width:100%}.cod-reassign>*{flex:1 1 100%}}
    @media(max-width:430px){.manager{padding:11px}.manager-head{flex-direction:column}.quiet{align-self:flex-start}.group-card{padding:11px}.facts{grid-template-columns:1fr}.controls>*{flex:1 1 100%;min-width:0}.controls button{width:100%}.cod-reassign button{width:100%}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MarketplaceFulfilmentManagerComponent implements OnInit {
  @Input({ required: true }) parentOrder!: MarketplaceParentOrder;
  @Output() readonly closed = new EventEmitter<void>();
  readonly permissions = inject(PermissionService);
  private readonly api = inject(DeliveryApiService);
  readonly groups = signal<MarketplaceFulfilmentGroup[]>([]);
  readonly agents = signal<DeliveryAgent[]>([]);
  readonly settings = signal<DeliveryVerificationRequirements | null>(null);
  readonly loading = signal(true);
  readonly busyGroup = signal('');
  readonly error = signal('');
  readonly success = signal('');
  agentByGroup: Record<string, string> = {};
  assignmentReason: Record<string, string> = {};
  failureReason: Record<string, string> = {};
  trackingByGroup: Record<string, string> = {};
  otpByGroup: Record<string, string> = {};
  overrideReason: Record<string, string> = {};
  manualReason: Record<string, string> = {};
  cancelReason: Record<string, string> = {};
  restockByGroup: Record<string, string> = {};
  codTargetGroupId = '';
  codReason = '';

  async ngOnInit(): Promise<void> { await this.load(); }

  async load(): Promise<void> {
    this.loading.set(true); this.error.set('');
    try {
      const [groups, agents, settings] = await Promise.all([
        firstValueFrom(this.api.marketplaceGroups(this.parentOrder.id)),
        this.permissions.has('delivery.assign') ? firstValueFrom(this.api.agents()).catch(() => [] as DeliveryAgent[]) : Promise.resolve([] as DeliveryAgent[]),
        firstValueFrom(this.api.marketplaceVerificationRequirements()).catch(() => null),
      ]);
      this.groups.set(groups); this.agents.set(agents); this.settings.set(settings);
      for (const group of groups) this.agentByGroup[group.fulfilmentGroupId] = group.deliveryAgentId || '';
      const owner = groups.find(group => group.fulfilmentGroupId === group.codCollectionFulfilmentGroupId);
      this.codTargetGroupId = owner?.fulfilmentGroupId || '';
    } catch (error) { this.error.set(this.apiMessage(error, 'Unable to load marketplace fulfilment groups.')); }
    finally { this.loading.set(false); }
  }

  activeAgents(): DeliveryAgent[] { return this.agents().filter(agent => agent.isActive && agent.isAvailable); }
  trackGroup(_index: number, group: MarketplaceFulfilmentGroup): string { return group.fulfilmentGroupId; }
  label(value?: string | null): string { return (value || 'Not available').replaceAll('_', ' ').toLowerCase().replace(/\b\w/g, letter => letter.toUpperCase()); }
  parentStatus(): string { return this.groups()[0]?.parentDeliveryStatus || this.parentOrder.status; }
  parentCodStatus(): string { return this.groups()[0]?.parentCodStatus || 'NONE'; }
  codAmount(): number { return this.groups()[0]?.codExpectedAmount ?? this.parentOrder.total; }
  codOwner(): MarketplaceFulfilmentGroup | undefined { const id=this.groups()[0]?.codCollectionFulfilmentGroupId; return this.groups().find(group => group.fulfilmentGroupId === id); }
  sellerFallback(group: MarketplaceFulfilmentGroup): string { return group.sellerKind === 'PARTNER' ? `Partner retailer (${group.partnerSellerId || 'seller'})` : group.sellerKind === 'SUPPLIER' ? `Supplier (${group.supplierId || 'supplier'})` : 'GuturGo inventory'; }
  statusClass(status: string): string { return status === 'DELIVERED' ? 'good' : ['DELIVERY_FAILED','CANCELLED'].includes(status) ? 'attention' : ['PENDING','PREPARING','READY_FOR_DISPATCH','OUT_FOR_DELIVERY'].includes(status) ? 'progress' : 'closed'; }
  canAssign(group: MarketplaceFulfilmentGroup): boolean { return !['DELIVERED','CANCELLED'].includes(group.status); }
  nextStatuses(group: MarketplaceFulfilmentGroup): string[] {
    if (group.status === 'PENDING' || group.status === 'DELIVERY_FAILED') return ['PREPARING'];
    if (group.status === 'PREPARING') return ['READY_FOR_DISPATCH'];
    if (group.status === 'READY_FOR_DISPATCH') return ['OUT_FOR_DELIVERY'];
    return [];
  }
  transitionLabel(status: string): string { return ({PREPARING:'Start preparing',READY_FOR_DISPATCH:'Ready for dispatch',OUT_FOR_DELIVERY:'Dispatch delivery'} as Record<string,string>)[status] || this.label(status); }
  canRunTransition(group: MarketplaceFulfilmentGroup, target: string): boolean {
    if (target === 'OUT_FOR_DELIVERY') return !!group.deliveryAgentId && ['POSTED','PARTIALLY_FULFILLED'].includes(group.parentLifecycleStatus);
    return true;
  }
  canFail(group: MarketplaceFulfilmentGroup): boolean { return ['PREPARING','READY_FOR_DISPATCH','OUT_FOR_DELIVERY'].includes(group.status); }
  canCancel(group: MarketplaceFulfilmentGroup): boolean {
    if (!['PENDING','PREPARING','READY_FOR_DISPATCH','DELIVERY_FAILED'].includes(group.status)) return false;
    if (group.parentLifecycleStatus === 'CONFIRMED') return this.groups().length === 1;
    return ['POSTED','PARTIALLY_FULFILLED'].includes(group.parentLifecycleStatus);
  }
  requiresRestock(group: MarketplaceFulfilmentGroup): boolean { return ['POSTED','PARTIALLY_FULFILLED'].includes(group.parentLifecycleStatus); }
  isGuid(value?: string): boolean { return !!value && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value.trim()); }
  hasOtpRequest(group: MarketplaceFulfilmentGroup): boolean { return group.events.some(event => event.eventType === 'GROUP_OTP_REQUESTED'); }
  validOtp(group: MarketplaceFulfilmentGroup): boolean { return /^\d{4}$/.test(this.otpByGroup[group.fulfilmentGroupId] || ''); }
  canReassignCod(): boolean {
    const owner=this.codOwner();
    return this.permissions.has('delivery.manage') && this.parentCodStatus()==='PENDING' && !!owner && ['DELIVERY_FAILED','CANCELLED'].includes(owner.status) && this.codTargets().length>0;
  }
  canConfirmDelivery(): boolean { return this.parentOrder.paymentMethod.toUpperCase() !== 'COD' || ['COLLECTED','REFUND_DUE'].includes(this.parentCodStatus()); }
  codTargets(): MarketplaceFulfilmentGroup[] { return this.groups().filter(group => group.fulfilmentGroupId !== this.groups()[0]?.codCollectionFulfilmentGroupId && !['DELIVERED','CANCELLED'].includes(group.status) && !!group.deliveryAgentId); }

  async assign(group: MarketplaceFulfilmentGroup): Promise<void> {
    await this.run(group, 'Delivery agent assignment saved.', () => this.api.assignMarketplaceGroup(this.parentOrder.id,group.fulfilmentGroupId,this.agentByGroup[group.fulfilmentGroupId] || undefined,this.assignmentReason[group.fulfilmentGroupId]));
  }
  async transition(group: MarketplaceFulfilmentGroup, target: string): Promise<void> {
    const reason=target==='DELIVERY_FAILED'?this.failureReason[group.fulfilmentGroupId]?.trim():undefined;
    if (target==='DELIVERY_FAILED'&&!reason) { this.error.set('Enter a reason for the failed delivery attempt.'); return; }
    const tracking=target==='OUT_FOR_DELIVERY'?this.trackingByGroup[group.fulfilmentGroupId]?.trim():undefined;
    await this.run(group, `Delivery ${group.groupNumber}: ${this.label(target)}.`, () => this.api.transitionMarketplaceGroup(this.parentOrder.id,group.fulfilmentGroupId,target,reason,tracking));
  }
  async requestOtp(group: MarketplaceFulfilmentGroup): Promise<void> {
    await this.run(group, 'OTP request submitted for this delivery group. The code is not shown in ERP.', () => this.api.requestMarketplaceGroupOtp(this.parentOrder.id,group.fulfilmentGroupId));
  }
  async confirmOtp(group: MarketplaceFulfilmentGroup): Promise<void> {
    await this.run(group, `Delivery ${group.groupNumber} confirmed.`, () => this.api.confirmMarketplaceGroupOtp(this.parentOrder.id,group.fulfilmentGroupId,this.otpByGroup[group.fulfilmentGroupId]));
  }
  async confirmWithoutOtp(group: MarketplaceFulfilmentGroup): Promise<void> {
    const reason=this.manualReason[group.fulfilmentGroupId]?.trim();if(!reason){this.error.set('Enter a confirmation reason.');return;}
    await this.run(group, `Delivery ${group.groupNumber} confirmed.`, () => this.api.confirmMarketplaceGroup(this.parentOrder.id,group.fulfilmentGroupId,reason));
  }
  async overrideDelivery(group: MarketplaceFulfilmentGroup): Promise<void> {
    const reason=this.overrideReason[group.fulfilmentGroupId]?.trim();if(!reason){this.error.set('Enter an audited override reason.');return;}
    if(!window.confirm(`Confirm Delivery ${group.groupNumber} without OTP? This override will be audited.`))return;
    await this.run(group, `Delivery ${group.groupNumber} confirmed with an audited override.`, () => this.api.overrideMarketplaceGroup(this.parentOrder.id,group.fulfilmentGroupId,reason));
  }
  async cancelGroup(group: MarketplaceFulfilmentGroup): Promise<void> {
    const reason=this.cancelReason[group.fulfilmentGroupId]?.trim();const restock=this.restockByGroup[group.fulfilmentGroupId]?.trim();
    if(!reason){this.error.set('Enter a cancellation reason.');return;}
    if(this.requiresRestock(group)&&!this.isGuid(restock)){this.error.set('Enter the approved stock-return transaction ID.');return;}
    if(!window.confirm(`Cancel Delivery ${group.groupNumber} for ${this.parentOrder.orderNumber}? Inventory reconciliation rules will be enforced.`))return;
    this.busyGroup.set(group.fulfilmentGroupId);this.error.set('');this.success.set('');
    try{const result=await firstValueFrom(this.api.cancelMarketplaceGroup(this.parentOrder.id,group.fulfilmentGroupId,reason,restock));this.success.set(`Delivery ${group.groupNumber} cancelled. Refund state: ${this.label(result.refundStatus)}${result.refundAmount!=null?` · ${result.refundAmount.toFixed(2)}`:''}${result.refundReviewReason?` · Review: ${result.refundReviewReason}`:''}`);await this.load();}
    catch(error){this.error.set(this.apiMessage(error,'Cancellation was not completed. Refresh and review the backend response.'));}
    finally{this.busyGroup.set('');}
  }
  async reassignCod(): Promise<void> {
    if(!this.codTargetGroupId||!this.codReason.trim()){this.error.set('Choose an eligible delivery and enter a reason.');return;}
    this.busyGroup.set('cod');this.error.set('');this.success.set('');
    try{await firstValueFrom(this.api.assignMarketplaceCod(this.parentOrder.id,this.codTargetGroupId,this.codReason.trim(),crypto.randomUUID()));this.success.set('Parent COD collection responsibility reassigned. No collection was recorded.');this.codReason='';await this.load();}
    catch(error){this.error.set(this.apiMessage(error,'COD responsibility was not reassigned. Refresh and review the backend response.'));}
    finally{this.busyGroup.set('');}
  }
  private async run(group: MarketplaceFulfilmentGroup, message: string, action: () => import('rxjs').Observable<unknown>): Promise<void> {
    if(this.busyGroup())return;this.busyGroup.set(group.fulfilmentGroupId);this.error.set('');this.success.set('');
    try{await firstValueFrom(action());this.success.set(message);await this.load();}
    catch(error){this.error.set(this.apiMessage(error,'The fulfilment operation could not be completed. Refresh and review the backend response.'));}
    finally{this.busyGroup.set('');}
  }
  private apiMessage(error: unknown, fallback: string): string {
    const body=(error as {error?:{message?:string;detail?:string;title?:string}})?.error;
    return body?.message||body?.detail||body?.title||fallback;
  }
}
