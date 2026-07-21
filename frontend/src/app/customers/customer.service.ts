import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Customer, Case, CreateCustomer } from '../shared/models';

/**
 * Talks to the Customers API (list, search, create, update, delete).
 */
@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/customers';

  /** Lists all customers. */
  list(): Observable<Customer[]> {
    return this.http.get<Customer[]>(this.baseUrl);
  }

  /** Searches customers by name/email/phone. */
  search(term: string): Observable<Customer[]> {
    return this.http.get<Customer[]>(`${this.baseUrl}/search`, {
      params: { term },
    });
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
}
