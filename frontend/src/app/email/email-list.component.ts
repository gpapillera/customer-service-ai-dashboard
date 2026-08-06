import {
  Component,
  afterNextRender,
  computed,
  ElementRef,
  EnvironmentInjector,
  HostListener,
  OnDestroy,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { CsIconComponent } from '../shared/cs-icon.component';
import { EmailLogService } from './email-log.service';
import { EmailConfigService } from './email-config.service';
import { AuthService } from '../auth/auth.service';
import {
  EmailConfigBundleDto,
  EmailDomainDto,
  EmailTemplateDto,
  Notification,
} from '../shared/models';
import { LayoutComponent } from '../shared/layout/layout.component';
import { DatePreset, DATE_PRESETS, formatDatePreset, filterByDatePreset, positionHeaderDropdown } from '../shared/date-filter';

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
export class EmailListComponent implements OnInit, OnDestroy {
  private readonly service = inject(EmailLogService);
  private readonly emailConfigService = inject(EmailConfigService);
  private readonly auth = inject(AuthService);
  private readonly elRef = inject(ElementRef);
  private readonly envInjector = inject(EnvironmentInjector);
  /** True while the scroll/resize watch for the date dropdown is attached. */
  private dropdownWatchAttached = false;

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
  /** True while a resend request is in flight. */
  readonly resending = signal(false);
  /** Error message, if the fetch failed. */
  readonly error = signal<string | null>(null);

  /** Search text filter. */
  readonly searchTerm = signal('');
  /** Selected notification type filter ('' = all). */
  readonly filterType = signal('');

  /** Date filter preset for the "Date" column (same presets as Conversations). */
  readonly dateFilterPreset = signal<DatePreset>('all');
  /** Custom range start (YYYY-MM-DD) — only used when preset is 'custom'. */
  readonly customDateFrom = signal('');
  /** Custom range end (YYYY-MM-DD) — only used when preset is 'custom'. */
  readonly customDateTo = signal('');
  /** Single date input (YYYY-MM-DD) — used by the before/after/on-or-before/on-or-after presets. */
  readonly customDateSingle = signal('');
  /** Preset options for the Date header filter dropdown. */
  readonly datePresets = DATE_PRESETS;
  /** Labels a date preset key for display. */
  readonly datePresetLabel = formatDatePreset;
  /** Track which table-header filter dropdown is open, or null. */
  readonly openHeaderFilter = signal<string | null>(null);

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

    // Apply date filter (conversations-style presets on the Date column).
    result = filterByDatePreset(
      result,
      this.dateFilterPreset(),
      (e) => e.createdAtUtc,
      this.customDateFrom(),
      this.customDateTo(),
      this.customDateSingle(),
    );

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

  /** Clears the search text. */
  clearSearch(): void {
    this.searchTerm.set('');
  }

  /** Toggle a header filter dropdown open/closed. */
  toggleHeaderFilter(col: string): void {
    const next = this.openHeaderFilter() === col ? null : col;
    this.openHeaderFilter.set(next);
    if (next) {
      this.attachDropdownScrollWatch();
      // The dropdown is inside an @if, so it only exists after Angular
      // renders. Schedule the placement for after that render.
      afterNextRender(() => this.applyHeaderDropdownPlacement(), {
        injector: this.envInjector,
      });
    } else {
      this.detachDropdownScrollWatch();
    }
  }

  /** Sets the date filter preset from the Date header dropdown. */
  setDatePreset(preset: DatePreset): void {
    this.dateFilterPreset.set(preset);
    // Close the dropdown for presets that don't need date inputs; keep it
    // open for date-requiring presets so the user can type dates inline.
    if (preset === 'all' || preset === 'today' || preset === '7days' || preset === '30days') {
      this.openHeaderFilter.set(null);
      this.detachDropdownScrollWatch();
    } else {
      // Date inputs appear/disappear → height changes → re-place the popup
      // after the render that adds/removes them.
      afterNextRender(() => this.applyHeaderDropdownPlacement(), {
        injector: this.envInjector,
      });
    }
  }

  /** Resets the date filter back to "All time" and closes the dropdown. */
  resetDateFilter(): void {
    this.dateFilterPreset.set('all');
    this.customDateFrom.set('');
    this.customDateTo.set('');
    this.customDateSingle.set('');
    this.openHeaderFilter.set(null);
    this.detachDropdownScrollWatch();
  }

  /** Set a filter value from a header dropdown and close it. */
  setHeaderFilter(col: string, value: string | number | null): void {
    this.openHeaderFilter.set(null);
    this.detachDropdownScrollWatch();
    if (col === 'type') {
      this.filterType.set((value as string) ?? '');
    }
  }

  /** Reset the type filter and close the dropdown. */
  resetTypeFilter(): void {
    this.filterType.set('');
    this.openHeaderFilter.set(null);
    this.detachDropdownScrollWatch();
  }

  /**
   * Places the open header-filter dropdown with `position: fixed`, clamped to
   * the visible area so it is never clipped by the (now short) table wrapper.
   */
  private applyHeaderDropdownPlacement(): void {
    const dd = this.elRef.nativeElement.querySelector(
      '.header-filter-dropdown',
    ) as HTMLElement | null;
    if (!dd) return;
    const scrollRoot = (document.querySelector('.content') as HTMLElement | null) ?? document.body;
    positionHeaderDropdown(dd, scrollRoot);
  }

  /** Re-place on scroll/resize so the fixed popup stays glued to the funnel. */
  private readonly onDropdownViewportChange = (): void => {
    if (this.openHeaderFilter() !== null) this.applyHeaderDropdownPlacement();
  };

  private attachDropdownScrollWatch(): void {
    if (this.dropdownWatchAttached) return;
    this.dropdownWatchAttached = true;
    const root = (document.querySelector('.content') as HTMLElement | null) ?? window;
    root.addEventListener('scroll', this.onDropdownViewportChange, { passive: true });
    window.addEventListener('resize', this.onDropdownViewportChange);
  }

  private detachDropdownScrollWatch(): void {
    if (!this.dropdownWatchAttached) return;
    this.dropdownWatchAttached = false;
    const root = (document.querySelector('.content') as HTMLElement | null) ?? window;
    root.removeEventListener('scroll', this.onDropdownViewportChange);
    window.removeEventListener('resize', this.onDropdownViewportChange);
  }

  ngOnDestroy(): void {
    this.detachDropdownScrollWatch();
  }

  /** Updates a custom-date input (From/To/single). The filteredEmails
      computed re-filters reactively, so no manual reload is needed. */
  onCustomDateChange(field: 'from' | 'to' | 'single', value: string): void {
    if (field === 'from') this.customDateFrom.set(value);
    else if (field === 'to') this.customDateTo.set(value);
    else this.customDateSingle.set(value);
  }

  /** Close the header filter dropdown when clicking outside. */
  @HostListener('document:click')
  closeHeaderFilter(): void {
    this.detachDropdownScrollWatch();
    this.openHeaderFilter.set(null);
  }

  /** Opens the email detail overlay. */
  openEmail(email: Notification): void {
    this.selectedEmail.set(email);
  }

  /** Closes the email detail overlay. */
  close(): void {
    this.selectedEmail.set(null);
  }

  /** Re-sends the selected email to its original recipient (Admin only). */
  resend(email: Notification): void {
    if (this.resending()) return;
    this.resending.set(true);
    this.service.resend(email.id).subscribe({
      next: () => {
        this.resending.set(false);
        this.load();
      },
      error: () => {
        this.resending.set(false);
      },
    });
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

  /** True when the current user is an Admin (sees the config button). */
  readonly isAdmin = this.auth.getRole() === 'Admin';

  // ── Email configuration side panel (Admin) ──

  /** Personalization tokens the user can insert into templates. */
  readonly TEMPLATE_TOKENS = [
    '{{customerName}}',
    '{{customerEmail}}',
    '{{caseId}}',
    '{{caseSubject}}',
    '{{caseStatus}}',
    '{{agentName}}',
    '{{agentEmail}}',
    '{{portalLink}}',
  ];

  /** True when the config panel is open. */
  readonly showConfig = signal(false);
  /** Config bundle (config + domains + templates + suggestions). */
  readonly configBundle = signal<EmailConfigBundleDto | null>(null);
  /** True while the bundle is loading. */
  readonly configLoading = signal(false);
  /** Error message for config operations. */
  readonly configError = signal<string | null>(null);
  /** Per-section transient success message. */
  readonly configSaved = signal<string | null>(null);

  /** Working copy of the test email (edited in the panel). */
  readonly draftTestEmail = signal('');
  /** New-domain input. */
  readonly newDomain = signal('');
  /** Editing template key (type) or null. */
  readonly editingTemplateType = signal<string | null>(null);
  /** Draft template subject/body for the selected type. */
  readonly draftTemplateSubject = signal('');
  readonly draftTemplateBody = signal('');

  /** Opens the config panel and loads the current bundle. */
  openConfig(): void {
    this.showConfig.set(true);
    this.configError.set(null);
    this.configSaved.set(null);
    this.configLoading.set(true);
    this.emailConfigService.getBundle().subscribe({
      next: (bundle) => {
        this.configBundle.set(bundle);
        this.draftTestEmail.set(bundle.config.testEmailAddress);
        this.configLoading.set(false);
      },
      error: (err) => {
        this.configError.set(err?.error ?? err?.message ?? 'Failed to load email config.');
        this.configLoading.set(false);
      },
    });
  }

  /** Closes the config panel. */
  closeConfig(): void {
    this.showConfig.set(false);
    this.editingTemplateType.set(null);
  }

  /** Saves the test-email address. */
  saveTestEmail(): void {
    const email = this.draftTestEmail().trim();
    if (!email) {
      this.configError.set('Test email is required.');
      return;
    }
    this.configError.set(null);
    this.emailConfigService.updateTestEmail(email).subscribe({
      next: (cfg) => {
        this.draftTestEmail.set(cfg.testEmailAddress);
        const b = this.configBundle();
        if (b) this.configBundle.set({ ...b, config: cfg });
        this.configSaved.set('Test email updated.');
      },
      error: (err) => this.configError.set(err?.error ?? err?.message ?? 'Failed to update test email.'),
    });
  }

  /** Adds a new allowed domain. */
  addDomain(): void {
    const domain = this.newDomain().trim().toLowerCase();
    if (!domain) return;
    this.configError.set(null);
    this.emailConfigService.addDomain({ domain }).subscribe({
      next: (dto) => {
        const b = this.configBundle();
        if (b) this.configBundle.set({ ...b, domains: [...b.domains, dto] });
        this.newDomain.set('');
        this.configSaved.set(`Added domain ${dto.domain}.`);
      },
      error: (err) => this.configError.set(err?.error ?? err?.message ?? 'Failed to add domain.'),
    });
  }

  /** Removes an allowed domain by id. */
  removeDomain(id: number): void {
    this.configError.set(null);
    this.emailConfigService.removeDomain(id).subscribe({
      next: () => {
        const b = this.configBundle();
        if (b) this.configBundle.set({ ...b, domains: b.domains.filter((d) => d.id !== id) });
        this.configSaved.set('Domain removed.');
      },
      error: (err) => this.configError.set(err?.error ?? err?.message ?? 'Failed to remove domain.'),
    });
  }

  /** Adds a suggested known domain from the quick-add list. */
  addSuggestedDomain(domain: string): void {
    const existing = this.configBundle()?.domains ?? [];
    if (existing.some((d) => d.domain.toLowerCase() === domain.toLowerCase())) return;
    this.newDomain.set(domain);
    this.addDomain();
  }

  /** Begins editing a template for a given type. */
  editTemplate(t: EmailTemplateDto): void {
    this.editingTemplateType.set(t.type);
    this.draftTemplateSubject.set(t.subject);
    this.draftTemplateBody.set(t.body);
  }

  /** Cancels template editing. */
  cancelTemplateEdit(): void {
    this.editingTemplateType.set(null);
  }

  /** Inserts a token at the end of the focused draft (subject or body). */
  insertToken(token: string, field: 'subject' | 'body'): void {
    if (field === 'subject') {
      this.draftTemplateSubject.update((v) => v + token);
    } else {
      this.draftTemplateBody.update((v) => v + token);
    }
  }

  /** Saves the currently-edited template. */
  saveTemplate(): void {
    const type = this.editingTemplateType();
    if (!type) return;
    this.configError.set(null);
    this.emailConfigService
      .upsertTemplate({ type, subject: this.draftTemplateSubject(), body: this.draftTemplateBody() })
      .subscribe({
        next: (saved) => {
          const b = this.configBundle();
          if (b) {
            const others = b.templates.filter((t) => t.type !== type);
            this.configBundle.set({ ...b, templates: [...others, saved].sort((a, c) => a.type.localeCompare(c.type)) });
          }
          this.editingTemplateType.set(null);
          this.configSaved.set(`Template "${type}" saved.`);
        },
        error: (err) => this.configError.set(err?.error ?? err?.message ?? 'Failed to save template.'),
      });
  }

  /** Deletes a template by id. */
  deleteTemplate(id: number): void {
    this.configError.set(null);
    this.emailConfigService.deleteTemplate(id).subscribe({
      next: () => {
        const b = this.configBundle();
        if (b) this.configBundle.set({ ...b, templates: b.templates.filter((t) => t.id !== id) });
        this.configSaved.set('Template deleted.');
      },
      error: (err) => this.configError.set(err?.error ?? err?.message ?? 'Failed to delete template.'),
    });
  }

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
