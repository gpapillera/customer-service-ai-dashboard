import { Injectable, inject, signal, effect } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { forkJoin } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CaseService } from '../cases/case.service';
import { CustomerService } from '../customers/customer.service';
import { AuthService } from '../auth/auth.service';
import { RealtimeService } from './realtime.service';

/**
 * Tracks "new item" badge counts for the sidenav navigation links.
 *
 * For Messages/Conversations tabs: counts unread conversations server-side.
 * For Dashboard/Customers/Cases tabs: counts items created since the last
 * time the user visited that section, PLUS (for Cases) cases assigned to the
 * current user since their last visit — tracked via localStorage timestamps
 * and the backend `AssignedAtUtc` field.
 *
 * Polls every 10 s while the browser tab is visible. Also listens for
 * custom `cs:comment-posted` DOM events for immediate refresh when a
 * message is sent from any page. Resets a section's badge to zero when
 * the user navigates to it. On a live SSE `case-assignment` event targeting
 * the current user, the Cases/Dashboard badges bump instantly (before the poll).
 */
@Injectable({ providedIn: 'root' })
export class NavBadgeService {
  private readonly caseService = inject(CaseService);
  private readonly customerService = inject(CustomerService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  /** Real-time assignment push (SSE) — refreshes badges the instant an assignment changes. */
  private readonly realtime = inject(RealtimeService);

  /** Badge count keyed by route path (e.g. '/dashboard', '/messages'). */
  readonly badges = signal<Record<string, number>>({});

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private readonly POLL_MS = 10_000;
  // Per-section "last visited" timestamps are scoped by user id so each account
  // tracks its own "new since I last looked" state. Without this, all accounts
  // on one browser share a single visited timestamp and clicking Cases as one
  // user dismisses the red dot for every other account.
  private readonly LS_PREFIX = 'cs_nav_badge_';

  // Floor for "last visited" timestamps. Any stored baseline older than this is
  // treated as absent on first load, so a stale localStorage value (e.g. from a
  // previous session days ago) can't backfill old customers/cases as "new".
  // Set once at service construction = the moment the app opened.
  private readonly appLoadFloor = Date.now();

  constructor() {
    // Reset the badge for the section the user navigates to.
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .pipe(takeUntilDestroyed())
      .subscribe((e) => {
        const url = (e as NavigationEnd).urlAfterRedirects || (e as NavigationEnd).url;
        const path = '/' + (url.split('?')[0].split('/')[1] || 'dashboard');
        // Don't reset the badge for conversations/messages on tab navigation.
        // Those badges should only clear when individual conversations are
        // actually opened and marked read server-side.
        const conversationPaths = ['/conversations', '/messages'];
        if (!conversationPaths.includes(path)) {
          this.resetBadge(path);
        }
        // Store visited timestamp for "new items" tracking.
        this.setVisited(path);
      });

    // Listen for comment-posted events from any page.
    if (typeof window !== 'undefined' && typeof window.addEventListener !== 'undefined') {
      window.addEventListener('cs:comment-posted', () => this.refresh());
    }

    // Real-time: when a mutation changes, refresh badges instantly (the
    // 10s poll is just the fallback). Customer edits/deletes/restores bump the
    // Customers dot the moment the SSE frame lands — no wait for the poll.
    // Case assignments/updates bump Cases/Dashboard. All of these only
    // *initiate a subscribe* (refresh/refresh/refresh) — no synchronous signal
    // write — so this is safe inside an effect.
    effect(() => {
      const evt = this.realtime.liveUpdate();
      if (!evt) return; // connect/reconnect no-op
      if (evt.kind === 'customer-update' || evt.kind === 'customer-deleted' || evt.kind === 'customer-restored') {
        this.bumpBadge('/customers', 1);
      } else if (evt.kind === 'case-assignment' || evt.kind === 'case-update') {
        this.bumpBadge('/cases', 1);
        this.bumpBadge('/dashboard', 1);
        // Case-assignment targeting the current user gets an immediate,
        // deduplicated bump (the poll would also catch it).
        const me = this.auth.currentUser()?.id ?? null;
        if (evt.kind === 'case-assignment' && evt.assignedToUserId && evt.assignedToUserId === me) {
          this.bumpBadge('/cases', 1);
        }
      }
      this.refresh();
    });

    // The per-section "last visited" timestamps are scoped by user id (see
    // keyFor), so each account tracks its own "new since I last looked" state.
    // When the signed-in user changes (login / logout / switch account), the
    // previous user's badge counts are stale for the new user — clear them and
    // recompute. This is what fixes the cross-account red-dot bleed: clicking
    // Cases as one user must not dismiss the dot for every other account.
    // Guarded on the user id so a no-op profile update (same id) doesn't
    // trigger a needless badge flicker + extra API calls.
    let prevUserId: string | null = this.auth.currentUser()?.id ?? null;
    this.auth.currentUser$
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        const userId = this.auth.currentUser()?.id ?? null;
        if (userId === prevUserId) return;
        prevUserId = userId;
        this.badges.set({});
        // Anchor this account's baselines to the app-load floor so it never
        // inherits a baseline older than this session (which would backfill
        // stale customers/cases as "new").
        this.anchorBaselines();
        this.refresh();
      });

    // On first load (and on every user switch), anchor each section's baseline
    // to the app-load floor so a freshly-appeared account never inherits a
    // baseline older than this session — which would otherwise backfill stale
    // customers/cases as "new". Clicking a section later writes a real baseline.
    this.anchorBaselines();

    // Initial fetch + periodic polling. The immediate refresh ensures badges
    // populate on first load (e.g. a restored session where currentUser$ already
    // matches the guard's initial value and is therefore skipped).
    this.refresh();
    if (typeof window !== 'undefined' && typeof window.setInterval !== 'undefined') {
      this.pollTimer = window.setInterval(() => this.refresh(), this.POLL_MS);
    }
  }

  /** Fetches unread counts and updates badge signals. */
  refresh(): void {
    const role = this.auth.getRole();
    const userId = this.auth.currentUser()?.id ?? null;

    // --- Messages / Conversations: unread count from conversation API ---
    if (role === 'Agent') {
      this.caseService.myConversations().subscribe({
        next: (list) => {
          const unreadCount = list.reduce(
            (sum, c) => sum + (c.unreadCount ?? (c.unread ? 1 : 0)), 0);
          this.updateBadge('/messages', unreadCount);
        },
        error: () => { /* ignore polling errors */ },
      });
    } else if (role === 'Admin') {
      this.caseService.allConversations().subscribe({
        next: (list) => {
          const unreadCount = list.reduce(
            (sum, c) => sum + (c.unreadCount ?? (c.unread ? 1 : 0)), 0);
          this.updateBadge('/conversations', unreadCount);
        },
        error: () => { /* ignore polling errors */ },
      });
    }

    // --- Cases / Customers: new items since last visit ---
    const casesSince = this.getVisited('/cases');
    const custSince = this.getVisited('/customers');

    // A case counts as "new for me" if it was created since my last visit OR
    // assigned to me since my last visit (deduped by id via the single filter).
    const newCasesSince = (list: { createdAtUtc: string; assignedToUserId: string | null; assignedAtUtc?: string | null }[], since: number | null): number => {
      if (!since) return 0;
      return list.filter((c) => {
        const created = new Date(c.createdAtUtc).getTime();
        const assigned = (c.assignedToUserId === userId && c.assignedAtUtc)
          ? new Date(c.assignedAtUtc).getTime()
          : -1;
        return created > since || assigned > since;
      }).length;
    };
    const newCustomersSince = (list: { createdAtUtc?: string | null; updatedAtUtc?: string | null }[], since: number | null): number => {
      if (!since) return 0;
      return list.filter((cu) => {
        const created = cu.createdAtUtc ? new Date(cu.createdAtUtc).getTime() : -1;
        const updated = cu.updatedAtUtc ? new Date(cu.updatedAtUtc).getTime() : -1;
        // A customer counts as "new for me" if it was created OR had its
        // account-level profile edited since my last visit. Deduped by id via
        // the single filter (an edit after creation only ever counts once).
        return created > since || updated > since;
      }).length;
    };

    forkJoin({
      cases: this.caseService.list({}),
      customers: this.customerService.list(),
    }).subscribe({
      next: ({ cases, customers }) => {
        this.updateBadge('/cases', newCasesSince(cases, casesSince));
        this.updateBadge('/customers', newCustomersSince(customers, custSince));
      },
      error: () => { /* ignore polling errors */ },
    });
  }

  /** Resets the badge for a route path to zero. */
  private resetBadge(path: string): void {
    this.badges.update((b) => ({ ...b, [path]: 0 }));
  }

  /** Updates a single badge count. */
  private updateBadge(path: string, count: number): void {
    this.badges.update((b) => ({ ...b, [path]: count }));
  }

  /** Increments a badge by `n` (clamped at 99 to keep the pill small).
   *  Used for the live SSE assignment bump. */
  private bumpBadge(path: string, n: number): void {
    const next = Math.min((this.badges()[path] ?? 0) + n, 99);
    this.updateBadge(path, next);
  }

  /** Records the current time as "last visited" for a section. */
  private setVisited(path: string): void {
    try {
      localStorage.setItem(this.keyFor(path), Date.now().toString());
    } catch { /* quota or SSR */ }
  }

  /** Returns the epoch-ms timestamp of the last visit, or null.
   *  Clamped to appLoadFloor so a stored baseline from before this app
   *  session opened (stale localStorage / shared key) is treated as "no
   *  baseline yet" → that section's badge starts empty instead of backfilling
   *  old items as "new since I last looked". */
  private getVisited(path: string): number | null {
    try {
      const v = localStorage.getItem(this.keyFor(path));
      if (!v) return null;
      const ts = Number(v);
      return ts >= this.appLoadFloor ? ts : null;
    } catch {
      return null;
    }
  }

  /**
   * Builds the localStorage key for a section's "last visited" timestamp,
   * scoped by the signed-in user so each account tracks its own state. Falls
   * back to a shared key when there is no current user (pre-login), which a
   * freshly logged-in user supersedes via the currentUser$ reset below.
   */
  private keyFor(path: string): string {
    const userId = this.auth.currentUser()?.id ?? '';
    return `${this.LS_PREFIX}${userId}:${path}`;
  }

  /**
   * Writes a baseline "last visited" timestamp for every sidenav section if
   * one doesn't already exist at-or-after the app-load floor. Run on first
   * load and on every user switch: it guarantees a section that was never
   * actually opened this session starts with an empty badge (no stale
   * backfill), while a section the user genuinely visited this session keeps
   * its real baseline. Sections with their own poll/reset logic
   * (/conversations, /messages) are skipped — the server owns those counts.
   */
  private anchorBaselines(): void {
    const paths = ['/dashboard', '/customers', '/cases'];
    for (const path of paths) {
      const existing = this.getVisited(path);
      if (existing == null) {
        try {
          localStorage.setItem(this.keyFor(path), this.appLoadFloor.toString());
        } catch { /* quota or SSR */ }
      }
    }
  }

  /** Returns the badge count for a route path (0 if none). */
  getCount(path: string): number {
    return this.badges()[path] ?? 0;
  }
}
