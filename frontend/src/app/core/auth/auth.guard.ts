import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps unauthenticated callers out of the shell, and makes sure a restored session knows who it
 * belongs to before any screen renders.
 *
 * A page reload leaves the tokens in storage but nothing else in memory, so without the second
 * step the shell would draw with no user, no company and an empty menu, then fill in a moment
 * later. Waiting here costs one request and avoids a screen that visibly assembles itself.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isSignedIn()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }

  if (!auth.user()) {
    try {
      await auth.loadContext();
    } catch {
      // The stored token is no longer honoured -- revoked, or the signing key was rotated.
      auth.clear();
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }
  }

  return true;
};

/**
 * Guards a route behind a permission.
 *
 * The menu already hides what a user cannot open, but a route can be reached by typing the address
 * or following an old bookmark, and a screen that loads and then fails every request is worse than
 * one that is not there.
 */
export function requirePermission(permission: string): CanActivateFn {
  return (route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (auth.can(permission)) {
      return true;
    }

    return router.createUrlTree(['/'], { queryParams: { denied: permission } });
  };
}
