import { Injectable, inject } from '@angular/core';
import {
  HttpInterceptor,
  HttpRequest,
  HttpHandler,
  HttpEvent,
  HttpErrorResponse,
} from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, switchMap, filter, take } from 'rxjs/operators';
import { Router } from '@angular/router';
import { CustomerAuthService } from './customer-auth.service';

/**
 * Attaches the CUSTOMER auth to /api/customer-portal requests only. All other
 * requests are passed through untouched (the staff TokenInterceptor owns them).
 *
 * The customer JWT is an HttpOnly cookie, so we send `withCredentials: true`
 * and still attach a header when a legacy token is present (dual-source). On a
 * 401 we attempt one silent refresh via the customer refresh cookie and retry;
 * failure clears the session and redirects to /customer/login.
 *
 * Single-flight guard shares one refresh across concurrent 401s.
 */
@Injectable()
export class CustomerTokenInterceptor implements HttpInterceptor {
  private readonly auth = inject(CustomerAuthService);
  private readonly router = inject(Router);

  private refreshInFlight: Observable<boolean> | null = null;

  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!req.url.startsWith('/api/customer-portal')) {
      return next.handle(req);
    }

    const authReq = this.withCredentials(req);

    return next.handle(authReq).pipe(
      catchError((error) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          return this.handle401(req, next);
        }
        return throwError(() => error);
      }),
    );
  }

  private withCredentials(req: HttpRequest<unknown>): HttpRequest<unknown> {
    if (req.withCredentials) {
      return req;
    }
    const token = this.auth.getToken();
    return token
      ? req.clone({ withCredentials: true, setHeaders: { Authorization: `Bearer ${token}` } })
      : req.clone({ withCredentials: true });
  }

  private handle401(
    original: HttpRequest<unknown>,
    next: HttpHandler,
  ): Observable<HttpEvent<unknown>> {
    if (!this.refreshInFlight) {
      this.refreshInFlight = this.auth.refresh().pipe(
        switchMap(() => {
          this.refreshInFlight = null;
          return [true];
        }),
        catchError((err) => {
          this.refreshInFlight = null;
          this.auth.logout();
          this.router.navigate(['/customer/login']);
          return throwError(() => err);
        }),
      );
    }

    return this.refreshInFlight!.pipe(
      filter((success) => success),
      take(1),
      switchMap(() => next.handle(this.withCredentials(original))),
    );
  }
}
