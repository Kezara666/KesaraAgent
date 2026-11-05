import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth';

/** Sends people without a token to the sign-in page. The API still checks every call (FR-8.4). */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthStore);
  return auth.isSignedIn() ? true : inject(Router).createUrlTree(['/login']);
};
