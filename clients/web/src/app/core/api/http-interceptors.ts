import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, retry, throwError, timer } from 'rxjs';
import { ToastService } from '../notifications/toast.service';
import { errorMessage } from './error-messages';

/** Set to true on a request whose errors the caller shows itself. */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

/**
 * 503 means another module is still catching up (eventual consistency): retry a few times.
 * Other errors are shown as a toast with a localized message.
 */
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const toasts = inject(ToastService);

  return next(request).pipe(
    retry({
      count: 5,
      delay: (error) =>
        error instanceof HttpErrorResponse && error.status === 503 ? timer(700) : throwError(() => error),
    }),
    catchError((error) => {
      if (!request.context.get(SILENT_ERRORS) && request.url.startsWith('/api')) {
        toasts.error(errorMessage(error));
      }

      return throwError(() => error);
    }),
  );
};
