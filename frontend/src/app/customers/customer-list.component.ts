import { Component, computed, effect, inject, OnInit, signal, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RouteLoadingService } from '../shared/route-loading.service';
import { CustomerService } from './customer.service';
import { CustomerFormComponent } from './customer-form.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../shared/confirm-dialog.component';
import { Customer } from '../shared/models';
import { AuthService } from '../auth/auth.service';
import { withAuthRetry } from '../shared/auth-retry';
import { LayoutComponent } from '../shared/layout/layout.component';
import { DeletedDrawerComponent, RecycleItem } from '../shared/deleted-drawer.component';
import { Router } from '@angular/router';
import { SaveFlashService } from '../shared/save-flash.service';
import { RealtimeService } from '../shared/realtime.service';

/**
 * Customer list with debounced search and quick actions (view / edit / delete).
 * The new-customer form opens as a modal dialog on top of this list.
 */
@Component({
  selector: 'app-customer-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatMenuModule,
    MatTooltipModule,
    RevealDirective,
    CsIconComponent,
    DeletedDrawerComponent,
  ],
  templateUrl: './customer-list.component.html',
  styleUrl: './customer-list.component.scss',
})
export class CustomerListComponent implements OnInit {
  private readonly service = inject(CustomerService);
  private readonly dialog = inject(MatDialog);
  private readonly routeLoading = inject(RouteLoadingService);
  private readonly router = inject(Router);
  private readonly saveFlash = inject(SaveFlashService);
  readonly auth = inject(AuthService);
  /** Live customer-profile-edit push (SSE) — refreshes the grid the instant
   *  another staff member edits a customer, so the card footer's "recent
   *  activity" reflects the change without a manual page refresh. */
  private readonly realtime = inject(RealtimeService);

  readonly customers = signal<Customer[]>([]);
  /** Sidenav open state (from the app shell) — the page brand logo is shown
      only when the sidenav is collapsed. */
  readonly sidenavOpen = inject(LayoutComponent).opened;
  /** True only during an explicit sidenav toggle, so the logo animates then. */
  readonly brandAnimate = inject(LayoutComponent).brandAnimate;
  /** Internal data-fetch state. */
  private readonly dataLoading = signal(true);
  /** True while the list is loading OR a route navigation is in progress. */
  readonly loading = computed(() => this.dataLoading() || this.routeLoading.loading());
  readonly searchTerm = signal('');
  // Phase 24f — filter/sort state
  readonly hasAccountFilter = signal<string | null>(null); // null=all, "yes", "no"
  readonly sortBy = signal<string>('activity'); // "name" | "activity"
  readonly sortDirection = signal<string>('desc'); // "asc" | "desc"

  constructor() {
    // Live push: when ANY mutation lands that could change a customer card
    // (admin or cx self-service profile edit, delete/restore, or a case change
    // that surfaces on the card footer), re-fetch the grid so it reflects the
    // change instantly — no manual refresh. The current search/filter/sort
    // state is preserved by load(), which re-reads the signals. A null
    // (connect/reconnect) event does nothing. Created in the constructor =
    // injection context (an effect() in ngOnInit would throw NG0203 at runtime).
    // IMPORTANT: the effect only *initiates* the subscribe; all signal writes
    // happen in the async next() callback (writing a signal synchronously here
    // is NG0600 and silently kills the auto-refresh).
    effect(() => {
      const evt = this.realtime.liveUpdate();
      if (!evt) return; // connect/reconnect no-op
      this.load();
    });
  }

  ngOnInit(): void {
    this.load();
  }

  /** Loads all customers (or searches when a term is present). */
  load(): void {
    const term = this.searchTerm().trim();
    const hasAccount = this.hasAccountFilter();
    const sortBy = this.sortBy();
    const sortDir = this.sortDirection();

    // Convert hasAccountFilter to boolean|null
    let accountFilter: boolean | null = null;
    if (hasAccount === 'yes') accountFilter = true;
    else if (hasAccount === 'no') accountFilter = false;

    // NOTE: do NOT set dataLoading synchronously here — doing so inside an
    // effect() that called load() triggers NG0600 (signal write in a read
    // context). The grid keeps its current rows until the new list lands, which
    // is the correct, flicker-free behavior for a live refresh.
    const req = term
      ? this.service.search(term, accountFilter, sortBy, sortDir)
      : this.service.list(accountFilter, sortBy, sortDir);
    req.subscribe({
      next: (list) => {
        this.customers.set(list);
        this.dataLoading.set(false);
      },
      error: () => this.dataLoading.set(false),
    });
  }

  /** Sets account filter and reloads. */
  setAccountFilter(value: string | null): void {
    this.hasAccountFilter.set(value);
    this.load();
  }

  /** Sets sort field and reloads. */
  setSortBy(field: string): void {
    this.sortBy.set(field);
    this.load();
  }

  /** Toggles sort direction and reloads. */
  toggleSortDirection(): void {
    this.sortDirection.update(d => (d === 'asc' ? 'desc' : 'asc'));
    this.load();
  }

  /** Debounced search trigger from the input. */
  onSearch(value: string): void {
    this.searchTerm.set(value);
    this.load();
  }

  /** Clears the search input and reloads the unfiltered list. */
  clearSearch(): void {
    this.searchTerm.set('');
    this.load();
  }

  /** Opens the new-customer modal dialog. */
  openNew(): void {
    const ref = this.dialog.open(CustomerFormComponent, {
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((savedId) => {
      if (savedId) this.load();
    });
  }

  /** Deletes a customer after a confirmation dialog. */
  remove(id: number, name: string, caseCount: number): void {
    const ref = this.dialog.open<
      ConfirmDialogComponent,
      ConfirmDialogData,
      boolean
    >(ConfirmDialogComponent, {
      data: {
        title: 'Delete customer',
        message: `Delete customer '${name}'${caseCount > 0 ? ` (${caseCount} case${caseCount !== 1 ? 's' : ''})` : ''}? This moves them to the recycle bin, where they can be restored.`,
        confirmText: 'Delete',
        cancelText: 'Cancel',
        icon: 'delete',
      },
      width: '400px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.service.delete(id).subscribe(() => {
          this.saveFlash.show(`Customer '${name}' deleted`);
          this.load();
        });
      }
    });
  }

  /** Deterministic avatar color from the customer id. */
  avatarColor(id: number): string {
    const palette = [
      '#4f46e5', '#0ea5e9', '#10b981', '#f59e0b',
      '#ef4444', '#8b5cf6', '#ec4899', '#14b8a6',
    ];
    return palette[id % palette.length];
  }

  /** Recycle-bin rows (mapped from the deleted-customer DTOs). */
  readonly recycleItems = signal<RecycleItem[]>([]);

  /** The recycle-bin drawer (bound from the template #recycleDrawer ref). */
  @ViewChild('recycleDrawer') private recycleDrawerRef?: DeletedDrawerComponent;

  /** Opens the recycle-bin drawer (Admin only). */
  openRecycleBin(): void {
    this.service.recycleBin().pipe(
      withAuthRetry(this.auth),
    ).subscribe({
      next: (list) => {
        this.recycleItems.set(
          list.map((c) => ({
            id: c.id,
            title: c.name,
            subtitle: c.customerDisplayId ?? undefined,
            deletedAtUtc: c.deletedAtUtc ?? null,
          })),
        );
        this.recycleDrawerRef?.show();
      },
    });
  }

  /** Navigate to a deleted customer's read-only detail view. */
  onRecycleItemClick(item: RecycleItem): void {
    this.router.navigate(['/customers', item.id], { queryParams: { deleted: 1 } });
  }

  /** Returns the account icon name for a customer. */
  accountIcon(c: Customer): string {
    if (!c.hasAccount) return 'person_off';
    return c.accountActive ? 'check_circle' : 'pending';
  }

  /** Returns the account status label for a customer. */
  accountLabel(c: Customer): string {
    if (!c.hasAccount) return 'No account';
    return c.accountActive ? 'Active' : 'Invited';
  }

  /** Formats a UTC date string for display (date only). */
  formatDate(value?: string): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString();
  }

  /** Formats a UTC date string as "MMM DD, HH:MM AM/PM". */
  formatDateTime(value?: string): string {
    if (!value) return '—';
    const d = new Date(value);
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) +
      ', ' + d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
  }

  /** Formats the hover tooltip for active cases on a customer card. */
  activeCasesTooltip(c: Customer): string {
    return c.activeCases.map(ac => `• ${ac.subject} (${ac.status})`).join('\n');
  }
}
