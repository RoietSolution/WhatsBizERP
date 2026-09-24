import { ApplicationConfig } from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { DevelopmentStorefrontDataProvider } from './data/development-storefront-data.provider';
import { HttpStorefrontDataProvider } from './data/http-storefront-data.provider';
import { STOREFRONT_DATA_PROVIDER } from './data/storefront-data.provider';
import { routes } from './app.routes';
import { environment } from '../environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(),
    environment.useMockData
      ? { provide: STOREFRONT_DATA_PROVIDER, useExisting: DevelopmentStorefrontDataProvider }
      : { provide: STOREFRONT_DATA_PROVIDER, useExisting: HttpStorefrontDataProvider },
  ],
};
