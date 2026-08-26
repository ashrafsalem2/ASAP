import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Endpoints that must not be retried after a refresh, because they are the refresh. */
const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'];

/**
 * Attaches the access token, and quietly renews it when the server says it has expired.
 *
 * Access tokens last fifteen minutes, so without this a user is thrown back to the sign-in screen
 * four times an hour. Renewing on a 401 and retrying once means they never see it happen -- and if
 * the refresh is refused, the session really is over and they are sent to sign in.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isAuthEndpoint = AUTH_ENDPOINTS.some((path) => request.url.includes(path));
  const token = auth.accessToken;

  const authorised =
    token && !isAuthEndpoint
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;

  return next(authorised).pipe(
    catchError((error: unknown) => {
      const isUnauthorised = error instanceof HttpErrorResponse && error.status === 401;

      // Only an expired access token is worth retrying. A 401 from the sign-in endpoint means the
      // password was wrong, and retrying it would loop.
      if (!isUnauthorised || isAuthEndpoint || !auth.refreshToken) {
        return throwError(() => error);
      }

      return from(auth.refresh()).pipe(
        switchMap((renewed) => {
          if (!renewed) {
            void router.navigate(['/login']);
            return throwError(() => error);
          }

          return next(
            request.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken}` } }),
          );
        }),
      );
    }),
  );
};
