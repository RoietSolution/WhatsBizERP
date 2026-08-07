import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-required-label',
  template: '<span>{{ label() }} @if (required()) { <span class="wb-required-marker" aria-hidden="true">*</span><span class="sr-only"> required</span> }</span>',
  styles: [':host{display:inline}.sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequiredLabelComponent { readonly label = input.required<string>(); readonly required = input(true); }
