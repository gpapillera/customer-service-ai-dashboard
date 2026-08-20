import { Observable, of, throwError } from 'rxjs';
import { catchError, switchMap, take, filter, finalize } from 'rxjs/operators';
import { AuthService } from '../auth/auth.service';

/**
 * Retries an API call once using the existing silent-refresh path when it
 * fails with a 401/403 — the same refresh the TokenInterceptor performs, but
 * driven from the subscribe site so a transient auth failure self-heals
 * instead of leaving the caller stuck on a permanent error/empty state.
 *
 * Why: the app's secondary fetches (agent KPI overlay, dashboard workload,
 * recycle-bin drawer, conversation poll) fire after login when the short-lived
 * access cookie has aged out. The interceptor refreshes ONCE, but if that
 * single attempt races or the retry window misses, the component's error
 * handler has historically set a permanent "Could not load…" / empty widget.
 * Driving the retry from here lets the component just retry the same call.
 *
 * Single-flight: the AuthService.refresh() interceptor already de-dupes
 * concurrent refreshes, so calling it again here is safe and cheap.
 *
 * ponytail: single retry only — a second 401 after a successful refresh means
 * the session is genuinely dead, so we stop and let the app redirect to login.
 */
export function withAuthRetry<T>(
  auth: AuthService,
  attempt = 0,
): (source: Observable<T>) => Observable<T> {
  return (source: Observable<T>) =>
    source.pipe(
      catchError((err) => {
        const status = err?.status;
        if (attempt < 1 && (status === 401 || status === 403)) {
          return auth.refresh().pipe(
            take(1),
            switchMap(() => of(true)),
            catchError(() => of(false)),
            switchMap((ok) =>
              ok ? source : throwError(() => err),
            ),
          );
        }
        return throwError(() => err);
      }),
      finalize(() => {}),
    );
}
