import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { CustomerApiService } from './customer-api.service';
import { Customer } from './customer.models';
@Component({
  imports: [RouterLink, MatButtonModule, MatTabsModule],
  templateUrl: './customer-view.component.html',
  styles: [
    `
      header {
        display: flex;
        justify-content: space-between;
      }
      mat-tab-group {
        margin-top: 1rem;
      }
      dl {
        display: grid;
        grid-template-columns: 150px 1fr;
        gap: 1rem;
        padding: 1rem;
      }
      dd {
        margin: 0;
      }
      .upload {
        display: flex;
        gap: 1rem;
        padding: 1rem;
        flex-wrap: wrap;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomerViewComponent {
  readonly customer = signal<Customer | null>(null);
  private readonly id: string;
  constructor(
    private readonly api: CustomerApiService,
    route: ActivatedRoute,
    private readonly snack: MatSnackBar,
  ) {
    this.id = route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }
  load() {
    this.api.get(this.id).subscribe((x) => this.customer.set(x));
  }
  upload(type: string, input: HTMLInputElement) {
    const f = input.files?.[0];
    if (f)
      this.api.upload(this.id, type, f).subscribe(() => {
        input.value = '';
        this.snack.open('Document uploaded.', undefined, { duration: 2500 });
        this.load();
      });
  }
  replace(type: string, input: HTMLInputElement, did: string) {
    const f = input.files?.[0];
    if (f) this.api.deleteDocument(this.id, did).subscribe(() => this.upload(type, input));
  }
  preview(did: string) {
    this.api.document(this.id, did).subscribe((b) => {
      const u = URL.createObjectURL(b);
      window.open(u, '_blank');
      setTimeout(() => URL.revokeObjectURL(u), 60000);
    });
  }
  remove(did: string) {
    this.api.deleteDocument(this.id, did).subscribe(() => {
      this.snack.open('Document deleted.', undefined, { duration: 2500 });
      this.load();
    });
  }
}
