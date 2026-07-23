import { Component, computed, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { KbdNavDirective } from '../shared/keyboard-nav.directive';
import { NavBadgeService } from '../shared/nav-badge.service';
import { CaseService } from './case.service';
import { Conversation } from '../shared/models';
import { LayoutComponent } from '../shared/layout/layout.component';

/**
 * Admin "Conversations" tab (Phase 12). Lists every case that has at least
 * one comment, regardless of assignment. Shows the assigned agent name
 * (or "Unassigned"). Clicking opens the same Case Detail comment-thread UI
 * the agents use — no duplication.
 */
@Component({
  selector: 'app-admin-conversations',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatInputModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
    KbdNavDirective,
  ],
  templateUrl: './admin-conversations.component.html',
  styleUrl: './admin-conversations.component.scss',
})
export class AdminConversationsComponent implements OnInit, OnDestroy {
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

  /** Conversations filtered by subject or customer name. */
  readonly filteredConversations = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    if (!term) return this.conversations();
    return this.conversations().filter(
      (c) =>
        c.subject.toLowerCase().includes(term) ||
        c.customerName.toLowerCase().includes(term)
    );
  });

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private readonly POLL_MS = 30_000;

  ngOnInit(): void {
    this.service.allConversations().subscribe({
      next: (list) => {
        this.conversations.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load conversations.');
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
    this.service.allConversations().subscribe({
      next: (list) => {
        this.conversations.set(list);
        this.navBadgeService.refresh();
      },
      error: () => { /* ignore polling errors */ },
    });
  }

  open(c: Conversation): void {
    this.router.navigate(['/cases', c.caseId], {
      queryParams: { from: 'conversations', scrollToComment: c.lastCommentId },
    });
  }

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
