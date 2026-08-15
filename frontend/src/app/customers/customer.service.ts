import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Customer, Case, CreateCustomer, Notification, CustomerActivityItem } from '../shared/models';

/**
 * Talks to the Customers API (list, search, create, update, delete).
 */
@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/customers';

  /** Lists all customers, with optional filter/sort. */
  list(hasAccount?: boolean | null, sortBy?: string | null, sortDirection?: string | null): Observable<Customer[]> {
    const params: Record<string, string> = {};
    if (hasAccount != null) params['hasAccount'] = String(hasAccount);
    if (sortBy) params['sortBy'] = sortBy;
    if (sortDirection) params['sortDirection'] = sortDirection;
    return this.http.get<Customer[]>(this.baseUrl, { params });
  }

  /** Searches customers by name/email/phone, with optional filter/sort. */
  search(term: string, hasAccount?: boolean | null, sortBy?: string | null, sortDirection?: string | null): Observable<Customer[]> {
    const params: Record<string, string> = { term };
    if (hasAccount != null) params['hasAccount'] = String(hasAccount);
    if (sortBy) params['sortBy'] = sortBy;
    if (sortDirection) params['sortDirection'] = sortDirection;
    return this.http.get<Customer[]>(`${this.baseUrl}/search`, { params });
  }

  /** Gets a single customer by id. */
  get(id: number): Observable<Customer> {
    return this.http.get<Customer>(`${this.baseUrl}/${id}`);
  }

  /**
   * Gets the case history for a customer. Server-side scoped: an Agent only
   * receives cases assigned to them (Phase 6 enforcement).
   */
  customerCases(id: number): Observable<Case[]> {
    return this.http.get<Case[]>(`${this.baseUrl}/${id}/cases`);
  }

  /**
   * Gets every email sent to a customer (account invites/resets/manual + case
   * emails), newest first. Reuses the same Agent scoping as the detail page.
   */
  customerEmails(id: number): Observable<Notification[]> {
    return this.http.get<Notification[]>(`${this.baseUrl}/${id}/emails`);
  }

  /**
   * Gets the merged case + account activity timeline for a customer, newest
   * first. Includes account events even when the customer has no cases.
   */
  customerActivity(id: number): Observable<CustomerActivityItem[]> {
    return this.http.get<CustomerActivityItem[]>(`${this.baseUrl}/${id}/activity`);
  }

  /** Creates a customer. */
  create(dto: CreateCustomer): Observable<Customer> {
    return this.http.post<Customer>(this.baseUrl, dto);
  }

  /** Updates a customer. */
  update(dto: CreateCustomer & { id: number }): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${dto.id}`, dto);
  }

  /** Deletes a customer. */
  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /**
   * Records that the calling user viewed/opened this customer's detail page.
   * Coalesced server-side by a 10-minute per-viewer cooldown, so callers can
   * fire-and-forget on every open without flooding the audit. Returns the
   * created row (200) or null (204 coalesced) — the caller ignores both.
   */
  recordView(id: number): Observable<unknown> {
    return this.http.post<unknown>(`${this.baseUrl}/${id}/view`, {});
  }
}
