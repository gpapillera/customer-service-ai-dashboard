import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Notification } from '../shared/models';

/**
 * Fetches the email notification log from the backend. Every email sent by
 * the system (overdue reminders, password resets, resolved confirmations,
 * customer invites) is persisted with Channel == Email, so this endpoint
 * gives admins a clear view of what was sent, to whom, and when.
 */
@Injectable({ providedIn: 'root' })
export class EmailLogService {
  private readonly http = inject(HttpClient);

  /** Returns all sent emails, newest first. */
  getAll(): Observable<Notification[]> {
    return this.http.get<Notification[]>('/api/emails');
  }
}
