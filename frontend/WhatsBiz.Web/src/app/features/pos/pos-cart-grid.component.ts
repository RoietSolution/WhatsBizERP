import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { CartItem } from './pos.models';
@Component({
  selector: 'app-pos-cart-grid',
  imports: [CurrencyPipe, FormsModule, MatButtonModule],
  templateUrl: './pos-cart-grid.component.html',
  styleUrl: './pos-cart-grid.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PosCartGridComponent {
  readonly items = input<CartItem[]>([]);
  readonly images = input<Record<string, string>>({});
  readonly changed = output<void>();
  readonly remove = output<CartItem>();
  lineTotal(x: CartItem) {
    const base = x.quantity * x.unitPrice * (1 - x.discountPercentage / 100);
    return base * (1 + x.taxPercentage / 100);
  }
}
