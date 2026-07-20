import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Agent } from '../shared/models';

/**
 * Talks to the Users API. Provides the admin Agents list (per-agent open-case
 * counts) and the assignment dropdown source.
 */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/users';

  /** Lists every Agent-role user with their currently-open case count. */
  agentsSummary(): Observable<Agent[]> {
    return this.http.get<Agent[]>(`${this.baseUrl}/agents-summary`);
  }
}
