import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http'; import { catchError, throwError } from 'rxjs';
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) => next(request).pipe(catchError((error: HttpErrorResponse) => throwError(() => error)));
