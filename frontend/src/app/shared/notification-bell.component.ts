import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CsIconComponent } from './cs-icon.component';
import { NotificationStateService } from './notification-state.service';
import { OverdueCase, CallLog } from './models';

/**
 * In-app notification bell. The list of "needs follow-up" cases is computed
 * LIVE from the cases API (overdue filter) — it is not stored, so a case stays
 * in the center for as long as it remains overdue. Opening the bell shows a
 * modal listing every overdue case (title, customer, priority). Clicking a row
 * expands it inline to show category, description, how long it has sat, the
 * follow-up log, and a button to open the full case page. "Mark all read"
 * dismisses the red badge for this session only; on logout the read state is
 * cleared so the badge returns for any case still overdue.
 */
@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [CommonModule, CsIconComponent],
  templateUrl: './notification-bell.component.html',
  styleUrl: './notification-bell.component.scss',
})
export class NotificationBellComponent implements OnInit {
  private readonly router = inject(Router);
  protected readonly state = inject(NotificationStateService);

  /** Whether the modal is open. */
  readonly open = signal(false);
  /** Case id whose detail row is expanded (null = collapsed list). */
  readonly expandedId = signal<number | null>(null);
  /** Call logs loaded for the expanded case. */
  readonly logs = signal<CallLog[]>([]);
  /** Whether the detail for the expanded case is loading. */
  readonly loadingDetail = signal(false);

  ngOnInit(): void {
    this.state.refresh();
  }

  /** Opens the modal and refreshes the live list. */
  openModal(): void {
    this.open.set(true);
    this.expandedId.set(null);
    this.state.refresh();
  }

  /** Closes the modal (badge persists for still-overdue cases). */
  close(): void {
    this.open.set(false);
    this.expandedId.set(null);
  }

  /** Toggles the expanded detail row for a case. */
  toggleExpand(item: OverdueCase): void {
    if (this.expandedId() === item.caseId) {
      this.expandedId.set(null);
      return;
    }
    this.expandedId.set(item.caseId);
    this.loadingDetail.set(true);
    this.logs.set([]);
    this.state.loadDetail(item.caseId).subscribe({
      next: (l) => {
        this.logs.set(l);
        this.loadingDetail.set(false);
      },
      error: () => this.loadingDetail.set(false),
    });
  }

  /** True when the row is expanded. */
  isExpanded(id: number): boolean {
    return this.expandedId() === id;
  }

  /** Dismisses the badge for this session. */
  markAll(): void {
    this.state.dismissAll();
  }

  /** Opens the full case detail page and closes the modal. */
  openCase(item: OverdueCase): void {
    this.close();
    this.router.navigateByUrl(`/cases/${item.caseId}`);
  }

  /** Priority pill class. */
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }

  /** Status pill class. */
  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  /** Formats a UTC date string for display. */
  formatDate(value: string | null): string {
    if (!value) return '—';
    return new Date(value).toLocaleString();
  }
}
