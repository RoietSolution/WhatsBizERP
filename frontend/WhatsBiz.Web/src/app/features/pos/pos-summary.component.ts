import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
@Component({
  selector: 'app-pos-summary',
  imports: [CurrencyPipe, FormsModule, MatButtonModule],
  templateUrl: './pos-summary.component.html',
  styleUrl: './pos-summary.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PosSummaryComponent {
  readonly subtotal = input(0);
  readonly itemDiscount = input(0);
  readonly tax = input(0);
  readonly total = input(0);
  readonly itemCount = input(0);
  readonly billDiscount = input(0);
  readonly selectedMethod = input('CASH');
  readonly billDiscountChange = output<number>();
  readonly methodChange = output<string>();
  readonly quickAmount = output<number>();
  readonly pay = output<void>();
  readonly methods = [
    { code: 'CASH', label: 'Cash', icon: 'payments' },
    { code: 'UPI', label: 'UPI', icon: 'qr_code_2' },
    { code: 'SPLIT', label: 'Split Payment', icon: 'call_split' },
  ];
}
