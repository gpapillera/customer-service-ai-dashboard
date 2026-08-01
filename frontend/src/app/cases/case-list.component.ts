import {
  Component,
  afterNextRender,
  computed,
  ElementRef,
  EnvironmentInjector,
  HostListener,
  OnDestroy,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CsTooltipDirective } from '../shared/tooltip.directive';
import { TooltipData } from '../shared/tooltip-data';
import { RouteLoadingService } from '../shared/route-loading.service';
import { KbdNavDirective } from '../shared/keyboard-nav.directive';
import { CaseService } from './case.service';
import { CaseFormComponent } from './case-form.component';
import { Case } from '../shared/models';
import { CATEGORIES } from '../shared/categories';
import { DatePreset, DATE_PRESETS, formatDatePreset, filterByDatePreset, positionHeaderDropdown } from '../shared/date-filter';
import { SearchFilterToolbarComponent } from './search-filter-toolbar/search-filter-toolbar.component';
import { LayoutComponent } from '../shared/layout/layout.component';

/**
 * Case list with status / priority / category filters and a free-text search.
 * The new / edit case forms open as a modal dialog on top of this list.
 */
@Component({
  selector: 'app-case-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
    CsTooltipDirective,
    KbdNavDirective,
    SearchFilterToolbarComponent,
  ],
  templateUrl: './case-list.component.html',
  styleUrl: './case-list.component.scss',
})
export class CaseListComponent implements OnInit, OnDestroy {
  private readonly service = inject(CaseService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly elRef = inject(ElementRef);
  private readonly envInjector = inject(EnvironmentInjector);
  /** True while the scroll/resize watch for the date dropdown is attached. */
  private dropdownWatchAttached = false;
  /** Sidenav open state (from the app shell) — the page brand logo is shown
      only when the sidenav is collapsed. */
  readonly sidenavOpen = inject(LayoutComponent).opened;
  /** True only during an explicit sidenav toggle, so the logo animates then. */
  readonly brandAnimate = inject(LayoutComponent).brandAnimate;
  private readonly routeLoading = inject(RouteLoadingService);

  readonly cases = signal<Case[]>([]);
  /** Internal data-fetch state. */
  private readonly dataLoading = signal(true);
  /** True while the list is loading OR a route navigation is in progress. */
  readonly loading = computed(() => this.dataLoading() || this.routeLoading.loading());
  readonly categories = CATEGORIES;
  /** Status / priority option lists for the table header filter dropdowns. */
  readonly statuses = ['New', 'InProgress', 'Escalated', 'Resolved', 'Closed'];
  readonly priorities = ['Low', 'Medium', 'High'];

  /** Initial search value (for query-param pre-fill). */
  toolbarSearch = '';

  readonly filters = signal({
    status: '' as string,
    priority: '' as string,
    categoryId: null as number | null,
    search: '' as string,
    aiOnly: false,
    overdue: false,
    assignedToMe: false,
  });

  /** True when the "Open" pseudo-filter (only New / InProgress / Escalated) is active. */
  readonly isOpenFilter = signal(false);

  /** Track which table-header filter dropdown is open, or null. */
  readonly openHeaderFilter = signal<string | null>(null);

  /** Date filter preset for the "Created" column (same presets as Conversations). */
  readonly dateFilterPreset = signal<DatePreset>('all');
  /** Custom range start (YYYY-MM-DD) — only used when preset is 'custom'. */
  readonly customDateFrom = signal('');
  /** Custom range end (YYYY-MM-DD) — only used when preset is 'custom'. */
  readonly customDateTo = signal('');
  /** Single date input (YYYY-MM-DD) — used by the before/after/on-or-before/on-or-after presets. */
  readonly customDateSingle = signal('');
  /** Preset options for the Created header filter dropdown. */
  readonly datePresets = DATE_PRESETS;
  /** Labels a date preset key for display. */
  readonly datePresetLabel = formatDatePreset;

  /** Active filters rendered as removable chips in the filter row. */
  readonly activeChips = computed<{ key: string; label: string }[]>(() => {
    const f = this.filters();
    const chips: { key: string; label: string }[] = [];
    if (this.isOpenFilter()) chips.push({ key: 'status', label: 'Open' });
    else if (f.status) chips.push({ key: 'status', label: f.status });
    if (f.priority) chips.push({ key: 'priority', label: f.priority });
    if (f.categoryId != null) {
      const cat = this.categories.find((c) => c.id === f.categoryId);
      chips.push({ key: 'categoryId', label: cat?.name ?? 'Category' });
    }
    if (this.dateFilterPreset() !== 'all') {
      chips.push({ key: 'date', label: formatDatePreset(this.dateFilterPreset()) });
    }
    return chips;
  });

  /** Current sort state. */
  readonly sortColumn = signal<'subject' | 'customerName' | 'categoryName' | 'priority' | 'status' | 'createdAtUtc'>('createdAtUtc');
  readonly sortDesc = signal(true);

  /** Cases sorted according to the current sort column and direction. */
  readonly sortedCases = computed(() => {
    const list = this.cases();
    const col = this.sortColumn();
    const desc = this.sortDesc();
    const priorityWeight: Record<string, number> = { Low: 0, Medium: 1, High: 2 };
    const statusWeight: Record<string, number> = {
      New: 0, InProgress: 1, Escalated: 2, Resolved: 3, Closed: 4,
    };
    const sorted = [...list].sort((a, b) => {
      let cmp = 0;
      if (col === 'priority') {
        cmp = (priorityWeight[a.priority] ?? 0) - (priorityWeight[b.priority] ?? 0);
      } else if (col === 'status') {
        cmp = (statusWeight[a.status] ?? 0) - (statusWeight[b.status] ?? 0);
      } else {
        const aVal = a[col] ?? '';
        const bVal = b[col] ?? '';
        cmp = typeof aVal === 'string'
          ? aVal.localeCompare(String(bVal))
          : Number(aVal) - Number(bVal);
      }
      return desc ? -cmp : cmp;
    });
    return sorted;
  });

  /** Toggle sort column; reverse direction if already sorting by this column. */
  toggleSort(column: 'subject' | 'customerName' | 'categoryName' | 'priority' | 'status' | 'createdAtUtc'): void {
    if (this.sortColumn() === column) {
      this.sortDesc.update((d) => !d);
    } else {
      this.sortColumn.set(column);
      this.sortDesc.set(true);
    }
  }

  ngOnInit(): void {
    // Support deep-link from a customer detail ("View cases").
    const customerId = this.route.snapshot.queryParamMap.get('customerId');
    if (customerId) {
      this.service
        .list()
        .subscribe((all) =>
          this.cases.set(all.filter((c) => c.customerId === Number(customerId))),
        );
      this.dataLoading.set(false);
      return;
    }

    // Pre-apply filters coming from dashboard KPI / chart deep-links.
    const qp = this.route.snapshot.queryParamMap;
    const status = qp.get('status');
    const priority = qp.get('priority');
    const categoryId = qp.get('categoryId');
    const aiOnly = qp.get('aiOnly') === 'true';
    const overdue = qp.get('overdue') === 'true';
    const assignedToMe = qp.get('assignedToMe') === 'true';
    if (status) {
      // "Open" is a pseudo-status (only New / InProgress / Escalated) handled client-side.
      if (status === 'Open') {
        this.isOpenFilter.set(true);
      } else {
        this.filters.update((f) => ({ ...f, status }));
      }
    }
    if (priority) {
      this.filters.update((f) => ({ ...f, priority }));
    }
    if (categoryId) {
      this.filters.update((f) => ({ ...f, categoryId: Number(categoryId) }));
    }
    if (aiOnly) this.filters.update((f) => ({ ...f, aiOnly: true }));
    if (overdue) this.filters.update((f) => ({ ...f, overdue: true }));
    if (assignedToMe) this.filters.update((f) => ({ ...f, assignedToMe: true }));

    this.load();

    // Open the create/edit modal when reached via /cases/new or /cases/:id/edit.
    const id = this.route.snapshot.paramMap.get('id');
    if (this.route.snapshot.url.some((s) => s.path === 'new') || id) {
      this.openDialog(id ? Number(id) : undefined);
    }
  }

  /** Opens the create/edit case dialog. */
  openDialog(caseId?: number): void {
    const ref = this.dialog.open(CaseFormComponent, {
      data: { caseId },
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe(() => {
      // Return to the plain list URL and refresh.
      this.router.navigateByUrl('/cases', { replaceUrl: true });
      this.load();
    });
  }

  /** Reloads cases using the current filter state. */
  load(): void {
    this.dataLoading.set(true);
    const f = this.filters();
    // "Open" is a pseudo-status (only New / InProgress / Escalated) — fetch all and
    // filter client-side; aligns with the dashboard OpenCases definition.
    const serverStatus = this.isOpenFilter() ? undefined : f.status || undefined;
    this.service
      .list({
        status: serverStatus,
        priority: f.priority || undefined,
        categoryId: f.categoryId ?? undefined,
        overdue: f.overdue || undefined,
        assignedToMe: f.assignedToMe || undefined,
      })
      .subscribe({
        next: (list) => {
          let filtered = list;
          if (this.isOpenFilter()) {
            filtered = filtered.filter((c) => c.status !== 'Resolved' && c.status !== 'Closed');
          }
          if (f.aiOnly) {
            filtered = filtered.filter((c) => c.priorityAutoSuggested);
          }
          const term = f.search.trim().toLowerCase();
          if (term) {
            filtered = filtered.filter(
              (c) =>
                c.subject.toLowerCase().includes(term) ||
                c.description.toLowerCase().includes(term) ||
                c.customerName.toLowerCase().includes(term),
            );
          }
          const preset = this.dateFilterPreset();
          if (preset !== 'all') {
            filtered = filterByDatePreset(
              filtered,
              preset,
              (c) => c.createdAtUtc,
              this.customDateFrom(),
              this.customDateTo(),
              this.customDateSingle(),
            );
          }
          this.cases.set(filtered);
          this.dataLoading.set(false);
          this.placeHeaderDropdownAfterLoad();
        },
        error: () => {
          this.dataLoading.set(false);
          this.placeHeaderDropdownAfterLoad();
        },
      });
  }

  /**
   * If a header-filter dropdown is open, re-place it after the next render.
   * load() flips the loading state, which swaps the table (and the open
   * dropdown inside it) for a spinner and back — the recreated dropdown has
   * no inline placement, so it must be re-placed once the table is back.
   */
  private placeHeaderDropdownAfterLoad(): void {
    if (this.openHeaderFilter() !== null) {
      afterNextRender(() => this.applyHeaderDropdownPlacement(), {
        injector: this.envInjector,
      });
    }
  }

  /** Updates a single filter field and reloads. */
  updateFilter(key: keyof ReturnType<typeof this.filters>, value: string | number | null): void {
    if (key === 'status') {
      // "Open" is a pseudo-status (only New/InProgress/Escalated) handled client-side.
      this.isOpenFilter.set(value === 'Open');
      if (value !== 'Open') {
        this.filters.update((f) => ({ ...f, status: value as string }));
      }
    } else {
      this.filters.update((f) => ({ ...f, [key]: value }));
    }
    this.load();
  }

  /** Toggles the AI-only filter (cases where the AI suggested the priority). */
  toggleAiOnly(): void {
    this.filters.update((f) => ({ ...f, aiOnly: !f.aiOnly }));
    this.load();
  }

  /** Toggles the overdue-follow-ups filter (open + past deadline + no follow-up since). */
  toggleOverdue(): void {
    this.filters.update((f) => ({ ...f, overdue: !f.overdue }));
    this.load();
  }

  /** Toolbar (Row A) handler — feeds search value into filter state. */
  onSearchChanged(value: string): void {
    this.toolbarSearch = value;
    this.filters.update((f) => ({ ...f, search: value }));
    this.load();
  }

  /** Sets the date filter preset from the Created header dropdown. */
  setDatePreset(preset: DatePreset): void {
    this.dateFilterPreset.set(preset);
    // Close the dropdown for presets that don't need date inputs; keep it
    // open for date-requiring presets so the user can type dates inline.
    if (preset === 'all' || preset === 'today' || preset === '7days' || preset === '30days') {
      this.openHeaderFilter.set(null);
      this.detachDropdownScrollWatch();
    }
    // load() re-renders the table (spinner swap), which recreates the open
    // dropdown — placeDateDropdownAfterLoad re-places it once the table is back.
    this.load();
  }

  /** Updates a custom-date input (From/To/single) and re-applies the filter. */
  onCustomDateChange(field: 'from' | 'to' | 'single', value: string): void {
    if (field === 'from') this.customDateFrom.set(value);
    else if (field === 'to') this.customDateTo.set(value);
    else this.customDateSingle.set(value);
    this.load();
  }

  /** Toggle a header filter dropdown open/closed. */
  toggleHeaderFilter(col: string): void {
    const next = this.openHeaderFilter() === col ? null : col;
    this.openHeaderFilter.set(next);
    if (next) {
      this.attachDropdownScrollWatch();
      // The dropdown is inside an @if, so it only exists after Angular
      // renders. Schedule the placement for after that render.
      afterNextRender(() => this.applyHeaderDropdownPlacement(), {
        injector: this.envInjector,
      });
    } else {
      this.detachDropdownScrollWatch();
    }
  }

  /** Set a filter value from a header dropdown and close it. */
  setHeaderFilter(col: string, value: string | number | null): void {
    this.openHeaderFilter.set(null);
    this.detachDropdownScrollWatch();
    if (col === 'status') {
      this.isOpenFilter.set(value === 'Open');
      if (value !== 'Open') {
        this.filters.update((f) => ({ ...f, status: (value as string) ?? '' }));
      } else {
        this.filters.update((f) => ({ ...f, status: '' }));
      }
    } else if (col === 'priority') {
      this.filters.update((f) => ({ ...f, priority: (value as string) ?? '' }));
    } else if (col === 'category') {
      this.filters.update((f) => ({ ...f, categoryId: value as number | null }));
    }
    this.load();
  }

  /** Close the header filter dropdown when clicking outside. */
  @HostListener('document:click')
  closeHeaderFilter(): void {
    this.detachDropdownScrollWatch();
    this.openHeaderFilter.set(null);
  }

  /**
   * Places the open header-filter dropdown with `position: fixed`, clamped to
   * the visible area so it is never clipped by the (now short) table wrapper.
   */
  private applyHeaderDropdownPlacement(): void {
    const dd = this.elRef.nativeElement.querySelector(
      '.header-filter-dropdown',
    ) as HTMLElement | null;
    if (!dd) return;
    const scrollRoot = (document.querySelector('.content') as HTMLElement | null) ?? document.body;
    positionHeaderDropdown(dd, scrollRoot);
  }

  /** Re-place on scroll/resize so the fixed popup stays glued to the funnel. */
  private readonly onDropdownViewportChange = (): void => {
    if (this.openHeaderFilter() !== null) this.applyHeaderDropdownPlacement();
  };

  private attachDropdownScrollWatch(): void {
    if (this.dropdownWatchAttached) return;
    this.dropdownWatchAttached = true;
    const root = (document.querySelector('.content') as HTMLElement | null) ?? window;
    root.addEventListener('scroll', this.onDropdownViewportChange, { passive: true });
    window.addEventListener('resize', this.onDropdownViewportChange);
  }

  private detachDropdownScrollWatch(): void {
    if (!this.dropdownWatchAttached) return;
    this.dropdownWatchAttached = false;
    const root = (document.querySelector('.content') as HTMLElement | null) ?? window;
    root.removeEventListener('scroll', this.onDropdownViewportChange);
    window.removeEventListener('resize', this.onDropdownViewportChange);
  }

  ngOnDestroy(): void {
    this.detachDropdownScrollWatch();
  }

  /** Clears a single active filter chip and reloads. */
  clearFilter(chip: { key: string; label: string }): void {
    if (chip.key === 'status') {
      this.isOpenFilter.set(false);
      this.filters.update((f) => ({ ...f, status: '' }));
    } else if (chip.key === 'aiOnly') {
      this.filters.update((f) => ({ ...f, aiOnly: false }));
    } else if (chip.key === 'priority') {
      this.filters.update((f) => ({ ...f, priority: '' }));
    } else if (chip.key === 'categoryId') {
      this.filters.update((f) => ({ ...f, categoryId: null }));
    } else if (chip.key === 'date') {
      this.dateFilterPreset.set('all');
      this.customDateFrom.set('');
      this.customDateTo.set('');
      this.customDateSingle.set('');
    }
    this.load();
  }

  /** Navigates to a case detail. */
  open(id: number): void {
    this.router.navigateByUrl(`/cases/${id}`);
  }

  /** Opens the new-case modal. */
  openNew(): void {
    this.openDialog();
  }

  /** Status pill class for the template. */
  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  /** Priority pill class for the template. */
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }

  /** Build tooltip data for a priority pill. */
  priorityTooltip(c: Case): TooltipData {
    const items = [
      { label: 'Priority', value: c.priority },
      { label: 'Suggested', value: c.priorityAutoSuggested ? 'Yes (AI)' : 'Manual' },
      { label: 'Category', value: c.categoryName },
    ];
    if (c.priorityReason) items.push({ label: 'Reason', value: c.priorityReason });
    if (c.daysOverdue != null) items.push({ label: 'Overdue', value: `${c.daysOverdue} day${c.daysOverdue !== 1 ? 's' : ''}` });
    items.push({ label: 'Comments', value: String(c.commentCount ?? 0) });
    return { items };
  }

  /** Build tooltip data for a status pill. */
  statusTooltip(c: Case): TooltipData {
    const created = c.createdAtUtc ? new Date(c.createdAtUtc).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
    const updated = c.updatedAtUtc ? new Date(c.updatedAtUtc).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
    const items = [
      { label: 'Status', value: c.status },
      { label: 'Assigned', value: c.assignedToUserName ?? 'Unassigned' },
      { label: 'Created', value: created },
      { label: 'Updated', value: updated },
    ];
    return { items };
  }
}
