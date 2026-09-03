import { MatSnackBar } from '@angular/material/snack-bar';
import { POSApiService } from './pos-api.service';
import { ReturnScreenComponent } from './return-screen.component';

describe('ReturnScreenComponent', () => {
  it('shows required validation and does not call the API for a blank invoice number', () => {
    const api = jasmine.createSpyObj<POSApiService>('POSApiService', ['get', 'invoices', 'return']);
    const snack = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    const component = new ReturnScreenComponent(api, snack);

    component.invoiceId = '   ';
    component.load();

    expect(component.invoiceAttempted()).toBeTrue();
    expect(component.invoice()).toBeNull();
    expect(api.get).not.toHaveBeenCalled();
    expect(api.invoices).not.toHaveBeenCalled();
    expect(snack.open).toHaveBeenCalledWith('Invoice Number is required.', undefined, {
      duration: 5000,
    });
  });
});
