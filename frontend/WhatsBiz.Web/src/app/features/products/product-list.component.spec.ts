import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { ProductListComponent } from './product-list.component';

describe('ProductListComponent', () => {
  it('downloads the existing product import template from the toolbar action', () => {
    const api = jasmine.createSpyObj<ProductApiService>('ProductApiService', [
      'search',
      'template',
    ]);
    api.search.and.returnValue(
      of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }),
    );
    const template = new Blob(['product-template']);
    api.template.and.returnValue(of(template));
    const snack = jasmine.createSpyObj('MatSnackBar', ['open']);
    const dialog = jasmine.createSpyObj('MatDialog', ['open']);
    const router = jasmine.createSpyObj('Router', ['navigate']);
    const component = TestBed.runInInjectionContext(
      () => new ProductListComponent(api, snack, dialog, router),
    );
    const download = spyOn(
      component as unknown as { download(file: Blob, name: string): void },
      'download',
    );

    component.handle({ action: 'template' });

    expect(component.config.templateEnabled).toBeTrue();
    expect(api.template).toHaveBeenCalledTimes(1);
    expect(download).toHaveBeenCalledWith(template, 'product-import-template.xlsx');
  });
});
