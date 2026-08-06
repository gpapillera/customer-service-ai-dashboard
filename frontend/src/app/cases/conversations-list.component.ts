import { Component, computed, inject, OnInit, OnDestroy, signal, HostListener, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelect, MatSelectModule } from '@angular/material/select';
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
  private readonly elementRef = inject(ElementRef);

  /** Sidenav open state — brand logo hidden when open. */
  readonly sidenavOpen = inject(LayoutComponent).opened;
  /** True only during explicit sidenav toggle for brand logo animation. */
  readonly brandAnimate = inject(LayoutComponent).brandAnimate;

  readonly conversations = signal<Conversation[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly searchTerm = signal('');
  readonly dateFilterPreset = signal<'all' | 'today' | '7days' | '30days' | 'custom' | 'beforeCustomDate' | 'afterCustomDate' | 'onOrBeforeCustomDate' | 'onOrAfterCustomDate'>('all');
  /** Custom date range inputs — only used when preset is 'custom'. */
  readonly customDateFrom = signal('');
  readonly customDateTo = signal('');
  /** Single date input — only used when preset is 'beforeCustomDate', 'afterCustomDate', 'onOrBeforeCustomDate', or 'onOrAfterCustomDate'. */
  readonly customDateSingle = signal('');

  /** Reference to the date preset mat-select for programmatic close. */
  @ViewChild('dateSelectRef', { static: true }) dateSelectRef!: MatSelect;

  /** True when only unread conversations should be shown. */
  readonly unreadOnly = signal(false);

  /** True when any non-default filter is active. */
  readonly hasActiveFilter = computed(() => this.dateFilterPreset() !== 'all' || this.unreadOnly());

  /**
   * Whether the date input popup is visible.
   * Opens when a date-requiring preset is selected, closes on outside click.
   * Stays visible even after dates are filled so users can modify them.
   */
  readonly showDatePopup = signal(false);

  /** Pixel position for the fixed-position date popup (computed from filter-wrapper's bounding rect). */
  readonly datePopupTop = signal('0px');
  readonly datePopupLeft = signal('0px');

  /**
   * Shows the date popup and computes its pixel position relative to the
   * viewport (using getBoundingClientRect). Uses position:fixed so it
   * stacks above the CDK overlay backdrop.
   */
  private showDatePopupWithPosition(): void {
    const wrapper = this.elementRef.nativeElement.querySelector('.filter-wrapper') as HTMLElement | null;
    if (wrapper) {
      const rect = wrapper.getBoundingClientRect();
      this.datePopupTop.set(`${rect.bottom + 10}px`);
      this.datePopupLeft.set(`${rect.left}px`);
    }
    this.showDatePopup.set(true);
  }

  /**
   * Flag set by onDatePresetChange before openedChange(false) fires.
   * When true, the popup stays open after the mat-select panel closes
   * (because the user selected a date preset). When false, the panel
   * closed from an outside click, so the popup closes too — one click
   * hides both the dropdown panel and the date popup.
   */
  private popupKeepOnPanelClose = false;

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
      } else if (preset === 'onOrBeforeCustomDate') {
        const single = this.customDateSingle();
        if (single) {
          const singleMs = new Date(single).getTime();
          if (!isNaN(singleMs)) {
            list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() <= singleMs + 86_400_000);
          }
        }
      } else if (preset === 'onOrAfterCustomDate') {
        const single = this.customDateSingle();
        if (single) {
          const singleMs = new Date(single).getTime();
          if (!isNaN(singleMs)) {
            list = list.filter((c) => new Date(c.lastCommentAtUtc).getTime() >= singleMs);
          }
        }
      }
    }
    if (this.unreadOnly()) {
      list = list.filter((c) => c.unread);
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
      onOrBeforeCustomDate: 'On or before…',
      onOrAfterCustomDate: 'On or after…',
    };
    return labels[preset] ?? preset;
  }

  /** Handles date preset selection — shows popup for date-requiring presets. */
  onDatePresetChange(preset: string): void {
    this.dateFilterPreset.set(preset as any);
    if (preset === 'custom' || preset === 'beforeCustomDate' || preset === 'afterCustomDate' || preset === 'onOrBeforeCustomDate' || preset === 'onOrAfterCustomDate') {
      this.showDatePopupWithPosition();
      // ngModelChange fires before openedChange(false), so this flag
      // tells openedChange to keep the popup open after the panel closes.
      this.popupKeepOnPanelClose = true;
    } else {
      this.showDatePopup.set(false);
    }
  }

  /**
   * Handles mat-select panel close. Closes the popup together with the
   * dropdown, so a single outside click hides both. The flag is set by
   * onDatePresetChange when a preset is selected — in that case the popup
   * stays open (or was already closed if the preset doesn't need dates).
   */
  onDateSelectOpenedChange(opened: boolean): void {
    if (!opened && !this.popupKeepOnPanelClose) {
      // If a date-requiring preset is active and the mat-select closes
      // (e.g. because the user clicked the date popup), keep the popup
      // visible so the user can edit dates. The onDocumentClick handler
      // still closes the popup when clicking truly outside.
      const preset = this.dateFilterPreset();
      const needsDate = preset === 'custom' || preset === 'beforeCustomDate' || preset === 'afterCustomDate' || preset === 'onOrBeforeCustomDate' || preset === 'onOrAfterCustomDate';
      if (!needsDate) {
        this.showDatePopup.set(false);
      }
    }
    this.popupKeepOnPanelClose = false;
  }

  /** Called when clicking the filter area. Re-shows date popup if a
   *  date-requiring preset is already active, so user can modify dates
   *  without having to re-select the preset from the dropdown. */
  onFilterWrapperClick(): void {
    const preset = this.dateFilterPreset();
    if (preset === 'custom' || preset === 'beforeCustomDate' || preset === 'afterCustomDate' || preset === 'onOrBeforeCustomDate' || preset === 'onOrAfterCustomDate') {
      this.showDatePopupWithPosition();
    }
  }

  /**
   * Handles clicks inside the date popup. Stops propagation so the
   * document click handler doesn't close the popup, and programmatically
   * closes the mat-select panel so the CDK overlay doesn't block the
   * date input. This lets users edit dates when both dropdown and popup
   * are visible.
   */
  onDatePopupClick(event: MouseEvent): void {
    event.stopPropagation();
    if (this.dateSelectRef.panelOpen) {
      this.dateSelectRef.close();
    }
  }

  /** Resets date filter to defaults. */
  resetDateFilter(): void {
    this.dateFilterPreset.set('all');
    this.customDateFrom.set('');
    this.customDateTo.set('');
    this.customDateSingle.set('');
    this.showDatePopup.set(false);
  }

  /** Closes date popup when clicking outside it. */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.showDatePopup()) return;
    const target = event.target as HTMLElement;
    const popupEl = this.elementRef.nativeElement.querySelector('.date-popup') as HTMLElement;
    if (popupEl && popupEl.contains(target)) return;
    // Don't close for clicks inside the filter area or on CDK overlays
    if (target.closest('.filter-wrapper') || target.closest('.cdk-overlay-container')) return;
    this.showDatePopup.set(false);
  }

  /** Toggles the unread-only filter. */
  toggleUnreadFilter(): void {
    this.unreadOnly.update((v) => !v);
  }

  /** Clears the search input (filteredConversations recomputes automatically). */
  clearSearch(): void {
    this.searchTerm.set('');
  }

  /** Formats an ISO date string for display. */
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
