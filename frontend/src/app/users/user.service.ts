import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Agent, UpdateAgent, Dashboard } from '../shared/models';

/**
 * Talks to the Users API. Provides the admin Agents list (per-agent open-case
 * counts), assignment dropdown source, and admin agent management (Phase 11).
 */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/users';

  /** Lists every Agent-role user with their currently-open case count. */
  agentsSummary(): Observable<Agent[]> {
    return this.http.get<Agent[]>(`${this.baseUrl}/agents-summary`);
  }

  /** Admin edits an agent's name and email (Phase 11). */
  updateAgent(id: string, dto: UpdateAgent): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, dto);
  }

  /** Returns the full KPI set for a specific agent (Phase 11). */
  getAgentKpis(id: string): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${this.baseUrl}/${id}/kpis`);
  }
}
