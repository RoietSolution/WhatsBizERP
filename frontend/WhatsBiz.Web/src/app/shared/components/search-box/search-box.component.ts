import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-search-box',
  imports: [FormsModule],
  templateUrl: './search-box.component.html',
  styleUrl: './search-box.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchBoxComponent {
  readonly placeholder = input('Search');
  readonly ariaLabel = input('Search');
  readonly searchChange = output<string>();
  readonly value = signal('');
  clear(): void {
    this.value.set('');
    this.searchChange.emit('');
  }
}
