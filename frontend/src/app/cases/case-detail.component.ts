import { Component, computed, DestroyRef, ElementRef, HostListener, inject, OnInit, signal, ViewChild, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink, Router, NavigationStart } from '@angular/router';
import { FormBuilder, FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { interval, Subscription } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CsTooltipDirective } from '../shared/tooltip.directive';
import { TooltipData } from '../shared/tooltip-data';
import { NavBadgeService } from '../shared/nav-badge.service';
import { CaseService } from './case.service';
import { CallLogService } from './call-log.service';
import { EmailLogService } from '../email/email-log.service';
import { CaseFormComponent, CaseFormDialogData } from './case-form.component';
import { Case, CallLog, Agent, CustomerCaseComment, Notification, ViewEvent } from '../shared/models';
import { RealtimeService } from '../shared/realtime.service';
import { DatePreset, DATE_PRESETS, DATE_PRESET_LABELS, filterByDatePreset, datePresetNeedsInput } from '../shared/date-filter';
import { AuthService } from '../auth/auth.service';
import { SaveFlashService } from '../shared/save-flash.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../shared/confirm-dialog.component';

/** One row in the case Activity timeline: an email, call log, comment, or state change. */
export interface ActivityItem {
  key: string;
  kind: 'opened' | 'updated' | 'log' | 'comment' | 'email' | 'viewed';
  label: string;
  detail: string;
  atUtc: string;
  /** Optional secondary author/recipient shown beside the label. */
  who?: string;
}

/** Human-readable labels for notification/email types (mirrors email-list). */
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
 * Case detail: shows the case, its AI-suggested priority, and the call/follow-up
 * log with an inline form to add new entries.
 */
@Component({
  selector: 'app-case-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ReactiveFormsModule,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
    CsTooltipDirective,
  ],
  templateUrl: './case-detail.component.html',
  styleUrl: './case-detail.component.scss',
})
export class CaseDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  readonly router = inject(Router);
  private readonly caseService = inject(CaseService);
  private readonly callLogService = inject(CallLogService);
  private readonly emailLogService = inject(EmailLogService);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);
  readonly auth = inject(AuthService);
  private readonly navBadgeService = inject(NavBadgeService);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private commentsPolling: Subscription | null = null;

  readonly case = signal<Case | null>(null);
  /** When an assignment change is pushed for THIS case, re-fetch it so the
      Assignee card stays authoritative in real time (e.g. an admin unassigns
      while an agent has the case open — it flips to Unassigned instantly). */
  private readonly rtEffect = effect(() => {
    const evt = this.realtime.caseEvent();
    const c = this.case();
    if (evt && c && evt.caseId === c.id) {
      this.caseService.get(c.id).subscribe({ next: (fresh) => this.case.set(fresh) });
    }
  });
  /**
   * Reactive control backing the Assignee <mat-select>. Material's mat-select
   * does NOT reliably repaint its trigger text when bound with one-way
   * `[value]` and the underlying signal changes after init — the selected
   * label stays stale until a later cycle (looks like the change "waits").
   * Binding via a FormControl and syncing it from the case signal on every
   * change (load, local save, SSE re-GET) forces an immediate repaint, so the
   * Assignee card reflects a selection the instant it lands.
   */
  readonly assigneeControl = new FormControl<string>('', { nonNullable: true });
  private readonly assigneeSync = effect(() => {
    const c = this.case();
    const next = c?.assignedToUserId ?? '';
    if (this.assigneeControl.value !== next) {
      this.assigneeControl.setValue(next, { emitEvent: false });
    }
  });
  readonly logs = signal<CallLog[]>([]);
  readonly comments = signal<CustomerCaseComment[]>([]);
  /** Viewed/opened audit rows for this case (recorded on open; coalesced server-side). */
  readonly caseViews = signal<ViewEvent[]>([]);
  readonly loading = signal(true);
  /** Set when the case cannot be loaded (e.g. 403 for an Agent). */
  readonly loadError = signal<string | null>(null);
  /** True when reached via /cases/:id?deleted=1 (recycle-bin detail view). */
  readonly deleted = signal(false);
  /** True once the loaded case has been purged (PII scrubbed, not restorable). */
  readonly isPurged = computed(() => this.case()?.purged === true);
  /** True when the owning customer is still soft-deleted (case restore is gated). */
  readonly customerStillDeleted = computed(() => this.case()?.customerIsDeleted === true);
  /** Agents available for assignment (GET /api/users). */
  readonly agents = signal<Agent[]>([]);
  readonly assigning = signal(false);
  /** Set when an assignment PUT fails, so the admin sees it didn't save
      (instead of a silently-stale optimistic assignee). */
  readonly assignError = signal<string | null>(null);
  /** Global read-only "change saved" flash (top-of-viewport banner).
      Shared across all pages/panels via SaveFlashService. */
  private readonly saveFlash = inject(SaveFlashService);

  // ── Emails card ────────────────────────────────────────────────
  /** Full email log from the backend (newest first). Filtered client-side; no extra endpoint needed. */
  readonly emails = signal<Notification[]>([]);
  readonly emailSearch = signal('');
  readonly emailDatePreset = signal<DatePreset>('all');
  readonly emailDateFrom = signal('');
  readonly emailDateTo = signal('');
  readonly emailDateSingle = signal('');

  /** Emails tied to this case (any email sender stamps CaseId). */
  readonly caseEmails = computed(() => {
    const id = this.case()?.id;
    return id == null ? [] : this.emails().filter((e) => e.caseId === id);
  });

  /** Emails for this case, filtered by live search + date preset. */
  readonly filteredEmails = computed(() => {
    let list = this.caseEmails();
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

  // ── Activity card ─────────────────────────────────────────────
  readonly activitySearch = signal('');
  readonly activityDatePreset = signal<DatePreset>('all');
  readonly activityDateFrom = signal('');
  readonly activityDateTo = signal('');
  readonly activityDateSingle = signal('');

  // ── History side panel (replaces the old Emails/Activity side cards) ──
  /** Whether the right-side Emails/Activity panel is open. */
  readonly panelOpen = signal(false);
  /** Which list the panel shows. Only one at a time (never both). */
  readonly panelMode = signal<'email' | 'activity'>('email');
  /** Search input is revealed only after the search icon is clicked. */
  readonly searchVisible = signal(false);
  /** Date filter is revealed only after the date icon is clicked. */
  readonly dateVisible = signal(false);

  /** Merged timeline of everything done to this case, newest first. */
  readonly activity = computed<ActivityItem[]>(() => {
    const c = this.case();
    if (!c) return [];
    const items: ActivityItem[] = [];

    items.push({ key: `opened-${c.id}`, kind: 'opened', label: 'Opened', detail: `Case created`, atUtc: c.createdAtUtc });

    if (c.updatedAtUtc) {
      const statusLabel = c.status === 'New' ? c.status : `moved to ${c.status}`;
      items.push({ key: `updated-${c.updatedAtUtc}`, kind: 'updated', label: 'Updated', detail: `Status ${statusLabel}`, atUtc: c.updatedAtUtc });
    }

    for (const log of this.logs()) {
      items.push({ key: `log-${log.id}`, kind: 'log', label: log.direction, detail: log.notes, atUtc: log.createdAtUtc });
    }

    for (const comment of this.comments()) {
      const who = comment.authorDisplayName || (comment.isStaff ? 'Staff' : 'Customer');
      const what = comment.isStaff ? 'Staff comment' : 'Customer message';
      items.push({ key: `comment-${comment.id}`, kind: 'comment', label: what, detail: comment.body, atUtc: comment.createdAtUtc, who });
    }

    for (const email of this.caseEmails()) {
      items.push({ key: `email-${email.id}`, kind: 'email', label: 'Email sent', detail: email.title || email.message, atUtc: email.createdAtUtc, who: email.recipient ?? undefined });
    }

    // Viewed/opened audit rows: recorded on open, coalesced server-side by a
    // 10-min per-viewer cooldown. Shown as their own timeline kind.
    for (const v of this.caseViews()) {
      items.push({ key: `viewed-${v.id}`, kind: 'viewed', label: 'Viewed', detail: `Viewed by ${v.viewerName}`, atUtc: v.atUtc, who: v.viewerRole ?? undefined });
    }

    return items.sort((a, b) => new Date(b.atUtc).getTime() - new Date(a.atUtc).getTime());
  });

  /** Loads the viewed/opened audit rows for this case into `caseViews`. */
  private loadCaseViews(id: number): void {
    this.caseService.caseViews(id).subscribe({
      next: (views) => this.caseViews.set(views),
      error: () => { /* best-effort: activity panel simply omits views */ },
    });
  }

  /** Activity rows filtered by live search + date preset. */
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

  // Shared by the template date-filter selects.
  readonly datePresets = DATE_PRESETS;
  readonly datePresetLabels = DATE_PRESET_LABELS;
  datePresetNeedsInput = datePresetNeedsInput;

  /**
   * Whether the current user may edit this case. Admins always can. Agents may
   * only edit a case assigned to them; unassigned or other-agent cases are
   * read-only for an Agent (mirrors the server-side Phase 6 enforcement).
   */
  readonly canEdit = computed(() => {
    const c = this.case();
    if (!c) return false;
    if (this.auth.getRole() !== 'Agent') return true;
    return c.assignedToUserId === this.auth.currentUser()?.id;
  });

  readonly statuses: Case['status'][] = ['New', 'InProgress', 'Escalated', 'Resolved', 'Closed'];
  readonly priorities: Case['priority'][] = ['Low', 'Medium', 'High'];

  readonly logForm = this.fb.nonNullable.group({
    direction: ['Outbound' as CallLog['direction']],
    notes: ['', Validators.required],
    durationSeconds: [0],
  });
  readonly savingLog = signal(false);

  readonly commentForm = this.fb.nonNullable.group({
    body: ['', Validators.required],
  });
  readonly savingComment = signal(false);

  @ViewChild('chatScroll') private chatScroll!: ElementRef<HTMLDivElement>;

  /** Handle Enter/Shift+Enter on textareas for form submission. */
  onTextareaKeydown(event: KeyboardEvent, formType: 'comment' | 'log'): void {
    // Enter (without Shift) submits the form
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      if (formType === 'comment' && this.commentForm.valid && !this.savingComment()) {
        this.addComment();
      } else if (formType === 'log' && this.logForm.valid && !this.savingLog()) {
        this.addLog();
      }
      return;
    }
    // Shift+Enter inserts a new line (browser default — do nothing)
    // Ctrl+Enter also submits (backward compat)
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      if (formType === 'comment' && this.commentForm.valid && !this.savingComment()) {
        this.addComment();
      } else if (formType === 'log' && this.logForm.valid && !this.savingLog()) {
        this.addLog();
      }
    }
  }

  /** Navigate back to the cases list. */
  goBack(): void {
    this.router.navigateByUrl('/cases');
  }

  /** Restores a soft-deleted case from the recycle bin (Admin). */
  restoreCase(): void {
    const c = this.case();
    if (!c || this.isPurged() || this.customerStillDeleted()) return;
    this.caseService.restoreCase(c.id).subscribe({
      next: () => this.router.navigate(['/cases', c.id]),
      error: () => { /* surface via a toast later */ },
    });
  }

  /** Permanently purges a soft-deleted case after confirmation (Admin). */
  purgeCase(): void {
    const c = this.case();
    if (!c || this.isPurged()) return;
    const ref = this.dialog.open<ConfirmDialogComponent, ConfirmDialogData, boolean>(
      ConfirmDialogComponent,
      {
        data: {
          title: 'Permanently erase case',
          message: `Erase "${c.subject}"? This scrubs all case content and cannot be undone.`,
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
        this.caseService.purgeCase(c.id).subscribe({
          next: () => this.router.navigate(['/cases']),
          error: () => { /* surface via a toast later */ },
        });
      }
    });
  }

  /** True while the close animation is playing (keeps the element mounted). */
  readonly closing = signal(false);

  /**
   * Closes the panel with a slide-out animation that reverses `panel-slide-in`.
   * We can't rely on the open `@if` alone because it destroys the node instantly
   * on close — so we set `closing`, let the reverse animation play, then unmount.
   */
  closePanel(): void {
    if (!this.panelOpen() || this.closing()) return;
    this.closing.set(true);
  }

  /**
   * Fires on every CSS animation end inside the panel. Only the slide-out
   * animation should unmount the node; the deep-link `act-pulse` also emits
   * `animationend` and must be ignored here.
   */
  onPanelAnimationEnd(event?: AnimationEvent): void {
    if (event && event.animationName !== 'panel-slide-out') return;
    if (!this.closing()) return;
    this.closing.set(false);
    this.panelOpen.set(false);
  }

  /** Toggles the right-side Emails/Activity panel. */
  togglePanel(): void {
    if (this.panelOpen()) {
      this.closePanel();
    } else {
      this.closing.set(false);
      this.panelOpen.set(true);
    }
  }

  /**
   * Closes the panel when a click lands outside it AND outside the header
   * toggle button (so re-clicking the toggle still just toggles). Mode/tool
   * buttons live inside the panel, so clicks there don't dismiss it. Material
   * overlays (mat-select dropdowns) render in a body-level cdk container
   * outside the panel, so exclude those too or selecting a date would close it.
   */
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

  /** Closes the panel on Escape for keyboard users. */
  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.panelOpen()) this.closePanel();
  }

  /** Switches the panel to show emails or activity (never both at once). */
  setPanelMode(mode: 'email' | 'activity'): void {
    this.panelMode.set(mode);
  }

  /** Reveals/hides the search input (deferred until the search icon is clicked). */
  toggleSearch(): void {
    this.searchVisible.update((v) => !v);
  }

  /** Reveals/hides the date filter (deferred until the date icon is clicked). */
  toggleDate(): void {
    this.dateVisible.update((v) => !v);
  }

  /**
   * Clears every panel filter value and hides both filter UIs. Called by the
   * reset 'x' button and automatically when leaving the case. Filters persist
   * across mode switches and panel open/close — only this resets them.
   */
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

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    const scrollToCommentId = this.route.snapshot.queryParamMap.get('scrollToComment');
    const fromTab = this.route.snapshot.queryParamMap.get('from');
    // Recycle-bin entry sets ?deleted=1 -> read-only deleted-mode view.
    this.deleted.set(this.route.snapshot.queryParamMap.get('deleted') === '1');
    // Deep link from the Customers page: ?activity=1 scrolls to + pulses the
    // case Activity card (so the customer's latest activity is in view).
    const focusActivity = this.route.snapshot.queryParamMap.get('activity') === '1';

    this.caseService.get(id).subscribe({
      next: (c) => {
        this.case.set(c);
        this.loading.set(false);
        // Mark conversation as read for both Agent and Admin users.
        const role = this.auth.getRole();
        if (role === 'Agent' || role === 'Admin') {
          this.caseService.markConversationRead(id).subscribe({
            next: () => this.navBadgeService.refresh(),
            error: () => { /* badge will correct on next poll */ },
          });
          // Record this open as a "viewed" activity row. Fire-and-forget:
          // server-side cooldown coalesces repeats, so refreshing the page
          // within 10 min won't add a second row.
          this.caseService.recordView(id).subscribe({
            next: () => this.loadCaseViews(id),
            error: () => { /* audit is best-effort; never block the page */ },
          });
        }
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set('You do not have permission to view this case.');
      },
    });
    this.callLogService.listByCase(id).subscribe({
      next: (logs) => this.logs.set(logs),
      // Agent on an unassigned/other-agent case gets a 403 from the server-side
      // log-scope guard (CallLogService) — expected, since the UI shows the
      // read-only banner. Swallow it instead of throwing an unhandled error.
      error: () => {},
    });
    this.caseService.agents().subscribe((list) => this.agents.set(list));
    // Full email log powers the Emails card (filtered client-side by CaseId).
    this.emailLogService.getAll().subscribe((list) => this.emails.set(list));
  // Load the comment thread.
  this.caseService.getComments(id).subscribe((list) => {
    this.comments.set(list);
    if (fromTab) {
      // --- From Conversations/Messages tab: scroll the inner chat container
      //     to show the target comment, then pulse it. ---
      const pulseComment = () => {
        let el: Element | null = null;
        if (scrollToCommentId) {
          el = document.querySelector(`[data-comment-id="${scrollToCommentId}"]`);
        }
        if (!el) {
          const all = document.querySelectorAll<HTMLElement>('.comment-item');
          el = all.length > 0 ? all[all.length - 1] : null;
        }
        if (!el) return;
        el.classList.add('comment-pulse');
        el.addEventListener('animationend', () => {
          el.classList.remove('comment-pulse');
        }, { once: true });
      };

      const doScroll = (retries = 15) => {
        const inner = this.chatScroll?.nativeElement;
        if (!inner) {
          if (retries > 0) { setTimeout(() => doScroll(retries - 1), 200); }
          return;
        }
        // The page body has `overflow: hidden` — the actual scrollable
        // container is the `.content` div inside the layout shell.
        const scrollContainer = document.querySelector('.content');
        const cardEl = document.getElementById('conversation-card');
        if (scrollContainer && cardEl) {
          const cardRect = cardEl.getBoundingClientRect();
          const containerRect = scrollContainer.getBoundingClientRect();
          const cardTopInContainer = cardRect.top - containerRect.top + scrollContainer.scrollTop;
          scrollContainer.scrollTo({ top: Math.max(0, cardTopInContainer - 16), behavior: 'smooth' });
        }
        // Small delay so the container smooth-scroll starts before the inner scroll.
        setTimeout(() => {
          if (scrollToCommentId) {
            const el = document.querySelector(`[data-comment-id="${scrollToCommentId}"]`);
            if (el) {
              // Scroll the chat container so the target comment is visible.
              el.scrollIntoView({ behavior: 'smooth', block: 'center' });
              setTimeout(pulseComment, 800);
              return;
            }
          }
          // Fall back: scroll the inner chat to the bottom.
          inner.scrollTo({ top: inner.scrollHeight, behavior: 'smooth' });
          setTimeout(pulseComment, 800);
        }, 100);
      };
      // Wait for Angular to render the DOM, disable entrance animations,
      // then start the scroll + pulse sequence.
      setTimeout(() => {
        const cardEl = document.getElementById('conversation-card');
        if (cardEl) cardEl.classList.add('is-visible');
        const listEl = document.querySelector('.comment-list');
        if (listEl) listEl.classList.remove('stagger');
        document.querySelectorAll<HTMLElement>('.comment-item').forEach((el) => {
          el.style.opacity = '1';
          el.style.transform = 'none';
        });
        doScroll();
      }, 500);
    } else {
      // --- Normal navigation: when the card enters view, smoothly scroll
      //     to the latest message so the user sees it happen. ---
      const observer = new IntersectionObserver(
        (entries) => {
          for (const entry of entries) {
            if (entry.isIntersecting) {
              // Small delay so the user perceives the smooth scroll.
              setTimeout(() => this.scrollToBottom(), 300);
              observer.disconnect();
              break;
            }
          }
        },
        { threshold: 0.85 },
      );
      // Check periodically until the card exists.
      const waitForCard = setInterval(() => {
        const card = document.getElementById('conversation-card');
        if (card) {
          // If already in view, fire after a brief pause so the user sees it.
          const rect = card.getBoundingClientRect();
          const visible = rect.top < window.innerHeight && rect.bottom > 0;
          if (visible) {
            setTimeout(() => this.scrollToBottom(), 600);
          }
          observer.observe(card);
          clearInterval(waitForCard);
        }
      }, 200);
      // Safety: clean up after 10 s.
      setTimeout(() => {
        clearInterval(waitForCard);
        observer.disconnect();
      }, 10000);
      // Also clean up on destroy so we never leak.
      this.destroyRef.onDestroy(() => {
        clearInterval(waitForCard);
        observer.disconnect();
      });
    }
  });

    // Poll for new comments every 5 seconds so messages appear in real-time.
    this.startCommentsPolling(id);

    // Deep link from the Customers page: open the history panel in Activity
    // mode and pulse it (so the customer's latest activity is in view).
    if (focusActivity) {
      const pulsePanel = (attempts = 20) => {
        const panel = document.getElementById('history-panel');
        if (!panel) {
          if (attempts > 0) setTimeout(() => pulsePanel(attempts - 1), 100);
          return;
        }
        panel.classList.add('act-pulse');
        panel.addEventListener('animationend', () => panel.classList.remove('act-pulse'), { once: true });
      };
      // Open the panel first; the element only exists once panelOpen is true.
      this.panelOpen.set(true);
      this.panelMode.set('activity');
      setTimeout(pulsePanel, 350);
    }

    // Reset the chat-scroll position when navigating away, so the next visit
    // starts from the top (instead of being stuck at the bottom).
    this.router.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((e) => {
      if (e instanceof NavigationStart) {
        const el = document.querySelector<HTMLElement>('.chat-scroll');
        if (el) el.scrollTop = 0;
        // Leaving the case clears the panel's filter state.
        this.resetFilters();
      }
    });
  }

  /** Polls for new comments every 5 s and appends any that are new. */
  private startCommentsPolling(caseId: number): void {
    this.commentsPolling?.unsubscribe();
    this.commentsPolling = interval(5000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.caseService.getComments(caseId).subscribe({
          next: (fresh) => {
            this.comments.update((existing) => {
              if (existing.length === 0) return fresh;
              const maxId = Math.max(...existing.map((c) => c.id));
              const newer = fresh.filter((c) => c.id > maxId);
              if (newer.length > 0) {
                setTimeout(() => this.scrollToBottom());
                this.navBadgeService.refresh();
                return [...existing, ...newer];
              }
              return existing;
            });
          },
          // Transient backend errors (restart, network blip, scope 403) must not
          // kill the 5s poll. With no handler, RxJS re-throws and tears down the
          // outer interval — one failed tick would stop all future real-time
          // comment refresh. Swallow and let the next tick retry.
          error: () => {},
        });
      });
  }

  /** Scrolls the chat container to the bottom so the latest message is visible. */
  private scrollToBottom(): void {
    const el = this.chatScroll?.nativeElement;
    if (el) {
      requestAnimationFrame(() => { el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' }); });
    }
  }

  /** Scrolls to a specific comment by id (for auto-scroll from Messages tab). */
  private scrollToComment(commentId: number): void {
    requestAnimationFrame(() => {
      setTimeout(() => {
        const el = this.chatScroll?.nativeElement;
        if (!el) return;
        const items = el.querySelectorAll<HTMLElement>('.comment-item');
        if (items.length === 0) return;
        // The comments array index matches DOM index.
        const idx = this.comments().findIndex((c) => c.id === commentId);
        if (idx >= 0 && items[idx]) {
          items[idx].scrollIntoView({ behavior: 'smooth', block: 'center' });
        } else {
          // Fallback: scroll to top of chat area
          el.scrollTop = 0;
        }
      }, 100);
    });
  }

  /** Adds a call / follow-up log to the case. */
  addLog(): void {
    if (!this.canEdit()) return; // Agents may only log on cases assigned to them
    if (this.logForm.invalid) {
      this.logForm.markAllAsTouched();
      return;
    }
    const id = this.case()?.id;
    if (!id) return;
    this.savingLog.set(true);
    const v = this.logForm.getRawValue();
    this.callLogService
      .create({ caseId: id, direction: v.direction, notes: v.notes, durationSeconds: v.durationSeconds })
      .subscribe({
        next: (log) => {
          this.logs.update((l) => [...l, log]);
          this.logForm.reset({ direction: 'Outbound', notes: '', durationSeconds: 0 });
          this.savingLog.set(false);
        },
        error: () => this.savingLog.set(false),
      });
  }

  /** Gap 3: Posts a staff reply to the case's comment thread. */
  addComment(): void {
    if (this.commentForm.invalid) {
      this.commentForm.markAllAsTouched();
      return;
    }
    const id = this.case()?.id;
    if (!id) return;
    this.savingComment.set(true);
    const body = this.commentForm.getRawValue().body;
    this.caseService.postComment(id, body).subscribe({
      next: (comment) => {
        this.comments.update((list) => [...list, comment]);
        this.commentForm.reset({ body: '' });
        this.savingComment.set(false);
        this.scrollToBottom();
        this.navBadgeService.refresh();
      },
      error: () => this.savingComment.set(false),
    });
  }

  /** Updates the case status immediately from the side card. */
  updateStatus(status: Case['status']): void {
    const c = this.case();
    if (!c || c.status === status) return;
    this.caseService
      .update(c.id, {
        subject: c.subject,
        description: c.description,
        status,
        priority: c.priority,
        categoryId: c.categoryId,
        assignedToUserId: null,
      })
      .subscribe(() => {
        this.case.set({ ...c, status });
        this.saveFlash.show(`Status → ${status}`);
      });
  }

  /** Updates the case priority immediately from the side card. */
  updatePriority(priority: Case['priority']): void {
    const c = this.case();
    if (!c || c.priority === priority) return;
    this.caseService
      .update(c.id, {
        subject: c.subject,
        description: c.description,
        status: c.status,
        priority,
        categoryId: c.categoryId,
        assignedToUserId: null,
      })
      .subscribe(() => {
        this.case.set({ ...c, priority, priorityAutoSuggested: false });
        this.saveFlash.show(`Priority → ${priority}`);
      });
  }

  /**
   * Assigns (or reassigns) the case to the chosen agent via the existing
   * update path. Sends the selected agent id explicitly so the backend sets
   * the assignee; the null-preservation logic leaves every other field
   * untouched (re-verifies the earlier data-loss fix).
   *
   * NOTE: the backend's UpdateAsync treats a *null* AssignedToUserId as
   * "preserve the existing assignee" — only the UnassignSentinel
   * ("__unassign__") actually clears it. So selecting "Unassigned" must send
   * the sentinel, otherwise the request is a silent no-op and the old
   * assignee survives a reload. (The case-form dialog already does this.)
   */
  private static readonly UNASSIGN_SENTINEL = '__unassign__';

  assignTo(agentId: string | null): void {
    const c = this.case();
    if (!c) return;
    // No-op when the selection matches the current assignee.
    if ((agentId ?? null) === (c.assignedToUserId ?? null)) return;
    this.assigning.set(true);
    this.assignError.set(null);
    // Backend's UpdateAsync treats a null AssignedToUserId as "preserve the
    // existing assignee" — only the UnassignSentinel actually clears it. So
    // selecting "Unassigned" must send the sentinel, otherwise the request is
    // a silent no-op and the old assignee survives a reload.
    const payload = agentId ?? CaseDetailComponent.UNASSIGN_SENTINEL;
    this.caseService
      .update(c.id, {
        subject: c.subject,
        description: c.description,
        status: c.status,
        priority: c.priority,
        categoryId: c.categoryId,
        assignedToUserId: payload,
      })
      .subscribe({
        next: () => {
          // Re-fetch the case so the Assignee field, "Updated" timestamp, and
          // any server-derived values reflect the authoritative saved state.
          // This is the proven-instant path: the PUT lands, we GET the fresh
          // case and set it — the card flips within the round-trip (~100ms),
          // no optimistic pre-set that can race the realtime re-GET.
          this.caseService.get(c.id).subscribe({
            next: (fresh) => this.case.set(fresh),
            // If the refetch fails, keep the last known value.
            error: () => {},
          });
          this.assigning.set(false);
          const name = agentId ? (this.agents().find((a) => a.id === agentId)?.fullName ?? 'agent') : null;
          this.saveFlash.show(agentId ? `Assigned to ${name}` : 'Unassigned');
        },
        error: () => {
          this.assigning.set(false);
          this.assignError.set('Could not save the assignment. Please try again.');
        },
      });
  }

  /** Opens the edit-case modal directly; navigates to Cases List if deleted. */
  edit(): void {
    const id = this.case()?.id;
    if (!id) return;
    const data: CaseFormDialogData = { caseId: id };
    const ref = this.dialog.open(CaseFormComponent, {
      data,
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((result) => {
      if (result && (result as { deleted?: boolean }).deleted) {
        // The case no longer exists — go back to the list.
        this.router.navigateByUrl('/cases');
      }
    });
  }

  /** Status pill class. */
  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  /** Priority pill class. */
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }

  /** Build tooltip data for a priority pill. */
  priorityTooltip(c: Case): TooltipData {
    const items = [
      { label: 'Priority', value: c.priority },
      { label: 'Suggested', value: c.priorityAutoSuggested ? 'Yes (AI)' : 'Manual' },
      { label: 'Category', value: c.categoryName },
    ];
    if (c.priorityReason) items.push({ label: 'Reason', value: c.priorityReason });
    if (c.daysOverdue != null) items.push({ label: 'Overdue', value: `${c.daysOverdue} day${c.daysOverdue !== 1 ? 's' : ''}` });
    items.push({ label: 'Comments', value: String(c.commentCount ?? 0) });
    return { items };
  }

  /** Build tooltip data for a status pill. */
  statusTooltip(c: Case): TooltipData {
    const created = c.createdAtUtc ? new Date(c.createdAtUtc).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
    const updated = c.updatedAtUtc ? new Date(c.updatedAtUtc).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
    const items = [
      { label: 'Status', value: c.status },
      { label: 'Assigned', value: c.assignedToUserName ?? 'Unassigned' },
      { label: 'Created', value: created },
      { label: 'Updated', value: updated },
    ];
    return { items };
  }

  /** Formats a UTC date string for display. */
  formatDate(value?: string | null): string {
    if (!value) return '—';
    return new Date(value).toLocaleString();
  }

  /** Human-readable label for a notification/email type. */
  typeLabel(type: string): string {
    return EMAIL_TYPE_LABELS[type] ?? type;
  }
}
