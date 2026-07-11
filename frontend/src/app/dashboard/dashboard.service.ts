import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Dashboard } from '../shared/models';

/**
 * Talks to the Dashboard API (aggregated KPIs + trends + breakdown).
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/dashboard';

  /** Returns the full dashboard payload. */
  get(): Observable<Dashboard> {
    return this.http.get<Dashboard>(this.baseUrl);
  }
}
