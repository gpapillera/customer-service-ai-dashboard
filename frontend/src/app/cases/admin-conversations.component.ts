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
export class AdminConversationsComponent implements OnInit {
  private readonly service = inject(CaseService);
  private readonly router = inject(Router);

  readonly conversations = signal<Conversation[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

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
  }

  open(c: Conversation): void {
    this.router.navigateByUrl(`/cases/${c.caseId}`);
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
