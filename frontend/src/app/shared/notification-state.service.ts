import { Injectable, inject, signal } from '@angular/core';
import { CaseService } from '../cases/case.service';
import { CallLogService } from '../cases/call-log.service';
import { OverdueCase } from './models';

const READ_KEY = 'cs_read_overdue';

/**
 * Drives the notification center. The list of "needs follow-up" cases is
 * computed LIVE from the cases API (overdue filter) — it is not stored, so a
 * case stays in the center for as long as it remains overdue (open and no
 * recent follow-up). "Mark all read" is SESSION-SCOPED: it hides the red badge
 * for this login only. On logout the read set is cleared, so the next login
 * shows the badge again for any case still overdue. Marking a single case read
 * is not supported — the unit of acknowledgement is the whole session.
 */
@Injectable({ providedIn: 'root' })
export class NotificationStateService {
  private readonly caseService = inject(CaseService);
  private readonly callLogService = inject(CallLogService);

  /** Live list of overdue cases (recomputed on refresh). */
  readonly overdue = signal<OverdueCase[]>([]);
  /** True when the badge should be suppressed for this session. */
  readonly readDismissed = signal<boolean>(this.loadDismissed());
  /** Whether a refresh is in flight. */
  readonly loading = signal(false);

  /** Number of overdue cases still shown (0 when dismissed this session). */
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
          daysOverdue: this.computeDaysOverdue(c.followUpDueUtc),
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

  /** Marks the whole session as read (badge hidden until next login). */
  dismissAll(): void {
    this.readDismissed.set(true);
    this.saveDismissed(true);
    this.recompute();
  }

  /** Restores the badge (used on logout / new session). */
  reset(): void {
    this.readDismissed.set(false);
    this.saveDismissed(false);
    this.recompute();
  }

  private recompute(): void {
    this.visibleCount.set(this.readDismissed() ? 0 : this.overdue().length);
  }

  private computeDaysOverdue(dueUtc: string | null): number {
    if (!dueUtc) return 0;
    const due = new Date(dueUtc).getTime();
    const now = Date.now();
    const days = Math.ceil((now - due) / 86_400_000);
    return days < 1 ? 1 : days;
  }

  private loadDismissed(): boolean {
    return sessionStorage.getItem(READ_KEY) === '1';
  }

  private saveDismissed(v: boolean): void {
    if (v) sessionStorage.setItem(READ_KEY, '1');
    else sessionStorage.removeItem(READ_KEY);
  }
}
