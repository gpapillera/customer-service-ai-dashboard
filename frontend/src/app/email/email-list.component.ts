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
  AdminManual: 'Manual email',
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
  /** Email selected for detail overlay. */
  readonly selectedEmail = signal<Notification | null>(null);
  /** True while the initial load is in flight. */
  readonly loading = signal(false);
  /** Error message, if the fetch failed. */
  readonly error = signal<string | null>(null);

  /** Search text filter. */
  readonly searchTerm = signal('');
  /** Selected notification type filter ('' = all). */
  readonly filterType = signal('');

  /** Sort column. */
  readonly sortColumn = signal<'date' | 'recipient' | 'subject' | 'type' | 'status'>('date');
  /** Sort direction — true = descending (newest first for dates). */
  readonly sortDesc = signal(true);

  /** Unique type options for the filter dropdown. */
  readonly typeOptions = computed(() => {
    const all = this.emails().map((e) => e.type);
    return [...new Set(all)].sort();
  });

  /** Emails filtered by search term and type, then sorted by column. */
  readonly filteredEmails = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    const type = this.filterType();
    const col = this.sortColumn();
    const desc = this.sortDesc();

    let result = this.emails().filter((e) => {
      if (type && e.type !== type) return false;
      if (!term) return true;
      return (
        (e.recipient ?? '').toLowerCase().includes(term) ||
        e.title.toLowerCase().includes(term) ||
        e.message.toLowerCase().includes(term) ||
        this.typeLabel(e.type).toLowerCase().includes(term) ||
        this.statusLabel(e.status).toLowerCase().includes(term) ||
        (e.caseId?.toString() ?? '').includes(term)
      );
    });

    // Apply sort
    return [...result].sort((a, b) => {
      let cmp = 0;
      switch (col) {
        case 'date':
          cmp = new Date(a.createdAtUtc).getTime() - new Date(b.createdAtUtc).getTime();
          break;
        case 'recipient':
          cmp = (a.recipient ?? '').localeCompare(b.recipient ?? '');
          break;
        case 'subject':
          cmp = a.title.localeCompare(b.title);
          break;
        case 'type':
          cmp = this.typeLabel(a.type).localeCompare(this.typeLabel(b.type));
          break;
        case 'status':
          cmp = this.statusLabel(a.status).localeCompare(this.statusLabel(b.status));
          break;
      }
      return desc ? -cmp : cmp;
    });
  });

  ngOnInit(): void {
    this.load();
  }

  /** Clears the type filter. */
  clearTypeFilter(): void {
    this.filterType.set('');
  }

  /** Opens the email detail overlay. */
  openEmail(email: Notification): void {
    this.selectedEmail.set(email);
  }

  /** Closes the email detail overlay. */
  close(): void {
    this.selectedEmail.set(null);
  }

  /** Toggle sort for a column — reverses direction or switches column. */
  toggleSort(column: string): void {
    const col = column as 'date' | 'recipient' | 'subject' | 'type' | 'status';
    if (this.sortColumn() === col) {
      this.sortDesc.update((d) => !d);
    } else {
      this.sortColumn.set(col);
      // Default: newest-first for dates, A-Z for text
      this.sortDesc.set(col === 'date');
    }
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

  // ── Compose email panel ──

  readonly showCompose = signal(false);
  readonly composeRecipient = signal('');
  readonly composeSubject = signal('');
  readonly composeMessage = signal('');
  readonly composeCaseId = signal<number | undefined>(undefined);
  readonly composeSending = signal(false);
  readonly composeError = signal<string | null>(null);
  readonly composeSuccess = signal(false);

  /** Opens the compose panel and resets the form. */
  openCompose(): void {
    this.showCompose.set(true);
    this.composeRecipient.set('');
    this.composeSubject.set('');
    this.composeMessage.set('');
    this.composeCaseId.set(undefined);
    this.composeSending.set(false);
    this.composeError.set(null);
    this.composeSuccess.set(false);
  }

  /** Closes the compose panel. */
  closeCompose(): void {
    this.showCompose.set(false);
  }

  /** Submits the compose form. */
  submitCompose(): void {
    const recipient = this.composeRecipient().trim();
    const subject = this.composeSubject().trim();
    const message = this.composeMessage().trim();

    if (!recipient || !subject || !message) {
      this.composeError.set('Please fill in recipient, subject, and message.');
      return;
    }

    this.composeSending.set(true);
    this.composeError.set(null);
    this.composeSuccess.set(false);

    this.service.compose({
      recipient,
      subject,
      message,
      caseId: this.composeCaseId() || undefined,
    }).subscribe({
      next: () => {
        this.composeSending.set(false);
        this.composeSuccess.set(true);
        // Reload the email list to include the new entry
        this.load();
        // Auto-close after a brief moment
        setTimeout(() => this.closeCompose(), 1500);
      },
      error: (err) => {
        this.composeSending.set(false);
        this.composeError.set(err?.error ?? err?.message ?? 'Failed to send email.');
      },
    });
  }
}
