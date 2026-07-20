import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { Case, CreateCase, UpdateCase, Category, Agent } from '../shared/models';
import { CATEGORIES } from '../shared/categories';

/**
 * Talks to the Cases and Categories APIs (list/filter, detail, create,
 * update, delete) plus the category lookup used by forms.
 */
@Injectable({ providedIn: 'root' })
export class CaseService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/cases';

  /** Lists cases with optional filters. */
  list(filters: {
    status?: string;
    priority?: string;
    categoryId?: number;
    from?: string;
    to?: string;
    overdue?: boolean;
    assignedToMe?: boolean;
  } = {}): Observable<Case[]> {
    let params = new HttpParams();
    if (filters.status) params = params.set('status', filters.status);
    if (filters.priority) params = params.set('priority', filters.priority);
    if (filters.categoryId)
      params = params.set('categoryId', filters.categoryId.toString());
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.overdue) params = params.set('overdue', 'true');
    if (filters.assignedToMe) params = params.set('assignedToMe', 'true');
    return this.http.get<Case[]>(this.baseUrl, { params });
  }

  /** Gets a single case by id. */
  get(id: number): Observable<Case> {
    return this.http.get<Case>(`${this.baseUrl}/${id}`);
  }

  /** Creates a case (priority is ML-suggested when omitted). */
  create(dto: CreateCase): Observable<Case> {
    return this.http.post<Case>(this.baseUrl, dto);
  }

  /** Updates a case (priority override allowed). */
  update(id: number, dto: UpdateCase): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, dto);
  }

  /** Deletes a case. */
  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /**
   * Previews an AI priority suggestion on demand (does not create a case).
   * The backend computes priority from the same features used at creation;
   * we derive the keyword flag from the description and send neutral
   * history values since the case does not exist yet.
   */
  predictPriority(req: {
    categoryId: number;
    customerId: number;
    description: string;
  }): Observable<{ priority: string; reason: string; source: string }> {
    // Send the raw description; the backend derives a sentiment score from it
    // (single source of truth, mirroring the Python training lexicon).
    return this.http.post<{ priority: string; reason: string; source: string }>(
      '/api/ml/predict-priority',
      {
        categoryId: req.categoryId,
        priorCaseCount: 0,
        daysSinceLastContact: 0,
        description: req.description,
      },
    );
  }

  /** Lists categories for dropdowns (from the shared seed constant). */
  categories(): Observable<Category[]> {
    return of(CATEGORIES);
  }

  /** Lists agents/admins for the assignee dropdown (GET /api/users). */
  agents(): Observable<Agent[]> {
    return this.http.get<Agent[]>('/api/users');
  }
}
