import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps a signed-out visitor out of the admin area and remembers where they were going.
 *
 * This is a convenience, not a security control: every admin endpoint refuses an anonymous or
 * under-privileged caller on the server (NFR-AUTHZ-01). Removing this guard would make the admin
 * area ugly, not insecure.
 */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isSignedIn()) {
    return true;
  }

  return router.createUrlTree(['/admin/login'], { queryParams: { returnUrl: state.url } });
};
