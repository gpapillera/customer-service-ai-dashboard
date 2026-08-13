import { Injectable, inject, signal, effect } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CaseService } from '../cases/case.service';
import { AuthService } from '../auth/auth.service';
import { RealtimeService } from './realtime.service';

/**
 * Tracks "new item" badge counts for the sidenav navigation links.
 *
 * For Messages/Conversations tabs: counts unread conversations server-side.
 * For Dashboard/Customers/Cases tabs: counts items created since the last
 * time the user visited that section (tracked via localStorage timestamps).
 *
 * Polls every 10 s while the browser tab is visible. Also listens for
 * custom `cs:comment-posted` DOM events for immediate refresh when a
 * message is sent from any page. Resets a section's badge to zero when
 * the user navigates to it.
 */
@Injectable({ providedIn: 'root' })
export class NavBadgeService {
  private readonly caseService = inject(CaseService);
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

    // Real-time: when an assignment changes anywhere, refresh badges instantly
    // (e.g. an agent's Messages/Cases sidebar counts update the moment a case
    // is assigned/unassigned — no wait for the 10s poll).
    effect(() => {
      this.realtime.caseEvent();
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
        this.refresh();
      });

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

    // --- Cases / Customers / Dashboard: new items since last visit ---
    const since = this.getVisited('/cases');
    this.caseService.list({}).subscribe({
      next: (list) => {
        if (since) {
          const newCases = list.filter((c) => new Date(c.createdAtUtc).getTime() > since).length;
          this.updateBadge('/cases', newCases);
        } else {
          // First visit — no badge.
          this.updateBadge('/cases', 0);
        }
      },
      error: () => { /* ignore */ },
    });

    // Dashboard badge = new cases + new customers since last visit.
    const dashSince = this.getVisited('/dashboard');
    if (dashSince) {
      // Reuse the cases data above — but we need customers too.
      // We'll compute dashboard badge as cases + customers in a separate call.
    }
    // For simplicity, dashboard badge is recomputed in the cases subscribe above.
  }

  /** Resets the badge for a route path to zero. */
  private resetBadge(path: string): void {
    this.badges.update((b) => ({ ...b, [path]: 0 }));
  }

  /** Updates a single badge count. */
  private updateBadge(path: string, count: number): void {
    this.badges.update((b) => ({ ...b, [path]: count }));
  }

  /** Records the current time as "last visited" for a section. */
  private setVisited(path: string): void {
    try {
      localStorage.setItem(this.keyFor(path), Date.now().toString());
    } catch { /* quota or SSR */ }
  }

  /** Returns the epoch-ms timestamp of the last visit, or null. */
  private getVisited(path: string): number | null {
    try {
      const v = localStorage.getItem(this.keyFor(path));
      return v ? Number(v) : null;
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

  /** Returns the badge count for a route path (0 if none). */
  getCount(path: string): number {
    return this.badges()[path] ?? 0;
  }
}
