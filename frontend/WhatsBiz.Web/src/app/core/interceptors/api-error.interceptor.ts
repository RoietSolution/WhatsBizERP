import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthenticationService } from '../services/authentication.service';
import { JwtStorageService } from '../services/jwt-storage.service';
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const storage = inject(JwtStorageService);
  const authentication = inject(AuthenticationService);
  const isAuthenticationRequest =
    request.url.endsWith('/login') || request.url.endsWith('/refresh');
  const token = storage.token;
  const authorized =
    token && !isAuthenticationRequest
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;
  return next(authorized).pipe(
    catchError((error: HttpErrorResponse) =>
      error.status === 401 &&
      storage.refreshToken &&
      !isAuthenticationRequest &&
      !request.url.endsWith('/logout')
        ? authentication.refresh().pipe(
            switchMap(() =>
              next(request.clone({ setHeaders: { Authorization: `Bearer ${storage.token}` } })),
            ),
            catchError((refreshError) => {
              authentication.clearSession();
              return throwError(() => refreshError);
            }),
          )
        : throwError(() => error),
    ),
  );
};
