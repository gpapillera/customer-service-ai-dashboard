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

    // Auth endpoints must never trigger a refresh/redirect loop: a failed
    // refresh or logout is terminal on its own, and retrying it would recurse.
    if (req.url.startsWith('/api/auth/refresh') || req.url.startsWith('/api/auth/logout')) {
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

  /**
   * Handles a 401 by attempting ONE silent refresh and retrying the original
   * request. The retry is the terminal attempt: if it 401s again the session is
   * genuinely dead, so we log out and bounce to /login?reason=session_expired
   * instead of looping forever (which previously left components stuck on a
   * permanent spinner). `attempt` caps the refresh at exactly one.
   */
  private handle401(
    original: HttpRequest<unknown>,
    next: HttpHandler,
    attempt = 0,
  ): Observable<HttpEvent<unknown>> {
    // Already refreshed once and the retry still failed — session is dead.
    // Clear the local session (no HTTP — avoids re-entering this interceptor)
    // and bounce to /login?reason=session_expired. Terminal: no second refresh.
    if (attempt >= 1) {
      this.refreshInFlight = null;
      this.auth.clearLocalSession();
      this.router.navigate(['/login'], { queryParams: { reason: 'session_expired' } });
      return throwError(() => new Error('Session expired'));
    }

    if (!this.refreshInFlight) {
      this.refreshInFlight = this.auth.refresh().pipe(
        switchMap(() => {
          this.refreshInFlight = null;
          return [true];
        }),
        catchError((err) => {
          this.refreshInFlight = null;
          this.auth.clearLocalSession();
          this.router.navigate(['/login'], { queryParams: { reason: 'session_expired' } });
          return throwError(() => err);
        }),
      );
    }

    return this.refreshInFlight!.pipe(
      filter((success) => success),
      take(1),
      switchMap(() => next.handle(this.withCredentials(original))),
      // The one retry: a second 401 after a successful refresh means the
      // session cannot be recovered — recurse with attempt=1 -> terminal redirect.
      catchError((err) => {
        if (err instanceof HttpErrorResponse && err.status === 401) {
          return this.handle401(original, next, attempt + 1);
        }
        return throwError(() => err);
      }),
    );
  }
}
