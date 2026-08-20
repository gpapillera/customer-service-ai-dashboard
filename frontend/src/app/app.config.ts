import { ApplicationConfig, provideZoneChangeDetection, ENVIRONMENT_INITIALIZER, inject } from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptorsFromDi, HTTP_INTERCEPTORS } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';

import { routes } from './app.routes';
import { TokenInterceptor } from './auth/token.interceptor';
import { CustomerTokenInterceptor } from './customer/customer-token.interceptor';
import { AuthService } from './auth/auth.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'enabled' })),
    provideHttpClient(withInterceptorsFromDi()),
    provideAnimations(),
    { provide: HTTP_INTERCEPTORS, useClass: TokenInterceptor, multi: true },
    { provide: HTTP_INTERCEPTORS, useClass: CustomerTokenInterceptor, multi: true },
    // On bootstrap, reconcile the cached sessionStorage identity with the
    // real API identity (the HttpOnly access_token cookie). If they disagree
    // the UI adopts the server's user, so an admin never sees their own name
    // while silently authorized as another user (which 403s every Admin call).
    {
      provide: ENVIRONMENT_INITIALIZER,
      multi: true,
      useValue: () => {
        const auth = inject(AuthService);
        auth.reconcile();
      },
    },
  ],
};
