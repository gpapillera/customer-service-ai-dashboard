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
import { AuthService } from './auth.service';

/**
 * Attaches auth to every staff API request and redirects to /login on 401.
 *
 * The JWT is now an HttpOnly cookie (set by the API on login/refresh), so we
 * send `withCredentials: true` on every request so the browser attaches it.
 * We STILL attach an Authorization header when a legacy token happens to be
 * present (dual-source safety: the API accepts cookie OR header). On a 401 we
 * attempt ONE silent refresh (using the refresh cookie) and retry the original
 * request; if that fails we clear the session and redirect to login.
 *
 * A module-level single-flight guard ensures that if many requests 401 at once
 * (e.g. the 15-min access token expired), only ONE refresh call is made and
 * every caller shares its result.
 */
@Injectable()
export class TokenInterceptor implements HttpInterceptor {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // Single-flight refresh state (shared across all in-flight requests).
  private refreshInFlight: Observable<boolean> | null = null;

  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (req.url.startsWith('/api/customer-portal')) {
      return next.handle(req);
    }

    // Ensure credentials are sent (cookies) on every staff request.
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
    // Already cloned with credentials? avoid double-cloning.
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
          this.router.navigate(['/login']);
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
