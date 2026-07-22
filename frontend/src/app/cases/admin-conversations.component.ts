import { Component, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CaseService } from './case.service';
import { Conversation } from '../shared/models';

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
    MatCardModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './admin-conversations.component.html',
  styleUrl: './admin-conversations.component.scss',
})
export class AdminConversationsComponent implements OnInit, OnDestroy {
  private readonly service = inject(CaseService);
  private readonly router = inject(Router);

  readonly conversations = signal<Conversation[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

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
      next: (list) => this.conversations.set(list),
      error: () => { /* ignore polling errors */ },
    });
  }

  open(c: Conversation): void {
    if (c.unread) {
      this.router.navigate(['/cases', c.caseId], {
        queryParams: { from: 'conversations' },
      });
    } else {
      this.router.navigate(['/cases', c.caseId]);
    }
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
