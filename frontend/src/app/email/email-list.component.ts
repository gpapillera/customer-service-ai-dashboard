import { Component, computed, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { CsIconComponent } from '../shared/cs-icon.component';
import { EmailLogService } from './email-log.service';
import { Notification } from '../shared/models';
import { LayoutComponent } from '../shared/layout/layout.component';

/** Type label helper for the table. */
const TYPE_LABELS: Record<string, string> = {
  CaseOverdue: 'Overdue reminder',
  CaseResolved: 'Resolved confirmation',
  CustomerInvite: 'Customer invite',
  CustomerPasswordReset: 'Customer password reset',
  NewCustomerMessage: 'New customer message',
  StaffPasswordReset: 'Staff password reset',
};

/** Status pill helper. */
const STATUS_LABELS: Record<string, string> = {
  Unread: 'Sent',
  Read: 'Read',
};

/**
 * Admin-facing email log page. Shows every email the system has sent
 * (overdue reminders, password resets, resolved confirmations, customer
 * invites) in a clean table — recipient, subject, type, status, timestamp.
 * This is a read-only audit view, not an outbox. Emails are persisted by
 * EmailNotificationSender and served via GET /api/emails.
 */
@Component({
  selector: 'app-email-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    CsIconComponent,
  ],
  templateUrl: './email-list.component.html',
  styleUrl: './email-list.component.scss',
})
export class EmailListComponent implements OnInit {
  private readonly service = inject(EmailLogService);

  /** Sidenav open state — brand logo hidden when open. */
  readonly sidenavOpen = inject(LayoutComponent).opened;
  /** True only during explicit sidenav toggle for brand logo animation. */
  readonly brandAnimate = inject(LayoutComponent).brandAnimate;

  /** The full email log, newest first. */
  readonly emails = signal<Notification[]>([]);
  /** True while the initial load is in flight. */
  readonly loading = signal(false);
  /** Error message, if the fetch failed. */
  readonly error = signal<string | null>(null);

  /** Search text filter. */
  readonly searchTerm = signal('');
  /** Selected notification type filter ('' = all). */
  readonly filterType = signal('');

  /** Unique type options for the filter dropdown. */
  readonly typeOptions = computed(() => {
    const all = this.emails().map((e) => e.type);
    return [...new Set(all)].sort();
  });

  /** Emails filtered by search term and type. */
  readonly filteredEmails = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    const type = this.filterType();
    return this.emails().filter((e) => {
      if (type && e.type !== type) return false;
      if (!term) return true;
      return (
        (e.recipient ?? '').toLowerCase().includes(term) ||
        e.title.toLowerCase().includes(term)
      );
    });
  });

  ngOnInit(): void {
    this.load();
  }

  /** Clears the type filter. */
  clearTypeFilter(): void {
    this.filterType.set('');
  }

  /** Fetches the email log. */
  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getAll().subscribe({
      next: (list) => {
        this.emails.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.message ?? 'Failed to load email log.');
        this.loading.set(false);
      },
    });
  }

  /** Human-readable label for a notification type. */
  typeLabel(type: string): string {
    return TYPE_LABELS[type] ?? type;
  }

  /** Human-readable label for a notification status. */
  statusLabel(status: string): string {
    return STATUS_LABELS[status] ?? status;
  }

  /** CSS class for the status pill. */
  statusClass(status: string): string {
    return 'status-' + status.toLowerCase();
  }

  /** CSS class for the type badge. */
  typeClass(type: string): string {
    return 'type-' + type.toLowerCase();
  }

  /** Formats a UTC ISO string into a locale-friendly date+time. */
  formatDate(utc: string): string {
    const d = new Date(utc);
    return d.toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}
