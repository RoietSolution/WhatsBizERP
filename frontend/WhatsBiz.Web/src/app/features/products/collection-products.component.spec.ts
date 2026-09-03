import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { CollectionApiService } from './collection-api.service';
import { CollectionProductsComponent } from './collection-products.component';
import { ProductApiService } from './product-api.service';

describe('CollectionProductsComponent', () => {
  it('shows collection members only once and keeps non-members available to add', () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'collection-1' } } },
        },
      ],
    });
    const api = jasmine.createSpyObj<CollectionApiService>('CollectionApiService', [
      'get',
      'addProducts',
      'removeProduct',
    ]);
    api.get.and.returnValue(
      of({
        collectionId: 'collection-1',
        name: 'Featured',
        slug: 'featured',
        isActive: true,
        productCount: 1,
        displayOrder: 1,
        products: [
          {
            productId: 'product-1',
            productCode: 'P001',
            productName: 'Existing product',
            categoryName: 'Category',
            sellingPrice: 100,
            isActive: true,
            displayOrder: 1,
          },
        ],
      }),
    );
    const productApi = jasmine.createSpyObj<ProductApiService>('ProductApiService', ['search']);
    productApi.search.and.returnValue(
      of({
        items: [
          {
            productId: 'product-1', productCode: 'P001', productName: 'Existing product',
            categoryName: 'Category', brandName: 'Brand', unitName: 'Each', purchasePrice: 80,
            sellingPrice: 100, gstPercentage: 0, isActive: true, isWhatsAppVisible: true,
          },
          {
            productId: 'product-2', productCode: 'P002', productName: 'Available product',
            categoryName: 'Category', brandName: 'Brand', unitName: 'Each', purchasePrice: 90,
            sellingPrice: 120, gstPercentage: 0, isActive: true, isWhatsAppVisible: true,
          },
        ],
        totalCount: 2,
        pageNumber: 1,
        pageSize: 50,
      }),
    );
    const snack = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    const router = jasmine.createSpyObj<Router>('Router', ['navigate']);

    const component = TestBed.runInInjectionContext(
      () => new CollectionProductsComponent(api, productApi, snack, router),
    );

    expect(component.members().map((item) => item.productId)).toEqual(['product-1']);
    expect(component.availableProducts().map((item) => item.productId)).toEqual(['product-2']);

    component.openProduct('product-1');
    expect(router.navigate).toHaveBeenCalledWith(['/products', 'product-1']);
  });
});
