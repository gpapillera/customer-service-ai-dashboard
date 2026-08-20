import { Component, computed, DestroyRef, HostListener, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { CustomerFormComponent } from './customer-form.component';
import { CaseFormComponent, CaseFormDialogData } from '../cases/case-form.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../shared/confirm-dialog.component';
import { Customer, Case, Notification, CustomerActivityItem } from '../shared/models';
import { AuthService } from '../auth/auth.service';
import { RestoreCasePickerComponent, RestoreCasePickerData } from './restore-case-picker.component';
import {
  DatePreset, DATE_PRESETS, DATE_PRESET_LABELS, filterByDatePreset, datePresetNeedsInput,
} from '../shared/date-filter';

/** Human-readable labels for notification/email types (mirrors case-detail). */
const EMAIL_TYPE_LABELS: Record<string, string> = {
  CaseOverdue: 'Overdue reminder',
  CaseResolved: 'Resolved confirmation',
  CustomerInvite: 'Customer invite',
  CustomerPasswordReset: 'Customer password reset',
  NewCustomerMessage: 'New customer message',
  StaffPasswordReset: 'Staff password reset',
  AdminManual: 'Manual email',
};

/**
 * Customer detail view: profile info plus the customer's case history and a
 * case-detail-style Emails / Activity side panel. The panel merges case-level
 * events (logs, comments, case emails) with account-level events (invites,
 * password resets, activation) so a customer with no cases still shows their
 * real recent activity.
 */
@Component({
  selector: 'app-customer-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatSelectModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './customer-detail.component.html',
  styleUrl: './customer-detail.component.scss',
})
export class CustomerDetailComponent implements OnInit {
  private readonly service = inject(CustomerService);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);

  readonly customer = signal<Customer | null>(null);
  readonly cases = signal<Case[]>([]);
  readonly loading = signal(true);
  /** True when reached via /customers/:id?deleted=1 (recycle-bin detail view). */
  readonly deleted = signal(false);
  /** True once the loaded customer has been purged (PII erased, not restorable). */
  readonly isPurged = computed(() => this.customer()?.purged === true);

  // ── Emails / Activity side panel (mirrors the case detail page) ──
  /** Full email log for this customer (account + case), newest first. */
  readonly emails = signal<Notification[]>([]);
  /** Full merged timeline for this customer (case + account), newest first. */
  readonly activity = signal<CustomerActivityItem[]>([]);
  readonly panelOpen = signal(false);
  readonly panelMode = signal<'email' | 'activity'>('activity');
  readonly searchVisible = signal(false);
  readonly dateVisible = signal(false);
  readonly emailSearch = signal('');
  readonly activitySearch = signal('');
  readonly emailDatePreset = signal<DatePreset>('all');
  readonly emailDateFrom = signal('');
  readonly emailDateTo = signal('');
  readonly emailDateSingle = signal('');
  readonly activityDatePreset = signal<DatePreset>('all');
  readonly activityDateFrom = signal('');
  readonly activityDateTo = signal('');
  readonly activityDateSingle = signal('');
  readonly closing = signal(false);

  readonly filteredEmails = computed(() => {
    let list = this.emails();
    const term = this.emailSearch().toLowerCase().trim();
    if (term) {
      list = list.filter((e) =>
        (e.title ?? '').toLowerCase().includes(term) ||
        (e.message ?? '').toLowerCase().includes(term) ||
        (e.recipient ?? '').toLowerCase().includes(term),
      );
    }
    if (this.emailDatePreset() !== 'all') {
      list = filterByDatePreset(list, this.emailDatePreset(), (e) => e.createdAtUtc,
        this.emailDateFrom(), this.emailDateTo(), this.emailDateSingle());
    }
    return list;
  });

  readonly filteredActivity = computed(() => {
    let list = this.activity();
    const term = this.activitySearch().toLowerCase().trim();
    if (term) {
      list = list.filter((a) =>
        (a.label ?? '').toLowerCase().includes(term) ||
        (a.detail ?? '').toLowerCase().includes(term) ||
        (a.who ?? '').toLowerCase().includes(term),
      );
    }
    if (this.activityDatePreset() !== 'all') {
      list = filterByDatePreset(list, this.activityDatePreset(), (a) => a.atUtc,
        this.activityDateFrom(), this.activityDateTo(), this.activityDateSingle());
    }
    return list;
  });

  readonly datePresets = DATE_PRESETS;
  readonly datePresetLabels = DATE_PRESET_LABELS;
  datePresetNeedsInput = datePresetNeedsInput;

  ngOnInit(): void {
    // Recycle-bin entry sets ?deleted=1 -> read-only deleted-mode view.
    this.deleted.set(this.route.snapshot.queryParamMap.get('deleted') === '1');
    this.load();
  }

  /** Loads the customer, their case history, and the email/activity feeds. */
  private load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loading.set(true);
    this.service.get(id).subscribe({
      next: (c) => {
        this.customer.set(c);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
    this.loadCases();
    this.loadPanelData();
  }

  /** Loads the email + activity feeds for the panel (independent of the profile load). */
  private loadPanelData(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.service.customerEmails(id).subscribe((list) => this.emails.set(list));
    this.service.customerActivity(id).subscribe((list) => this.activity.set(list));
    // Record this open as a "viewed" activity row (staff only). The backend
    // coalesces repeats by a 10-min per-viewer cooldown, so re-opening/refreshing
    // within that window won't add a second row. Re-fetch the activity feed so
    // the new "Viewed" entry appears in the panel immediately.
    const role = this.auth.getRole();
    if (role === 'Agent' || role === 'Admin') {
      this.service.recordView(id).subscribe({
        next: () => this.service.customerActivity(id).subscribe((list) => this.activity.set(list)),
        error: () => { /* audit is best-effort; never block the panel */ },
      });
    }
  }

  /** Opens the new-case modal directly on this page, locked to this customer. */
  newCase(): void {
    const id = this.customer()?.id;
    if (!id) return;
    const data: CaseFormDialogData = { customerId: id };
    const ref = this.dialog.open(CaseFormComponent, {
      data,
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((savedId) => {
      if (savedId) this.loadCases();
    });
  }

  /** Reloads only the case history for this customer (in place). */
  private loadCases(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.service.customerCases(id).subscribe((list) => {
      this.cases.set(list);
    });
  }

  /** Opens the edit-customer modal, prefilled, and refreshes in place on save. */
  edit(): void {
    const id = this.customer()?.id;
    if (!id) return;
    const ref = this.dialog.open(CustomerFormComponent, {
      data: id,
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((savedId) => {
      if (savedId) this.load();
    });
  }

  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }

  /** Restores a soft-deleted customer from the recycle bin, with a case-picker. */
  restoreCustomer(): void {
    const c = this.customer();
    if (!c || this.isPurged()) return;
    const ref = this.dialog.open<RestoreCasePickerComponent, RestoreCasePickerData, number[] | null>(
      RestoreCasePickerComponent,
      {
        data: { customerId: c.id, customerName: c.name },
        width: '480px',
        maxWidth: '92vw',
        autoFocus: false,
      },
    );
    ref.afterClosed().subscribe((chosen) => {
      if (chosen === null) return; // cancelled
      this.service.restore(c.id, chosen).subscribe({
        next: () => this.router.navigate(['/customers', c.id]),
        error: () => { /* surface via a toast later */ },
      });
    });
  }

  /** Permanently purges a soft-deleted customer after confirmation. */
  purgeCustomer(): void {
    const c = this.customer();
    if (!c || this.isPurged()) return;
    const ref = this.dialog.open<ConfirmDialogComponent, ConfirmDialogData, boolean>(
      ConfirmDialogComponent,
      {
        data: {
          title: 'Permanently erase customer',
          message: `Erase ${c.name}'s data? This scrubs all personal info and cannot be undone.`,
          confirmText: 'Erase',
          cancelText: 'Cancel',
          icon: 'delete_forever',
        },
        width: '420px',
        maxWidth: '92vw',
        autoFocus: false,
      },
    );
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.service.purge(c.id).subscribe({
          next: () => this.router.navigate(['/customers']),
          error: () => { /* surface via a toast later */ },
        });
      }
    });
  }

  /** Deletes the customer after a confirmation dialog. */
  deleteCustomer(): void {
    const c = this.customer();
    if (!c) return;
    const ref = this.dialog.open<
      ConfirmDialogComponent,
      ConfirmDialogData,
      boolean
    >(ConfirmDialogComponent, {
      data: {
        title: 'Delete customer',
        message: `Delete customer '${c.name}'${c.caseCount > 0 ? ` (${c.caseCount} case${c.caseCount !== 1 ? 's' : ''})` : ''}? This moves them to the recycle bin, where they can be restored.`,
        confirmText: 'Delete',
        cancelText: 'Cancel',
        icon: 'delete',
      },
      width: '400px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.service.delete(c.id).subscribe(() => {
          this.router.navigate(['/customers']);
        });
      }
    });
  }

  // ── Panel machinery (mirrors the case detail page) ──

  togglePanel(): void {
    if (this.panelOpen()) {
      this.closePanel();
    } else {
      this.closing.set(false);
      this.panelOpen.set(true);
    }
  }

  closePanel(): void {
    if (!this.panelOpen() || this.closing()) return;
    this.closing.set(true);
  }

  onPanelAnimationEnd(event?: AnimationEvent): void {
    if (event && event.animationName !== 'panel-slide-out') return;
    if (!this.closing()) return;
    this.closing.set(false);
    this.panelOpen.set(false);
  }

  setPanelMode(mode: 'email' | 'activity'): void {
    this.panelMode.set(mode);
  }

  toggleSearch(): void {
    this.searchVisible.update((v) => !v);
  }

  toggleDate(): void {
    this.dateVisible.update((v) => !v);
  }

  resetFilters(): void {
    this.emailSearch.set('');
    this.emailDatePreset.set('all');
    this.emailDateFrom.set('');
    this.emailDateTo.set('');
    this.emailDateSingle.set('');
    this.activitySearch.set('');
    this.activityDatePreset.set('all');
    this.activityDateFrom.set('');
    this.activityDateTo.set('');
    this.activityDateSingle.set('');
    this.searchVisible.set(false);
    this.dateVisible.set(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.panelOpen()) return;
    const target = event.target as HTMLElement | null;
    if (
      target?.closest('#history-panel') ||
      target?.closest('.history-toggle') ||
      target?.closest('.cdk-overlay-container')
    ) {
      return;
    }
    this.closePanel();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.panelOpen()) this.closePanel();
  }

  /** Human label for an email type. */
  typeLabel(type: string): string {
    return EMAIL_TYPE_LABELS[type] ?? type;
  }

  /** Picks the Material icon for an activity kind (case + account events). */
  activityIcon(kind: CustomerActivityItem['kind']): string {
    switch (kind) {
      case 'opened': return 'check_circle';
      case 'updated': return 'schedule';
      case 'resolved': return 'task_alt';
      case 'log': return 'phone';
      case 'comment': return 'forum';
      case 'email': return 'mail';
      case 'account_invite': return 'mail';
      case 'account_reset': return 'lock_reset';
      case 'account_activated': return 'verified_user';
      case 'account_updated': return 'edit';
      case 'viewed': return 'visibility';
      default: return 'circle';
    }
  }

  formatDate(value?: string): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString();
  }

  /** Formats a UTC date as "MMM DD, HH:MM AM/PM" (panel timestamps). */
  formatDateTime(value?: string): string {
    if (!value) return '—';
    const d = new Date(value);
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) +
      ', ' + d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
  }
}
