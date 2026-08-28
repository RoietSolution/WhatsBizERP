import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { RuntimeConfigurationService } from '../services/runtime-configuration.service';

export const apiBaseUrlInterceptor: HttpInterceptorFn = (request, next) => {
  const baseUrl = inject(RuntimeConfigurationService).apiBaseUrl();
  const isApiRequest = request.url.startsWith('/api/') || request.url === '/api';
  const isHealthRequest = request.url === '/health';
  const configuredRequest =
    baseUrl && (isApiRequest || isHealthRequest)
      ? request.clone({ url: `${baseUrl}${request.url}` })
      : request;
  return next(configuredRequest);
};
