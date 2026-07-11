import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse } from '../shared/models';

const TOKEN_KEY = 'cs_token';
const USER_KEY = 'cs_user';

/**
 * Handles authentication: login, token storage, and current-user state.
 *
 * The JWT is kept in sessionStorage (simple for an MVP; note this is less
 * secure than an httpOnly cookie for production — see README).
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
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(USER_KEY);
    this._currentUser.next(null);
    this.currentUser.set(null);
  }

  /** Returns the raw JWT, or null if not authenticated. */
  getToken(): string | null {
    return sessionStorage.getItem(TOKEN_KEY);
  }

  /** True when a token is present. */
  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  /** The current user's role, or empty string. */
  getRole(): string {
    return this.currentUser()?.role ?? '';
  }

  private setSession(res: LoginResponse): void {
    sessionStorage.setItem(TOKEN_KEY, res.token);
    sessionStorage.setItem(USER_KEY, JSON.stringify(res));
    this._currentUser.next(res);
    this.currentUser.set(res);
  }

  private loadUser(): LoginResponse | null {
    const raw = sessionStorage.getItem(USER_KEY);
    return raw ? (JSON.parse(raw) as LoginResponse) : null;
  }
}
