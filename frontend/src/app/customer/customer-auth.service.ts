import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

/**
 * Customer-facing authentication, kept entirely separate from the staff
 * AuthService. The token is held in an HttpOnly cookie (set by the API on
 * login/refresh), so the two sessions never share browser storage and XSS
 * cannot read either token.
 *
 * See docs/DIY.md §8 (customer portal) and the Phase C cookie-auth notes.
 */
const USER_KEY = 'customer_user';

/** Response from POST /api/customer-auth/login. */
export interface CustomerLoginResponse {
  token: string;
  expiresUtc: string;
  customerId: number;
  customerName: string;
  role: string;
}

@Injectable({ providedIn: 'root' })
export class CustomerAuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/customer-auth';

  /** Reactive signal of the current customer (or null). */
  readonly currentCustomer = signal<CustomerLoginResponse | null>(this.loadUser());

  /** Logs a customer in by email + password. */
  login(email: string, password: string): Observable<CustomerLoginResponse> {
    return this.http
      .post<CustomerLoginResponse>(`${this.baseUrl}/login`, { email, password })
      .pipe(tap((res) => this.setSession(res)));
  }

  /**
   * Silently rotates the server-side refresh cookie into a fresh customer
   * session. New cookies are set by the API on the response.
   */
  refresh(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/refresh`, {}, { withCredentials: true });
  }

  /** Clears the customer session. */
  logout(): void {
    // Best-effort backend logout (clears the HttpOnly cookies + revokes the
    // refresh token). Fire-and-forget: even if it fails the local session is
    // wiped below. `withCredentials` is required so the cookies are sent.
    this.http
      .post(`${this.baseUrl}/logout`, {}, { withCredentials: true })
      .subscribe({ error: () => {} });
    this.clearLocalSession();
  }

  /**
   * Wipes the local session WITHOUT an HTTP call. Used by the customer auth
   * interceptor when a session is non-recoverable: calling the full `logout()`
   * there would itself fire `/api/customer-auth/logout`, which (with no valid
   * cookies) 401s and re-enters the interceptor — an infinite logout/refresh
   * loop. This breaks that recursion.
   */
  clearLocalSession(): void {
    sessionStorage.removeItem(USER_KEY);
    this.currentCustomer.set(null);
  }

  /** Returns the raw JWT, or null. The token now lives in an HttpOnly cookie. */
  getToken(): string | null {
    return null;
  }

  /** True when a customer session is present. */
  isAuthenticated(): boolean {
    return this.currentCustomer() !== null;
  }

  /** The customer's display name, or empty string. */
  getName(): string {
    return this.currentCustomer()?.customerName ?? '';
  }

  /** The customer's id, or 0. */
  getId(): number {
    return this.currentCustomer()?.customerId ?? 0;
  }

  private setSession(res: CustomerLoginResponse): void {
    // The JWT is now set as an HttpOnly cookie by the API, not stored here.
    sessionStorage.setItem(USER_KEY, JSON.stringify(res));
    this.currentCustomer.set(res);
  }

  private loadUser(): CustomerLoginResponse | null {
    const raw = sessionStorage.getItem(USER_KEY);
    if (!raw) {
      return null;
    }
    try {
      return JSON.parse(raw) as CustomerLoginResponse;
    } catch {
      return null;
    }
  }
}
