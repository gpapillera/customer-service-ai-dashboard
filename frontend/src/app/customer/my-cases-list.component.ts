import { Component, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RevealDirective } from '../shared/reveal.directive';
import { KbdNavDirective } from '../shared/keyboard-nav.directive';
import { CustomerService } from './customer.service';
import { CustomerCaseSummary } from '../shared/models';

/**
 * Customer "My Cases" list. Shows only the calling customer's own cases and
 * links into the detail view. No priority / AI / agent data is rendered.
 */
@Component({
  selector: 'app-my-cases-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatIconModule,
    CsIconComponent,
    RevealDirective,
    KbdNavDirective,
  ],
  templateUrl: './my-cases-list.component.html',
  styleUrl: './my-cases-list.component.scss',
})
export class MyCasesListComponent implements OnInit, OnDestroy {
  private readonly customerService = inject(CustomerService);
  private readonly router = inject(Router);

  readonly cases = signal<CustomerCaseSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private readonly POLL_MS = 30_000;

  ngOnInit(): void {
    this.load();
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
    this.customerService.listCases().subscribe({
      next: (list) => this.cases.set(list),
      error: () => { /* ignore polling errors */ },
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.customerService.listCases().subscribe({
      next: (list) => {
        this.cases.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('We could not load your cases. Please try again.');
      },
    });
  }

  open(id: number): void {
    // Mark this case as read in localStorage before navigating.
    const c = this.cases().find((x) => x.id === id);
    if (c?.lastStaffCommentAtUtc) {
      try {
        localStorage.setItem(`cs_customer_read_${id}`, c.lastStaffCommentAtUtc);
      } catch { /* quota or SSR */ }
    } else {
      // No staff comments yet — mark as read using the case creation time.
      try {
        localStorage.setItem(`cs_customer_read_${id}`, c?.createdAtUtc || new Date().toISOString());
      } catch { /* quota or SSR */ }
    }
    this.router.navigateByUrl(`/customer/cases/${id}`);
  }

  newCase(): void {
    this.router.navigateByUrl('/customer/cases/new');
  }

  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  formatDate(value: string): string {
    return new Date(value).toLocaleString();
  }

  /** Checks if a case has unread staff messages (for the red dot indicator). */
  hasUnread(c: CustomerCaseSummary): boolean {
    if (!c.lastStaffCommentAtUtc) return false;
    try {
      const lastRead = localStorage.getItem(`cs_customer_read_${c.id}`);
      if (!lastRead) return true; // Never viewed — unread if there's a staff comment.
      return new Date(c.lastStaffCommentAtUtc).getTime() > new Date(lastRead).getTime();
    } catch {
      return false;
    }
  }
}
