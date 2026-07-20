import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CustomerCaseSummary,
  CustomerCaseDetail,
  CustomerCaseComment,
  CreateCustomerCase,
  CreateCustomerComment,
  ValidateInviteResponse,
} from '../shared/models';

/**
 * Talks to the customer-portal API: list/detail/create cases, the shared
 * comment thread, and the public invite-validation endpoint. All requests
 * target /api/customer-portal (or /api/customer-auth for the public invite
 * check) and are authenticated by the CustomerTokenInterceptor.
 */
@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly portalUrl = '/api/customer-portal';
  private readonly authUrl = '/api/customer-auth';

  /** Lists the calling customer's own cases. */
  listCases(): Observable<CustomerCaseSummary[]> {
    return this.http.get<CustomerCaseSummary[]>(`${this.portalUrl}/cases`);
  }

  /** Gets one of the customer's cases (404 if not owned / missing). */
  getCase(id: number): Observable<CustomerCaseDetail> {
    return this.http.get<CustomerCaseDetail>(`${this.portalUrl}/cases/${id}`);
  }

  /** Creates a case on behalf of the calling customer. */
  createCase(dto: CreateCustomerCase): Observable<CustomerCaseSummary> {
    return this.http.post<CustomerCaseSummary>(`${this.portalUrl}/cases`, dto);
  }

  /** Gets the comment thread for one of the customer's cases. */
  getComments(id: number): Observable<CustomerCaseComment[]> {
    return this.http.get<CustomerCaseComment[]>(
      `${this.portalUrl}/cases/${id}/comments`,
    );
  }

  /** Posts a comment authored by the customer. */
  addComment(id: number, dto: CreateCustomerComment): Observable<CustomerCaseComment> {
    return this.http.post<CustomerCaseComment>(
      `${this.portalUrl}/cases/${id}/comments`,
      dto,
    );
  }

  /** Validates an invite token (public, no auth). */
  validateInvite(token: string): Observable<ValidateInviteResponse> {
    return this.http.get<ValidateInviteResponse>(
      `${this.authUrl}/validate-invite`,
      { params: { token } },
    );
  }

  /** Accepts an invite: sets the password and activates the account. */
  acceptInvite(token: string, password: string): Observable<void> {
    return this.http.post<void>(`${this.authUrl}/accept-invite`, {
      token,
      password,
    });
  }
}
