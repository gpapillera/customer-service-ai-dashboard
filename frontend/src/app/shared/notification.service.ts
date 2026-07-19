import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Notification, NotificationSummary } from './models';

/**
 * Talks to the Notifications API: fetches the unread summary for the bell,
 * lists all notifications, and marks them read.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/notifications';

  /** Reactive unread count, shared across the app (bell badge). */
  readonly unreadCount = signal(0);

  /** Fetches the summary (unread count + recent) and updates the badge. */
  loadSummary(): Observable<NotificationSummary> {
    return new Observable<NotificationSummary>((subscriber) => {
      this.http.get<NotificationSummary>(`${this.baseUrl}/summary`).subscribe({
        next: (s) => {
          this.unreadCount.set(s.unreadCount);
          subscriber.next(s);
          subscriber.complete();
        },
        error: (err) => subscriber.error(err),
      });
    });
  }

  /** Returns all notifications, newest first. */
  list(): Observable<Notification[]> {
    return this.http.get<Notification[]>(this.baseUrl);
  }

  /** Marks a single notification read and decrements the badge. */
  markRead(id: number): Observable<void> {
    return new Observable<void>((subscriber) => {
      this.http.post<void>(`${this.baseUrl}/${id}/read`, {}).subscribe({
        next: () => {
          this.unreadCount.update((c) => (c > 0 ? c - 1 : 0));
          subscriber.next();
          subscriber.complete();
        },
        error: (err) => subscriber.error(err),
      });
    });
  }

  /** Marks every notification read and clears the badge. */
  markAllRead(): Observable<void> {
    return new Observable<void>((subscriber) => {
      this.http.post<void>(`${this.baseUrl}/read-all`, {}).subscribe({
        next: () => {
          this.unreadCount.set(0);
          subscriber.next();
          subscriber.complete();
        },
        error: (err) => subscriber.error(err),
      });
    });
  }
}
