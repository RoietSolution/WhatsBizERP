import { Directive, TemplateRef, ViewContainerRef, effect, input } from '@angular/core';
import { PermissionService } from '../../core/services/permission.service';

@Directive({ selector: '[appHasPermission]' })
export class HasPermissionDirective {
  readonly appHasPermission = input.required<string>();
  private visible = false;

  constructor(
    template: TemplateRef<unknown>,
    container: ViewContainerRef,
    permissions: PermissionService,
  ) {
    effect(() => {
      const next = permissions.has(this.appHasPermission());
      if (next === this.visible) return;
      this.visible = next;
      container.clear();
      if (next) container.createEmbeddedView(template);
    });
  }
}
