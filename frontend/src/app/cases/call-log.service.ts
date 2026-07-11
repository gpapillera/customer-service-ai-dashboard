import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CallLog, CreateCallLog } from '../shared/models';

/**
 * Talks to the CallLogs API (list by case, create).
 */
@Injectable({ providedIn: 'root' })
export class CallLogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/calllogs';

  /** Lists call logs for a case. */
  listByCase(caseId: number): Observable<CallLog[]> {
    return this.http.get<CallLog[]>(`${this.baseUrl}/case/${caseId}`);
  }

  /** Adds a call log to a case. */
  create(dto: CreateCallLog): Observable<CallLog> {
    return this.http.post<CallLog>(this.baseUrl, dto);
  }
}
