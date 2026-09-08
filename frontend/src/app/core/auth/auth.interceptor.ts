import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Attaches the access token to API calls.
 *
 * The sign-in and refresh endpoints are skipped deliberately: sending an expired token to them
 * would be pointless, and the refresh call must rely on the cookie alone.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const token = auth.token();

  const isAuthEndpoint =
    request.url.includes('/api/v1/auth/login') || request.url.includes('/api/v1/auth/refresh');

  if (!token || isAuthEndpoint) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
