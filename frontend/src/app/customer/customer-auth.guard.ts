import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CustomerAuthService } from './customer-auth.service';

/**
 * Route guard for the customer portal. Blocks unauthenticated customers and
 * redirects to /customer/login. Kept separate from the staff authGuard so the
 * two session types never interfere.
 *
 * @returns true when the customer is authenticated, otherwise a redirect.
 */
export const customerAuthGuard: CanActivateFn = () => {
  const auth = inject(CustomerAuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }
  return router.createUrlTree(['/customer/login']);
};
