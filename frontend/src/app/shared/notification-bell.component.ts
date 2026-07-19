import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { CsIconComponent } from './cs-icon.component';
import { NotificationService } from './notification.service';
import { Notification } from './models';

/**
 * In-app notification bell: shows an unread badge, opens a dropdown of recent
 * notifications, and lets the user mark them read (single or all). Clicking a
 * notification deep-links to the related case.
 */
@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [CommonModule, CsIconComponent],
  templateUrl: './notification-bell.component.html',
  styleUrl: './notification-bell.component.scss',
})
export class NotificationBellComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);

  /** Exposed for the template (badge + dropdown). */
  protected readonly notifications = inject(NotificationService);

  /** Whether the dropdown panel is open. */
  readonly open = signal(false);
  /** Recent notifications shown in the dropdown. */
  readonly items = signal<Notification[]>([]);

  private sub?: Subscription;

  ngOnInit(): void {
    this.refresh();
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  /** Toggles the dropdown and refreshes the list when opening. */
  toggle(): void {
    this.open.update((v) => !v);
    if (this.open()) {
      this.refresh();
    }
  }

  /** Closes the dropdown (used by the backdrop). */
  close(): void {
    this.open.set(false);
  }

  /** Marks a single notification read and navigates to its link. */
  openItem(item: Notification): void {
    if (item.status === 'Unread') {
      this.notifications.markRead(item.id).subscribe();
    }
    this.close();
    if (item.link) {
      this.router.navigateByUrl(item.link);
    }
  }

  /** Marks every notification read. */
  markAll(): void {
    this.notifications.markAllRead().subscribe(() => this.refresh());
  }

  private refresh(): void {
    this.sub?.unsubscribe();
    this.sub = this.notifications.loadSummary().subscribe((s) => {
      this.items.set(s.recent);
    });
  }
}
