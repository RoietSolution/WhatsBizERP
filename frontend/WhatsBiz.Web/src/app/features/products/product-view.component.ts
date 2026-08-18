import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { Product } from './product.models';

@Component({
  selector: 'app-product-view',
  imports: [
    CurrencyPipe,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './product-view.component.html',
  styles: [
    `
      header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 1rem;
      }
      .layout {
        display: grid;
        grid-template-columns: minmax(220px, 1fr) 2fr;
        gap: 1rem;
        margin-bottom: 1rem;
      }
      .image {
        display: grid;
        place-items: center;
        min-height: 280px;
      }
      .image img {
        max-width: 100%;
        max-height: 320px;
        object-fit: contain;
      }
      dl {
        display: grid;
        grid-template-columns: 150px 1fr;
        gap: 0.75rem;
      }
      dt {
        font-weight: 500;
      }
      dd {
        margin: 0;
      }
      @media (max-width: 700px) {
        .layout {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductViewComponent {
  readonly product = signal<Product | null>(null);
  readonly loading = signal(true);
  constructor(api: ProductApiService, route: ActivatedRoute) {
    api
      .get(route.snapshot.paramMap.get('id') ?? '')
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((item) => {
        this.product.set(item);
        if (item.imageUrl)
          api
            .image(item.productId)
            .subscribe((blob) =>
              this.product.update((product) =>
                product ? { ...product, imageUrl: URL.createObjectURL(blob) } : product,
              ),
            );
      });
  }
}
