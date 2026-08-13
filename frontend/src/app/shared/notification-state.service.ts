import { Injectable, inject, signal } from '@angular/core';
import { CaseService } from '../cases/case.service';
import { CallLogService } from '../cases/call-log.service';
import { OverdueCase } from './models';

const READ_KEY = 'cs_read_overdue_ids';
// sessionStorage key for the signed-in staff user's record (mirrors AuthService.USER_KEY =
// 'cs_user'). Read directly here to avoid a circular DI edge (AuthService already
// injects NotificationStateService for its logout() reset).
const USER_KEY = 'cs_user';

/**
 * Drives the notification center. The list of "needs follow-up" cases is
 * computed LIVE from the cases API (overdue filter) — it is not stored, so a
 * case stays in the center for as long as it remains overdue (open and no
 * recent follow-up). The "mark all read" acknowledgement set is USER-SCOPED
 * (keyed by the signed-in user id — see keyFor) so each account keeps its own
 * overdue acknowledgements: Grace marking all read does NOT clear admin's or
 * maria's badge. On logout the read set for the logging-out user is cleared
 * (AuthService.logout -> reset), so the next login shows the badge again for
 * any case still overdue. Marking a single case read is supported.
 */
@Injectable({ providedIn: 'root' })
export class NotificationStateService {
  private readonly caseService = inject(CaseService);
  private readonly callLogService = inject(CallLogService);

  /** Live list of overdue cases (recomputed on refresh). */
  readonly overdue = signal<OverdueCase[]>([]);
  /** Ids of cases the user has acknowledged (read) this session. */
  private readonly readIds = signal<Set<number>>(this.loadReadIds());
  /** Whether a refresh is in flight. */
  readonly loading = signal(false);

  /** Number of overdue cases still unread (drives the badge). */
  readonly visibleCount = signal(0);

  /** Refreshes the live overdue list and recomputes the visible count. */
  refresh(): void {
    this.loading.set(true);
    this.caseService.list({ overdue: true }).subscribe({
      next: (cases) => {
        const mapped: OverdueCase[] = cases.map((c) => ({
          caseId: c.id,
          subject: c.subject,
          customerName: c.customerName,
          assignedToUserName: c.assignedToUserName ?? '',
          priority: c.priority,
          followUpDueUtc: c.followUpDueUtc ?? '',
          // Use the server-computed value so it matches the dashboard exactly
          // (avoids client-side timezone drift in the day count).
          daysOverdue: c.daysOverdue ?? 0,
          detail: c,
        }));
        this.overdue.set(mapped);
        this.recompute();
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** Loads the full case detail (with call logs) for the expanded view. */
  loadDetail(caseId: number) {
    return this.callLogService.listByCase(caseId);
  }

  /** True when the case has been acknowledged this session. */
  isRead(caseId: number): boolean {
    return this.readIds().has(caseId);
  }

  /** Marks a single case read (decreases the unread count). */
  markRead(caseId: number): void {
    if (this.readIds().has(caseId)) return;
    const next = new Set(this.readIds());
    next.add(caseId);
    this.readIds.set(next);
    this.saveReadIds();
    this.recompute();
  }

  /** Marks a single case unread again (restores the highlight + count). */
  markUnread(caseId: number): void {
    if (!this.readIds().has(caseId)) return;
    const next = new Set(this.readIds());
    next.delete(caseId);
    this.readIds.set(next);
    this.saveReadIds();
    this.recompute();
  }

  /** Marks every currently-listed overdue case read (badge → 0). */
  markAllRead(): void {
    this.readIds.set(new Set(this.overdue().map((o) => o.caseId)));
    this.saveReadIds();
    this.recompute();
  }

  /** Marks every case unread again (badge returns to the full count). */
  markAllUnread(): void {
    this.readIds.set(new Set());
    this.saveReadIds();
    this.recompute();
  }

  /** Clears the read state for the CURRENT user (used on logout / new session). */
  reset(): void {
    try {
      sessionStorage.removeItem(this.keyFor());
    } catch { /* quota or SSR */ }
    this.readIds.set(new Set());
    this.recompute();
  }

  private recompute(): void {
    const read = this.readIds();
    const unread = this.overdue().filter((o) => !read.has(o.caseId)).length;
    this.visibleCount.set(unread);
  }

  /**
   * Builds the sessionStorage key for the overdue "read" acknowledgement set,
   * scoped by the signed-in user so each account keeps its own state (mirrors
   * the nav-badge fix). Falls back to the legacy unscoped key when no user is
   * signed in, so a pre-login/legacy value still resolves until first logout.
   */
  private keyFor(): string {
    try {
      const raw = sessionStorage.getItem(USER_KEY);
      if (raw) {
        const user = JSON.parse(raw) as { id?: string };
        if (user?.id) return `${READ_KEY}_${user.id}`;
      }
    } catch { /* quota or SSR */ }
    return READ_KEY;
  }

  private loadReadIds(): Set<number> {
    try {
      const raw = sessionStorage.getItem(this.keyFor());
      if (!raw) return new Set();
      return new Set(JSON.parse(raw) as number[]);
    } catch {
      return new Set();
    }
  }

  private saveReadIds(): void {
    sessionStorage.setItem(this.keyFor(), JSON.stringify([...this.readIds()]));
  }
}
