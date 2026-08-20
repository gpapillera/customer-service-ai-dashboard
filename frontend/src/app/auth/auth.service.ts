import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse, StaffProfile, UpdateStaffProfile } from '../shared/models';
import { NotificationStateService } from '../shared/notification-state.service';

const USER_KEY = 'cs_user';

/**
 * Handles authentication: login, current-user state, and logout.
 *
 * The JWT is stored in an HttpOnly cookie by the API (not in the browser),
 * so XSS cannot exfiltrate it. The Angular dev proxy makes `/api` same-origin
 * with the SPA, so the cookie is sent automatically on every request once
 * `withCredentials` is set on the client. A short-lived access cookie is paired
 * with a rotatable refresh cookie (see backend TokenCookieService + refresh
 * endpoints). See docs/DIY.md §4 and the Phase C cookie-auth notes.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly baseUrl = '/api/auth';
  private readonly _currentUser = new BehaviorSubject<LoginResponse | null>(
    this.loadUser(),
  );

  /** Observable of the currently authenticated user (or null). */
  readonly currentUser$ = this._currentUser.asObservable();

  /** Reactive signal mirroring the current user for templates. */
  readonly currentUser = signal<LoginResponse | null>(this.loadUser());

  private readonly notifications = inject(NotificationStateService);

  constructor(private readonly http: HttpClient) {}

  /**
   * Authenticates against the API and stores the returned JWT.
   *
   * @param userName - The user's username.
   * @param password - The user's plaintext password.
   * @returns Observable of the login response.
   */
  login(userName: string, password: string): Observable<LoginResponse> {
    const payload: LoginRequest = { userName, password };
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/login`, payload)
      .pipe(tap((res) => this.setSession(res)));
  }

  /** Clears the session and notifies subscribers. */
  logout(): void {
    // Best-effort backend logout (clears the HttpOnly cookies + revokes the
    // refresh token). Fire-and-forget: even if it fails the local session is
    // wiped below. `withCredentials` is required so the cookies are sent.
    this.http
      .post('/api/auth/logout', {}, { withCredentials: true })
      .subscribe({ error: () => {} });
    this.clearLocalSession();
  }

  /**
   * Wipes the local session WITHOUT an HTTP call. Used by the auth interceptor
   * when a session is non-recoverable: calling the full `logout()` there would
   * itself fire `/api/auth/logout`, which (with no valid cookies) 401s and
   * re-enters the interceptor — an infinite logout/refresh loop that starved
   * the redirect to /login. This breaks that recursion.
   */
  clearLocalSession(): void {
    this.notifications.reset();
    sessionStorage.removeItem(USER_KEY);
    this._currentUser.next(null);
    this.currentUser.set(null);
  }

  /** Returns the raw JWT, or null if not authenticated. */
  getToken(): string | null {
    // The JWT now lives in an HttpOnly cookie (set by the API on login/refresh)
    // and is readable only by the server — not by JS. Returning null here keeps
    // the interceptor on the cookie path while still attaching a header when a
    // legacy token is somehow present (dual-source safety for the SSE fetch).
    return null;
  }

  /** True when a user session is present. */
  isAuthenticated(): boolean {
    return this.currentUser() !== null;
  }

  /** The current user's role, or empty string. */
  getRole(): string {
    return this.currentUser()?.role ?? '';
  }

  // ── Staff profile + password-reset (Phase 10) ──

  /** Returns the signed-in staff member's own profile (JWT-scoped). */
  getProfile(): Observable<StaffProfile> {
    return this.http.get<StaffProfile>('/api/users/me');
  }

  /**
   * Silently rotates the server-side refresh cookie into a fresh session. The
   * new access + refresh cookies are set by the API on the response; nothing
   * needs to be stored client-side. Used by the interceptor on a 401.
   */
  refresh(): Observable<void> {
    return this.http
      .post<void>('/api/auth/refresh', {}, { withCredentials: true });
  }

  /** Updates the signed-in staff member's own name (email read-only). */
  updateProfile(dto: UpdateStaffProfile): Observable<void> {
    return this.http.put<void>('/api/users/me', dto).pipe(
      tap(() => {
        const current = this.currentUser();
        if (current) {
          const updated: LoginResponse = { ...current, fullName: dto.fullName };
          sessionStorage.setItem(USER_KEY, JSON.stringify(updated));
          this._currentUser.next(updated);
          this.currentUser.set(updated);
        }
      }),
    );
  }

  /** Requests a password-reset email (JWT-scoped). */
  requestPasswordReset(): Observable<void> {
    return this.http.post<void>('/api/users/me/request-password-reset', {});
  }

  private setSession(res: LoginResponse): void {
    // NOTE: the JWT is no longer stored in the browser. The API sets it as an
    // HttpOnly cookie (see backend TokenCookieService) so XSS cannot read it.
    // We keep the user record (minus the raw token) for the UI session.
    sessionStorage.setItem(USER_KEY, JSON.stringify(res));
    this._currentUser.next(res);
    this.currentUser.set(res);
  }

  /**
   * Reconciles the locally-cached identity (sessionStorage) with the identity
   * the API actually authenticates (the HttpOnly access_token cookie). These
   * are two independent sources: a prior session's localStorage entry can
   * outlive the real cookie, so the UI would show one user while every API
   * call is authorized as another (e.g. sidenav "Ada Admin" but /api/users/me
   * returns "Maria Santos", and all Admin-only endpoints 403).
   *
   * Call this at app bootstrap and after every silent refresh (the refresh
   * mints a fresh cookie that may belong to a different user than the cached
   * one). If the API returns a user that differs from the cached record — or
   * the call is unauthorized — we wipe the local session so the UI can never
   * display an identity the API will reject.
   */
  reconcile(): void {
    const cached = this._currentUser.value;
    if (!cached) return; // nothing to reconcile; guard handles unauthenticated
    this.getProfile().subscribe({
      next: (profile) => {
        if (profile.id !== cached.id || profile.role !== cached.role) {
          // The cookie belongs to a different user than we think we are.
          // Trust the server: adopt the real identity so the UI matches the
          // authorization the API will actually apply.
          const corrected: LoginResponse = {
            ...cached,
            id: profile.id,
            userName: profile.userName,
            fullName: profile.fullName,
            role: profile.role,
            agentDisplayId: profile.agentDisplayId,
            profilePictureUrl: profile.profilePictureUrl,
          };
          this.setSession(corrected);
        }
      },
      error: () => {
        // Cookie is missing/expired/invalid for the cached user — drop the
        // stale local session so the guard/redirect can send us to /login.
        this.clearLocalSession();
      },
    });
  }

  private loadUser(): LoginResponse | null {
    const raw = sessionStorage.getItem(USER_KEY);
    return raw ? (JSON.parse(raw) as LoginResponse) : null;
  }
}
