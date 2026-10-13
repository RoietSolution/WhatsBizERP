import { ChangeDetectionStrategy, Component, ElementRef, forwardRef, Input, ViewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'app-rich-text-editor',
  standalone: true,
  template: `
    <div class="editor" [class.disabled]="disabled">
      <div class="toolbar" role="toolbar" [attr.aria-label]="label + ' formatting'">
        @for (action of actions; track action.command) { <button type="button" [title]="action.title" [disabled]="disabled" (mousedown)="$event.preventDefault()" (click)="format(action.command, action.value)">{{ action.label }}</button> }
      </div>
      <div #surface class="surface" contenteditable="true" role="textbox" [attr.aria-label]="label" [attr.aria-multiline]="true" [attr.data-placeholder]="placeholder" (input)="changed()" (blur)="touched()"></div>
    </div>
  `,
  styles: [`.editor{border:1px solid var(--mat-sys-outline);border-radius:4px;overflow:hidden;background:#fff}.toolbar{display:flex;flex-wrap:wrap;gap:4px;padding:6px;border-bottom:1px solid var(--mat-sys-outline-variant);background:#f7f8fa}.toolbar button{min-width:30px;border:1px solid #d9dee5;border-radius:4px;background:#fff;cursor:pointer;font-weight:700}.surface{min-height:92px;padding:10px;outline:0;line-height:1.5}.surface:empty:before{content:attr(data-placeholder);color:#7b8390}.disabled{opacity:.6}.disabled .surface{background:#f4f4f4}`],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => RichTextEditorComponent), multi: true }],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RichTextEditorComponent implements ControlValueAccessor {
  @Input() label = 'Rich text';
  @Input() placeholder = 'Enter formatted text';
  @ViewChild('surface') surface?: ElementRef<HTMLElement>;
  disabled = false;
  readonly actions: { command: string; value?: string; label: string; title: string }[] = [
    { command: 'bold', label: 'B', title: 'Bold' }, { command: 'italic', label: 'I', title: 'Italic' },
    { command: 'formatBlock', value: 'h3', label: 'H', title: 'Heading' }, { command: 'insertUnorderedList', label: '•', title: 'Bullet list' },
    { command: 'insertOrderedList', label: '1.', title: 'Numbered list' }, { command: 'createLink', label: 'Link', title: 'Insert link' },
    { command: 'insertTable', label: 'Table', title: 'Insert table' },
  ];
  private onChange = (_: string) => {};
  private onTouched = () => {};
  writeValue(value: string | null): void { if (this.surface) this.surface.nativeElement.innerHTML = value ?? ''; else queueMicrotask(() => this.writeValue(value)); }
  registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(disabled: boolean): void { this.disabled = disabled; }
  changed(): void { this.onChange(this.surface?.nativeElement.innerHTML ?? ''); }
  touched(): void { this.onTouched(); }
  format(command: string, value?: string): void {
    if (command === 'createLink') { const url = window.prompt('Link URL'); if (!url) return; value = url; }
    if (command === 'insertTable') { document.execCommand('insertHTML', false, '<table><tbody><tr><td>Header</td><td>Value</td></tr></tbody></table>'); this.changed(); return; }
    document.execCommand(command, false, value); this.changed();
  }
}
