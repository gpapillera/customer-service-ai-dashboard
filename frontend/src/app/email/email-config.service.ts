import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  EmailConfigBundleDto,
  EmailConfigDto,
  EmailDomainDto,
  EmailTemplateDto,
  DomainRequest,
  TemplateRequest,
} from '../shared/models';

/**
 * Admin-only client for the email configuration API. Exposes the test/delivery
 * address, the allowed-domain list that controls direct delivery, and the
 * per-type email templates with personalization tokens. See docs/DIY.md §7.
 */
@Injectable({ providedIn: 'root' })
export class EmailConfigService {
  private readonly http = inject(HttpClient);

  /** Returns the full config bundle (config + domains + templates + suggestions). */
  getBundle(): Observable<EmailConfigBundleDto> {
    return this.http.get<EmailConfigBundleDto>('/api/email-config');
  }

  /** Updates the test/delivery email address. */
  updateTestEmail(testEmail: string): Observable<EmailConfigDto> {
    return this.http.put<EmailConfigDto>('/api/email-config/test-email', { testEmail });
  }

  /** Lists allowed domains. */
  listDomains(): Observable<EmailDomainDto[]> {
    return this.http.get<EmailDomainDto[]>('/api/email-config/domains');
  }

  /** Adds an allowed domain. */
  addDomain(req: DomainRequest): Observable<EmailDomainDto> {
    return this.http.post<EmailDomainDto>('/api/email-config/domains', req);
  }

  /** Updates an allowed domain. */
  updateDomain(id: number, req: DomainRequest): Observable<EmailDomainDto> {
    return this.http.put<EmailDomainDto>(`/api/email-config/domains/${id}`, req);
  }

  /** Removes an allowed domain. */
  removeDomain(id: number): Observable<void> {
    return this.http.delete<void>(`/api/email-config/domains/${id}`);
  }

  /** Lists templates. */
  listTemplates(): Observable<EmailTemplateDto[]> {
    return this.http.get<EmailTemplateDto[]>('/api/email-config/templates');
  }

  /** Inserts or updates a template for a type. */
  upsertTemplate(req: TemplateRequest): Observable<EmailTemplateDto> {
    return this.http.post<EmailTemplateDto>('/api/email-config/templates', req);
  }

  /** Removes a template. */
  deleteTemplate(id: number): Observable<void> {
    return this.http.delete<void>(`/api/email-config/templates/${id}`);
  }
}
