import { Component, computed, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { KbdNavDirective } from '../shared/keyboard-nav.directive';
import { NavBadgeService } from '../shared/nav-badge.service';
import { CaseService } from './case.service';
import { Conversation } from '../shared/models';
import { LayoutComponent } from '../shared/layout/layout.component';

/**
 * Agent "Messages" tab (Phase 9). Lists the agent's cases that have a comment
 * thread, most-recent activity first, with unread ones visually distinguished.
 * Clicking an entry opens that case's existing Case Detail page (which now
 * renders the shared comment thread — the same UI the customer sees).
 *
 * Admin's equivalent global view is a later phase and is intentionally not
 * built here; the nav item is Agent-only.
 */
@Component({
  selector: 'app-conversations-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    RevealDirective,
    CsIconComponent,
    KbdNavDirective,
  ],
  templateUrl: './conversations-list.component.html',
  styleUrl: './conversations-list.component.scss',
})
export class ConversationsListComponent implements OnInit, OnDestroy {
  private readonly service = inject(CaseService);
  private readonly router = inject(Router);
  private readonly navBadgeService = inject(NavBadgeService);

  /** Sidenav open state — brand logo hidden when open. */
  readonly sidenavOpen = inject(LayoutComponent).opened;
  /** True only during explicit sidenav toggle for brand logo animation. */
  readonly brandAnimate = inject(LayoutComponent).brandAnimate;

  readonly conversations = signal<Conversation[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly searchTerm = signal('');
  readonly dateFilterPreset = signal<'all' | 'today' | '7days' | '30days' | 'custom' | 'beforeCustomDate' | 'afterCustomDate'>('all');
  /** Custom date range inputs — only used when preset is 'custom'. */
  readonly customDateFrom = signal('');
  readonly customDateTo = signal('');
  /** Single date input — only used when preset is 'beforeCustomDate' or 'afterCustomDate'. */
  readonly customDateSingle = signal('');

  /** True when any non-default filter is active. */
  readonly hasActiveFilter = computed(() => this.dateFilterPreset() !== 'all');

  /**
   * Whether the date popup should be visible.
   * Auto-hides once the required date inputs are filled.
   */
  readonly showDatePopup = computed(() => {
    const preset = this.dateFilterPreset();
    if (preset === 'custom') {
      return !this.customDateFrom() || !this.customDateTo();
    }
    if (preset === 'beforeCustomDate' || preset === 'afterCustomDate') {
      return !this.customDateSingle();
    }
    return false;
  });

  /** Conversations filtered by subject, customer name, and date preset. */
  readonly filteredConversations = computed(() => {
    let list = this.conversations();
    const term = this.searchTerm().toLowerCase().trim();
    if (term) {
      list = list.filter(
        (c) =>
          c.subject.toLowerCase().includes(term) ||
          c.customerName.toLowerCase().includes(term)
      );
    }
    const preset = this.dateFilterPreset();
    if (preset !== 'all') {
      const now = new Date();
      if (preset === 'today') {
        const todayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
        list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() >= todayStart);
      } else if (preset === '7days') {
        const cutoff = now.getTime() - 7 * 24 * 60 * 60 * 1000;
        list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() >= cutoff);
      } else if (preset === '30days') {
        const cutoff = now.getTime() - 30 * 24 * 60 * 60 * 1000;
        list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() >= cutoff);
      } else if (preset === 'custom') {
        const from = this.customDateFrom();
        if (from) {
          const fromMs = new Date(from).getTime();
          if (!isNaN(fromMs)) {
            list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() >= fromMs);
          }
        }
        const to = this.customDateTo();
        if (to) {
          const toMs = new Date(to).getTime();
          if (!isNaN(toMs)) {
            list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() <= toMs + 86_400_000);
          }
        }
      } else if (preset === 'beforeCustomDate') {
        const single = this.customDateSingle();
        if (single) {
          const singleMs = new Date(single).getTime();
          if (!isNaN(singleMs)) {
            list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() < singleMs);
          }
        }
      } else if (preset === 'afterCustomDate') {
        const single = this.customDateSingle();
        if (single) {
          const singleMs = new Date(single).getTime();
          if (!isNaN(singleMs)) {
            list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() >= singleMs);
          }
        }
      }
    }
    return list;
  });

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private readonly POLL_MS = 30_000;

  ngOnInit(): void {
    this.service.myConversations().subscribe({
      next: (list) => {
        this.conversations.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load your conversations.');
        this.loading.set(false);
      },
    });
    if (typeof window !== 'undefined') {
      this.pollTimer = window.setInterval(() => this.refresh(), this.POLL_MS);
    }
  }

  ngOnDestroy(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  /** Silent refresh — does not show loading spinner. */
  private refresh(): void {
    this.service.myConversations().subscribe({
      next: (list) => {
        this.conversations.set(list);
        this.navBadgeService.refresh();
      },
      error: () => { /* ignore polling errors */ },
    });
  }

  /** Opens the case's existing Case Detail page (which shows the thread). */
  open(c: Conversation): void {
    this.router.navigate(['/cases', c.caseId], {
      queryParams: { from: 'messages', scrollToComment: c.lastCommentId },
    });
  }

  /** Human-readable label for a date preset key. */
  formatDatePreset(preset: string): string {
    const labels: Record<string, string> = {
      all: 'All time',
      today: 'Today',
      '7days': 'Last 7 days',
      '30days': 'Last 30 days',
      custom: 'Custom range',
      beforeCustomDate: 'Before date…',
      afterCustomDate: 'After date…',
    };
    return labels[preset] ?? preset;
  }

  /** Resets date filter to defaults. */
  resetDateFilter(): void {
    this.dateFilterPreset.set('all');
    this.customDateFrom.set('');
    this.customDateTo.set('');
    this.customDateSingle.set('');
  }

  /** Formats an ISO timestamp for display. */
  formatDate(iso: string): string {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleString(undefined, {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}
