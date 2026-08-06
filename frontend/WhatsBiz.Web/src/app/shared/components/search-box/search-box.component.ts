import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({ selector: 'app-search-box', imports: [FormsModule], template: '<label><span class="material-symbols-rounded">search</span><span class="sr-only">{{ ariaLabel() }}</span><input type="search" [placeholder]="placeholder()" [attr.aria-label]="ariaLabel()" [ngModel]="value()" (ngModelChange)="value.set($event); searchChange.emit($event)">@if (value()) { <button type="button" aria-label="Clear search" (click)="clear()"><span class="material-symbols-rounded">close</span></button> }</label>', styleUrl: './search-box.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class SearchBoxComponent { readonly placeholder = input('Search'); readonly ariaLabel = input('Search'); readonly searchChange = output<string>(); readonly value = signal(''); clear(): void { this.value.set(''); this.searchChange.emit(''); } }
