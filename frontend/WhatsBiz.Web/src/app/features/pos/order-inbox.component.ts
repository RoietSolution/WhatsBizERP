import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IntakeAddressForm, IntakeCustomerOption, IntakeDetail, IntakeProductMatch, IntakeQuote, IntakeSalesOrder, IntakeSavedAddress, IntakeSummary, OrderIntakeApiService } from './order-intake-api.service';

@Component({
  selector: 'app-order-inbox', standalone: true, imports: [FormsModule, DatePipe, DecimalPipe],
  templateUrl: './order-inbox.component.html', styleUrl: './order-inbox.component.scss',
})
export class OrderInboxComponent implements OnInit {
  private selectedRequestEpoch = 0;
  private lifecycleEpoch = 0;
  readonly rows = signal<IntakeSummary[]>([]); readonly selected = signal<IntakeDetail | null>(null);
  readonly quote = signal<IntakeQuote | null>(null); readonly salesOrder = signal<IntakeSalesOrder | null>(null);
  readonly quoteState = signal<'idle' | 'loading' | 'available' | 'not-generated' | 'invalidated' | 'error'>('idle');
  readonly salesOrderState = signal<'idle' | 'loading' | 'available' | 'not-created' | 'not-checked' | 'error'>('idle');
  readonly quoteError = signal(''); readonly salesOrderError = signal('');
  readonly matches = signal<IntakeProductMatch[]>([]); readonly loading = signal(false); readonly saving = signal(false);
  readonly error = signal(''); readonly detailError = signal(''); readonly total = signal(0);
  readonly confirmationFeedback = signal('');
  readonly savedAddresses = signal<IntakeSavedAddress[]>([]); readonly addressesLoading = signal(false); readonly addressesError = signal('');
  readonly customerMatches = signal<IntakeCustomerOption[]>([]); readonly customerSearchLoading = signal(false); readonly customerLinking = signal(false);
  customerSearch = ''; selectedCustomerId = ''; customerVerificationMethod = 'CALLBACK'; customerIdentityAcknowledged = false;
  readonly rejectOpen = signal(false); rejectNote = '';
  search = ''; channel = ''; status = ''; from = ''; to = ''; page = 1; readonly pageSize = 25;
  createOpen = false; sourceChannel = 'MANUAL_LIST'; customerName = ''; customerMobile = '';
  originalRequest = ''; notes = ''; fulfilmentPreference = '';
  matchQuery = ''; itemQueryId = ''; selectedMatchId = ''; editQuantity: number | null = null; editUnit = ''; unavailable = false; retailerConfirmedMatch = false; proposedSubstitution = false;
  quotePromoCode = ''; confirmationChannel = 'PHONE_CALL'; confirmationVerification = 'CALLBACK'; confirmationEvidence = ''; confirmationAcknowledged = false;
  newAddress: IntakeAddressForm = this.emptyAddress(''); deliveryAddress: IntakeAddressForm = this.emptyAddress(''); selectedSavedAddressId = '';
  constructor(private readonly api: OrderIntakeApiService) {}
  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true); this.error.set('');
    this.api.list({ search: this.search, channel: this.channel, status: this.status, from: this.from || undefined,
      to: this.to ? new Date(new Date(this.to).getTime() + 86400000).toISOString() : undefined, page: this.page, pageSize: this.pageSize })
      .subscribe({ next: result => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
        error: err => { this.error.set(this.message(err)); this.loading.set(false); } });
  }
  clearFilters(): void { this.search = this.channel = this.status = this.from = this.to = ''; this.page = 1; this.load(); }
  open(row: IntakeSummary): void {
    const epoch = ++this.selectedRequestEpoch; ++this.lifecycleEpoch;
    this.selected.set(null); this.quote.set(null); this.salesOrder.set(null); this.detailError.set('');
    this.quoteState.set('idle'); this.salesOrderState.set('idle'); this.quoteError.set(''); this.salesOrderError.set('');
    this.savedAddresses.set([]); this.addressesError.set(''); this.api.get(row.id).subscribe({ next: value => {
      if (epoch !== this.selectedRequestEpoch) return;
      this.customerMatches.set([]); this.customerSearch = ''; this.selectedCustomerId = ''; this.customerIdentityAcknowledged = false;
      this.selected.set(value); this.loadLifecycle(value.id);
      this.deliveryAddress = { ...this.emptyAddress(value.fulfilmentPreference || ''), ...(value.deliveryAddress ?? {}) };
      this.selectedSavedAddressId = value.deliveryAddress?.savedAddressId ?? '';
      this.addressesLoading.set(true); this.api.savedAddresses(value.id).subscribe({ next: list => { if(epoch!==this.selectedRequestEpoch)return; this.savedAddresses.set(list); this.addressesLoading.set(false); },
        error: e => { if(epoch!==this.selectedRequestEpoch)return; this.savedAddresses.set([]); this.addressesLoading.set(false); this.addressesError.set(this.message(e)); } });
    }, error: err => { if(epoch===this.selectedRequestEpoch)this.detailError.set(this.message(err)); } });
  }
  private loadLifecycle(id: string): void {
    if (this.selected()?.id !== id) return;
    const epoch = ++this.lifecycleEpoch;
    const current = (): boolean => epoch === this.lifecycleEpoch && this.selected()?.id === id;
    this.quote.set(null); this.salesOrder.set(null); this.quoteError.set(''); this.salesOrderError.set('');
    this.quoteState.set('loading'); this.salesOrderState.set('idle');
    const loadOrder = (): void => {
      if (!current()) return;
      this.salesOrderState.set('loading');
      this.api.getSalesOrder(id).subscribe({
        next: order => { if (!current()) return; this.salesOrder.set(order); this.salesOrderState.set('available'); },
        error: error => { if (!current()) return;
          if (error instanceof HttpErrorResponse && error.status === 404) { this.salesOrderState.set('not-created'); return; }
          this.salesOrderError.set(this.lifecycleError('Sales Order', error)); this.salesOrderState.set('error');
        },
      });
    };
    this.api.getQuote(id).subscribe({
      next: quote => { if (!current()) return; this.quote.set(quote); this.quoteState.set('available'); loadOrder(); },
      error: error => {
        if (!current()) return;
        if (error instanceof HttpErrorResponse && error.status === 404) { this.quoteState.set('not-generated'); loadOrder(); return; }
        this.quoteError.set(this.lifecycleError('Quotation', error)); this.quoteState.set('error');
        if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
          this.salesOrderState.set('not-checked');
          this.salesOrderError.set('Sales Order was not requested after the quotation authorization failure. Sign in again or ask an authorized user to retry.');
          return;
        }
        loadOrder();
      },
    });
  }
  retryLifecycle(id: string): void { if(this.selected()?.id===id)this.loadLifecycle(id); }
  private refreshDetail(id: string): void {
    if (this.selected()?.id !== id) return; const epoch = this.selectedRequestEpoch;
    this.api.get(id).subscribe({ next: x => { if(epoch!==this.selectedRequestEpoch || this.selected()?.id!==id)return; this.selected.set(x); this.loadLifecycle(id); this.load(); },
      error: e => { if(epoch===this.selectedRequestEpoch && this.selected()?.id===id)this.detailError.set(this.message(e)); } });
  }
  create(): void {
    if (this.saving() || !this.originalRequest.trim()) return;
    if (this.fulfilmentPreference === 'DELIVERY' && !this.validAddress(this.newAddress)) { this.error.set('Complete the required delivery address fields and enter a six-digit PIN code.'); return; }
    this.saving.set(true); this.error.set('');
    this.api.create({ sourceChannel: this.sourceChannel, customerName: this.newAddress.recipientName || this.customerName,
      customerMobile: this.newAddress.mobile || this.customerMobile,
      originalRequest: this.originalRequest, notes: this.notes, fulfilmentPreference: this.fulfilmentPreference || undefined,
      deliveryAddress: this.fulfilmentPreference === 'DELIVERY' ? this.newAddress : null })
      .subscribe({ next: result => { this.saving.set(false); this.createOpen = false; this.resetCreate(); this.page = 1; this.load(); this.open(result); },
        error: err => { this.saving.set(false); this.error.set(this.message(err)); } });
  }
  assign(): void { const item = this.selected(); if (!item) return; this.saving.set(true); this.api.assign(item.id, item.version).subscribe({ next: x => { this.selected.set(x); this.saving.set(false); this.load(); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } }); }
  findMatches(item: IntakeDetail['items'][number]): void { this.itemQueryId = item.id; this.matchQuery = item.requestedText; this.matches.set([]); this.selectedMatchId = item.matchedProductId ?? ''; this.editQuantity = item.requestedQuantity ?? null; this.editUnit = item.requestedUnit ?? ''; this.unavailable = item.matchStatus === 'UNAVAILABLE'; this.retailerConfirmedMatch = item.matchStatus === 'MATCHED'; this.proposedSubstitution = item.proposedSubstitution; this.api.matches(this.matchQuery).subscribe({ next: x => this.matches.set(x), error: e => this.detailError.set(this.message(e)) }); }
  searchMatches(): void { if (this.matchQuery.trim().length < 2) return; this.api.matches(this.matchQuery.trim()).subscribe({ next: x => this.matches.set(x), error: e => this.detailError.set(this.message(e)) }); }
  saveMatch(item: IntakeDetail['items'][number]): void {
    const current = this.selected(); if (!current) return; this.saving.set(true);
    this.api.updateItem(current.id, item.id, { matchedProductId: this.unavailable ? null : this.selectedMatchId || null, quantity: this.editQuantity,
      unit: this.editUnit || null, notes: item.notes ?? null, matchStatus: this.unavailable ? 'UNAVAILABLE' : null,
      retailerConfirmedMatch: !this.unavailable && this.retailerConfirmedMatch,
      proposedSubstitution: !this.unavailable && this.proposedSubstitution, version: current.version })
      .subscribe({ next: x => { this.selected.set(x); this.itemQueryId = ''; this.saving.set(false); this.load(); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } });
  }
  changeStatus(next: string, note = '', confirmationChannel?: string): void {
    const current = this.selected(); if (!current) return; this.saving.set(true); this.detailError.set('');
    this.api.setStatus(current.id, { status: next, note, confirmationChannel, version: current.version })
      .subscribe({ next: x => { this.selected.set(x); this.loadLifecycle(x.id); this.saving.set(false); if (next === 'REJECTED') { this.rejectOpen.set(false); this.rejectNote = ''; } this.load(); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } });
  }
  reject(): void { if (this.rejectNote.trim()) this.changeStatus('REJECTED', this.rejectNote.trim()); }
  saveDeliveryDetails(): void {
    const d = this.selected(); if (!d) return;
    if (this.deliveryAddress.fulfilmentPreference === 'DELIVERY' && !this.validAddress(this.deliveryAddress)) { this.detailError.set('Complete the required delivery address fields and enter a six-digit PIN code.'); return; }
    this.saving.set(true); this.detailError.set('');
    this.api.saveDeliveryDetails(d.id, { details: this.deliveryAddress, version: d.version }).subscribe({
      next: x => {
        this.saving.set(false); if(this.selected()?.id!==d.id)return;
        this.selected.set(x); this.deliveryAddress = { ...this.emptyAddress(x.fulfilmentPreference || ''), ...(x.deliveryAddress ?? {}) };
        this.selectedSavedAddressId = x.deliveryAddress?.savedAddressId ?? '';
        this.quote.set(null); this.quoteState.set('invalidated'); this.quoteError.set('');
        this.load();
      },
      error: e => { this.saving.set(false); if(this.selected()?.id===d.id)this.detailError.set(this.message(e)); }
    });
  }
  selectSavedAddress(id: string): void {
    this.selectedSavedAddressId = id;
    if (!id) { this.deliveryAddress = { ...this.deliveryAddress, savedAddressId: null }; return; }
    const a = this.savedAddresses().find(x => x.addressId === id); if (!a) return;
    this.deliveryAddress = { ...this.deliveryAddress, savedAddressId: id, recipientName: a.recipientName, mobile: a.mobile,
      houseStreet: a.addressLine1, locality: a.addressLine2 || '', city: a.city, state: a.state, pincode: a.pincode, landmark: a.landmark || '' };
  }
  retrySavedAddresses(id: string): void {
    this.addressesError.set(''); this.addressesLoading.set(true);
    this.api.savedAddresses(id).subscribe({ next: list => { this.savedAddresses.set(list); this.addressesLoading.set(false); },
      error: e => { this.savedAddresses.set([]); this.addressesLoading.set(false); if (e instanceof HttpErrorResponse && e.status !== 403) this.addressesError.set('Unable to load linked customer addresses.'); } });
  }
  searchCustomers(): void {
    const query = this.customerSearch.trim(); if (query.length < 2) return;
    this.customerSearchLoading.set(true); this.detailError.set('');
    this.api.searchCustomers(query).subscribe({ next: rows => { this.customerMatches.set(rows); this.customerSearchLoading.set(false); },
      error: e => { this.customerMatches.set([]); this.customerSearchLoading.set(false); this.detailError.set(this.message(e)); } });
  }
  linkVerifiedCustomer(): void {
    const d = this.selected(); if (!d || !this.selectedCustomerId || !this.customerIdentityAcknowledged) return;
    this.customerLinking.set(true); this.detailError.set('');
    this.api.linkVerifiedCustomer(d.id, { customerId: this.selectedCustomerId, verificationMethod: this.customerVerificationMethod,
      staffAcknowledged: true, version: d.version }).subscribe({ next: x => {
        this.selected.set(x); this.deliveryAddress = { ...this.emptyAddress(x.fulfilmentPreference || ''), ...(x.deliveryAddress ?? {}) };
        this.selectedSavedAddressId = ''; this.customerLinking.set(false); this.retrySavedAddresses(x.id); this.loadLifecycle(x.id); this.load();
      }, error: e => { this.customerLinking.set(false); this.detailError.set(this.message(e)); } });
  }
  generateQuote(): void { const d = this.selected(); if (!d) return;
    if (d.fulfilmentPreference === 'DELIVERY' && !d.deliveryAddress) { this.detailError.set('Save a valid delivery address before generating the quote.'); return; }
    this.saving.set(true); this.detailError.set('');
    this.api.createQuote(d.id, { promotionCode: this.quotePromoCode || null, validityMinutes: 30, version: d.version })
      .subscribe({ next: q => { this.quote.set(q); this.saving.set(false); this.refreshDetail(d.id); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } }); }
  markConfirmationRequested(): void { const d = this.selected(), q = this.quote(); if (!d || !q) return; this.saving.set(true);
    this.api.markConfirmationRequested(d.id, { quoteRevision: q.revision, channel: this.confirmationChannel, version: d.version })
      .subscribe({ next: x => { this.selected.set(x); this.saving.set(false); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } }); }
  recordConfirmation(): void { const d = this.selected(), q = this.quote(); if (!d || !q) return; this.saving.set(true);
    this.api.recordConfirmation(d.id, { quoteRevision: q.revision, channel: this.confirmationChannel, verificationMethod: this.confirmationVerification,
      evidenceReference: this.confirmationEvidence || null, staffAcknowledged: this.confirmationAcknowledged, version: d.version })
      .subscribe({ next: x => { this.selected.set(x); this.saving.set(false); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } }); }
  convertToSalesOrder(): void { const d = this.selected(); if (!d) return; this.saving.set(true);
    this.api.convertToSalesOrder(d.id, { version: d.version }).subscribe({ next: x => { this.salesOrder.set(x); this.saving.set(false); this.refreshDetail(d.id); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } }); }
  changeSalesOrderStatus(status: string): void { const order = this.salesOrder(); if (!order) return; this.saving.set(true);
    this.api.transitionSalesOrder(order.requestId, { status, version: order.version }).subscribe({ next: x => { this.salesOrder.set(x); this.saving.set(false); }, error: e => { this.saving.set(false); this.detailError.set(this.message(e)); } }); }
  quoteCanProceed(quote: IntakeQuote): boolean { return quote.status === 'CURRENT' && Date.parse(quote.expiresAtUtc) > Date.now(); }
  toggleProposedSubstitution(): void { if (this.proposedSubstitution) this.retailerConfirmedMatch = false; }
  onMatchSelectionChanged(): void { this.retailerConfirmedMatch = false; this.proposedSubstitution = false; this.unavailable = false; }
  confirmationSummary(): string {
    const q = this.quote(); if (!q) return '';
    const lines = q.items.map(item => `${item.isSubstitution ? `Substitute for ${item.requestedText}: ` : ''}${item.productName} × ${item.quantity} ${item.unitShortName || item.unitName} — ₹${(item.unitPrice * item.quantity).toFixed(2)}`);
    const address = q.fulfilmentPreference === 'DELIVERY' && q.deliveryAddress ? [`Deliver to: ${this.formatAddress(q.deliveryAddress)}`, `Recipient: ${q.deliveryAddress.recipientName} · ${q.deliveryAddress.mobile}`] : [];
    return [`Order summary (${q.fulfilmentPreference === 'DELIVERY' ? 'home delivery' : 'store pickup'})`, ...address, ...lines,
      `GST: ₹${q.pricing.merchandiseTaxAmount.toFixed(2)}`, `Discount: ₹${q.pricing.promotionDiscount.toFixed(2)}`,
      `Delivery: ₹${q.pricing.deliveryCharge.toFixed(2)}`, `Total payable: ₹${q.pricing.finalPayableAmount.toFixed(2)}`,
      `Please confirm these items and the total (quotation ${q.revision}).`].join('\n');
  }
  formatAddress(a: NonNullable<IntakeQuote['deliveryAddress']>): string {
    return [a.houseStreet, a.locality, a.landmark, a.city, a.state, a.pincode].filter(Boolean).join(', ');
  }
  private emptyAddress(fulfilmentPreference: string): IntakeAddressForm {
    return { fulfilmentPreference, recipientName: '', mobile: '', houseStreet: '', locality: '', city: '', state: '', pincode: '', landmark: '', deliveryInstructions: '' };
  }
  private validAddress(a: IntakeAddressForm): boolean {
    return !!(a.recipientName.trim() && a.mobile.trim() && a.houseStreet.trim() && a.locality.trim() && a.city.trim()
      && a.state.trim() && /^[0-9]{6}$/.test(a.pincode.trim()));
  }
  async copyConfirmationSummary(): Promise<void> {
    this.confirmationFeedback.set('');
    try { await navigator.clipboard.writeText(this.confirmationSummary()); this.confirmationFeedback.set('Summary copied. Share it with the customer using an authorized channel.'); }
    catch { this.confirmationFeedback.set('Clipboard access is unavailable. You can select and copy the displayed summary.'); }
  }
  pageBy(delta: number): void { this.page = Math.max(1, this.page + delta); this.load(); }
  private resetCreate(): void { this.customerName = this.customerMobile = this.originalRequest = this.notes = this.fulfilmentPreference = ''; this.newAddress = this.emptyAddress(''); this.sourceChannel = 'MANUAL_LIST'; }
  private message(error: unknown): string {
    if (error instanceof HttpErrorResponse) return typeof error.error?.message === 'string' ? error.error.message : 'Unable to complete the request. Retry or contact an administrator.';
    return 'Unable to complete the request. Retry.';
  }
  private lifecycleError(resource: 'Quotation' | 'Sales Order', error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) return `${resource} could not be loaded. Retry.`;
    if (error.status === 401) return `${resource} request was unauthorized (HTTP 401). Your session may have expired; sign in again and retry.`;
    if (error.status === 403) return `${resource} request was forbidden (HTTP 403). Your account does not have access to this resource.`;
    if (error.status === 0) return `Network failure while loading ${resource.toLowerCase()} (HTTP 0). Check connectivity and retry.`;
    return `${resource} could not be loaded (HTTP ${error.status}). Retry or contact an administrator.`;
  }
}
