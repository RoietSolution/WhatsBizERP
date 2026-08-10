import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, finalize, switchMap, throwError } from 'rxjs';
import { AuthenticationService } from '../services/authentication.service';
import { JwtStorageService } from '../services/jwt-storage.service';
const activeSubmissions = new Map<string, { key: string; callers: number }>();

export const apiErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const storage = inject(JwtStorageService);
  const authentication = inject(AuthenticationService);
  const isAuthenticationRequest =
    request.url.endsWith('/login') || request.url.endsWith('/refresh');
  const idempotentPaths = [
    '/api/pos/invoice',
    '/api/pos/return',
    '/api/pos/payment',
    '/api/purchases',
    '/api/purchases/return',
    '/api/purchases/payment',
    '/api/payments',
    '/api/receipts',
    '/api/inventory/adjustment',
    '/api/inventory/adjustments',
    '/api/inventory/transfer',
    '/api/inventory/transfers',
    '/api/inventory/verification',
  ];
  const needsIdempotency =
    request.method === 'POST' && idempotentPaths.some((path) => request.url === path);
  let submissionSignature: string | undefined;
  let generatedKey: string | undefined;
  if (needsIdempotency && !request.headers.has('Idempotency-Key')) {
    submissionSignature = `${request.method}|${request.url}|${JSON.stringify(request.body)}`;
    const active = activeSubmissions.get(submissionSignature);
    if (active) {
      active.callers += 1;
      generatedKey = active.key;
    } else {
      generatedKey = crypto.randomUUID();
      activeSubmissions.set(submissionSignature, { key: generatedKey, callers: 1 });
    }
  }
  const idempotent = generatedKey
    ? request.clone({ setHeaders: { 'Idempotency-Key': generatedKey } })
    : request;
  const token = storage.token;
  const authorized =
    token && !isAuthenticationRequest
      ? idempotent.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : idempotent;
  return next(authorized).pipe(
    catchError((error: HttpErrorResponse) =>
      error.status === 401 &&
      storage.refreshToken &&
      !isAuthenticationRequest &&
      !request.url.endsWith('/logout')
        ? authentication.refresh().pipe(
            switchMap(() =>
              next(idempotent.clone({ setHeaders: { Authorization: `Bearer ${storage.token}` } })),
            ),
            catchError((refreshError) => {
              authentication.clearSession();
              return throwError(() => refreshError);
            }),
          )
        : throwError(() => error),
    ),
    finalize(() => {
      if (!submissionSignature || !generatedKey) return;
      const active = activeSubmissions.get(submissionSignature);
      if (!active || active.key !== generatedKey) return;
      active.callers -= 1;
      if (active.callers === 0) activeSubmissions.delete(submissionSignature);
    }),
  );
};
