import { of } from 'rxjs';
import { POSApiService } from './pos-api.service';
import { HoldBillsComponent } from './hold-bills.component';
import { InvoiceList } from './pos.models';

describe('HoldBillsComponent provisional printing', () => {
  it('prints a held bill through the existing Android-aware bridge flow without changing it', () => {
    const api = jasmine.createSpyObj<POSApiService>('POSApiService', [
      'invoices',
      'methods',
      'printBridge',
      'completeHeld',
      'payment',
      'cancelHeld',
    ]);
    api.invoices.and.returnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20 }));
    api.methods.and.returnValue(of([]));
    const component = new HoldBillsComponent(
      api,
      jasmine.createSpyObj('MatDialog', ['open']),
      jasmine.createSpyObj('MatSnackBar', ['open']),
    );
    const bill: InvoiceList = {
      invoiceId: 'held-1',
      invoiceNumber: 'INV-HELD-1',
      invoiceDate: '2026-09-25T10:30:00+05:30',
      grandTotal: 118,
      paidAmount: 0,
      balanceAmount: 118,
      status: 'HELD',
    };

    component.print(bill);

    expect(api.printBridge).toHaveBeenCalledOnceWith('held-1');
    expect(api.completeHeld).not.toHaveBeenCalled();
    expect(api.payment).not.toHaveBeenCalled();
  });
});
