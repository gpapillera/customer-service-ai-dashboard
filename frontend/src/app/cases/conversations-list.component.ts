import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CaseService } from './case.service';
import { Conversation } from '../shared/models';

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
    MatCardModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './conversations-list.component.html',
  styleUrl: './conversations-list.component.scss',
})
export class ConversationsListComponent implements OnInit {
  private readonly service = inject(CaseService);
  private readonly router = inject(Router);

  readonly conversations = signal<Conversation[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

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
  }

  /** Opens the case's existing Case Detail page (which shows the thread). */
  open(c: Conversation): void {
    this.router.navigateByUrl(`/cases/${c.caseId}`);
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
