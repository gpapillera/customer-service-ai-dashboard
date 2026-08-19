import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { CsIconComponent } from './cs-icon.component';

/**
 * A single row in a recycle bin. The drawer is deliberately dumb: the
 * consuming page maps its domain DTO (Customer / Case) into this shape so the
 * drawer stays reusable and never imports backend models.
 */
export interface RecycleItem {
  id: number;
  /** Primary line — customer name or case subject (or "Deleted User"). */
  title: string;
  /** Secondary line — display id, owning customer, etc. */
  subtitle?: string;
  /** UTC timestamp the item was soft-deleted (for the "deleted" footer). */
  deletedAtUtc?: string | null;
  /** Optional warning line — e.g. "Restore the customer account first". */
  warning?: string;
}

/**
 * Reusable right-side drawer that lists soft-deleted (recycle-bin) items for
 * the Customers and Cases pages. Opened via the `open()` method, closed via
 * the X / backdrop / Esc. Clicking a row emits `itemClick` so the page can
 * navigate to that item's deleted-mode detail view.
 *
 * Kept simple on purpose: one surface, one list, one action (open detail).
 */
@Component({
  selector: 'app-deleted-drawer',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatSidenavModule, CsIconComponent],
  templateUrl: './deleted-drawer.component.html',
  styleUrl: './deleted-drawer.component.scss',
})
export class DeletedDrawerComponent {
  /** Drawer heading, e.g. "Deleted customers". */
  @Input() title = 'Recycle bin';
  /** Rows to show. */
  @Input() set items(value: RecycleItem[]) {
    this._items.set(value ?? []);
  }
  get items(): RecycleItem[] {
    return this._items();
  }

  /** Fired when a row is clicked (navigate to deleted-mode detail). */
  @Output() itemClick = new EventEmitter<RecycleItem>();

  private readonly _items = signal<RecycleItem[]>([]);
  /** Drawer open state. */
  readonly open = signal(false);

  /** Fired by Material when the drawer open/close transition settles. We use
   *  it to sync our `open` signal when the user closes via backdrop or Esc. */
  onOpenedChange(isOpen: boolean): void {
    this.open.set(isOpen);
  }

  /** Opens the drawer. */
  show(): void {
    this.open.set(true);
  }

  /** Closes the drawer. */
  close(): void {
    this.open.set(false);
  }

  onRowClick(item: RecycleItem): void {
    this.itemClick.emit(item);
  }

  /** Formats a UTC date string as "MMM DD, HH:MM AM/PM". */
  formatDateTime(value?: string | null): string {
    if (!value) return '—';
    const d = new Date(value);
    return (
      d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) +
      ', ' +
      d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })
    );
  }
}
