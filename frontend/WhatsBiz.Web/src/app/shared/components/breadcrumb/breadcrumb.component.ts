import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, startWith } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';

interface Crumb { label: string; route: string; }
@Component({ selector: 'app-breadcrumb', imports: [RouterLink], template: '<nav aria-label="Breadcrumb"><ol><li><a routerLink="/dashboard" aria-label="Home"><span class="material-symbols-rounded">home</span></a></li>@for (crumb of crumbs(); track crumb.route; let last = $last) { <li><span class="material-symbols-rounded separator">chevron_right</span>@if (last) { <span aria-current="page">{{ crumb.label }}</span> } @else { <a [routerLink]="crumb.route">{{ crumb.label }}</a> }</li> }</ol></nav>', styleUrl: './breadcrumb.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class BreadcrumbComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly navigation = toSignal(this.router.events.pipe(filter((event) => event instanceof NavigationEnd), startWith(null)));
  readonly crumbs = computed<Crumb[]>(() => { this.navigation(); const parts = this.router.url.split('?')[0].split('/').filter(Boolean); let path = ''; return parts.map((part) => { path += `/${part}`; let active = this.route.root; for (const segment of parts.slice(0, parts.indexOf(part) + 1)) active = active.firstChild ?? active; const title = active.snapshot.data['title'] as string | undefined; return { route: path, label: title ?? part.replace(/-/g, ' ').replace(/\b\w/g, (char) => char.toUpperCase()) }; }); });
}
