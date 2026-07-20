import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

/**
 * Customer-facing authentication, kept entirely separate from the staff
 * AuthService. The token is stored under a DIFFERENT sessionStorage key
 * ("customer_auth_token") so a customer session and a staff session can never
 * collide or overwrite each other in the same browser.
 *
 * See docs/DIY.md §8 (customer portal) and the Phase 3 build notes.
 */
const TOKEN_KEY = 'customer_auth_token';
const USER_KEY = 'customer_user';

/** Response from POST /api/customer-auth/login. */
export interface CustomerLoginResponse {
  token: string;
  expiresUtc: string;
  customerId: number;
  customerName: string;
  role: string;
}

/** Decoded JWT payload (only the fields we care about). */
interface JwtPayload {
  nameid?: string;
  CustomerId?: string;
  role?: string;
  exp?: number;
  [key: string]: unknown;
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

  /** Clears the customer session. */
  logout(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(USER_KEY);
    this.currentCustomer.set(null);
  }

  /** Returns the raw JWT, or null if not authenticated. */
  getToken(): string | null {
    return sessionStorage.getItem(TOKEN_KEY);
  }

  /** True when a token is present (and not obviously expired). */
  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) {
      return false;
    }
    const payload = this.decode(token);
    if (payload?.exp && payload.exp * 1000 < Date.now()) {
      this.logout();
      return false;
    }
    return true;
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
    sessionStorage.setItem(TOKEN_KEY, res.token);
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

  /** Decodes the JWT payload (base64url) without verifying the signature. */
  private decode(token: string): JwtPayload | null {
    try {
      const part = token.split('.')[1];
      if (!part) {
        return null;
      }
      const padded = part.replace(/-/g, '+').replace(/_/g, '/');
      const json = atob(padded);
      return JSON.parse(json) as JwtPayload;
    } catch {
      return null;
    }
  }
}
