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
  effect,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { DragDropModule, CdkDragDrop, moveItemInArray, CdkDragMove } from '@angular/cdk/drag-drop';
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
import { RealtimeService } from '../shared/realtime.service';
import { DatePreset, DATE_PRESETS, formatDatePreset, filterByDatePreset, positionHeaderDropdown } from '../shared/date-filter';
import { SearchFilterToolbarComponent } from './search-filter-toolbar/search-filter-toolbar.component';
import { LayoutComponent } from '../shared/layout/layout.component';
import { CaseTableSettingsService, MIN_COL_WIDTH } from './case-table-settings.service';

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
    DragDropModule,
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
  /** Real-time assignment push (SSE). Drives instant refresh on assignment change. */
  private readonly realtime = inject(RealtimeService);
  readonly cases = signal<Case[]>([]);
  /** Internal data-fetch state. */
  private readonly dataLoading = signal(true);
  /** True while the list is loading OR a route navigation is in progress. */
  readonly loading = computed(() => this.dataLoading() || this.routeLoading.loading());
  readonly categories = CATEGORIES;
  /** Status / priority option lists for the table header filter dropdowns. */
  readonly statuses = ['New', 'InProgress', 'Escalated', 'Resolved', 'Closed'];
  readonly priorities = ['Low', 'Medium', 'High'];
  /** Silent auto-refresh so an assignment/reassignment shows up without a manual
      reload (an admin assigning a case to this agent is otherwise invisible until
      navigation). Mirrors the customer "My Cases" 30s poll. Only active on the
      normal filtered list path — the customer-detail deep-link branch filters by
      customerId and is left alone by the poll. */
  private readonly pollActive = signal(false);
  private readonly pollTimer = interval(10_000)
    .pipe(takeUntilDestroyed())
    .subscribe(() => { if (this.pollActive()) this.silentRefresh(); });
  /** Instant refresh: when the SSE push reports a case-assignment change, re-fetch
      the list immediately (≤1 network round-trip — no 30s wait). The 30s poll above
      remains as a fallback if the stream ever drops. */
  private readonly rtEffect = effect(() => {
    this.realtime.caseEvent(); // subscribe
    if (this.pollActive()) this.silentRefresh();
  });

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
  /** Date filter preset for the "Modified on" column — mirrors Created exactly. */
  readonly modDateFilterPreset = signal<DatePreset>('all');
  /** Custom range start (YYYY-MM-DD) — only used when preset is 'custom'. */
  readonly modCustomDateFrom = signal('');
  /** Custom range end (YYYY-MM-DD) — only used when preset is 'custom'. */
  readonly modCustomDateTo = signal('');
  /** Single date input (YYYY-MM-DD) — used by the before/after/on-or-before/on-or-after presets. */
  readonly modCustomDateSingle = signal('');
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
    if (this.modDateFilterPreset() !== 'all') {
      chips.push({ key: 'modDate', label: 'Modified: ' + formatDatePreset(this.modDateFilterPreset()) });
    }
    return chips;
  });

  /** Current sort state. */
  readonly sortColumn = signal<'subject' | 'customerName' | 'categoryName' | 'priority' | 'status' | 'createdAtUtc' | 'updatedAtUtc'>('createdAtUtc');
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
      } else if (col === 'createdAtUtc' || col === 'updatedAtUtc') {
        // Modified-on falls back to created when a case has never been edited.
        const aVal = col === 'createdAtUtc' ? a.createdAtUtc : (a.updatedAtUtc ?? a.createdAtUtc);
        const bVal = col === 'createdAtUtc' ? b.createdAtUtc : (b.updatedAtUtc ?? b.createdAtUtc);
        cmp = new Date(aVal).getTime() - new Date(bVal).getTime();
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
  toggleSort(column: 'subject' | 'customerName' | 'categoryName' | 'priority' | 'status' | 'createdAtUtc' | 'updatedAtUtc'): void {
    if (this.sortColumn() === column) {
      this.sortDesc.update((d) => !d);
    } else {
      this.sortColumn.set(column);
      this.sortDesc.set(true);
    }
  }

  // ── Per-user column order + width (Cases table customization) ──
  private readonly tableSettings = inject(CaseTableSettingsService);
  /** Column order (per-user) driving both <thead> and <tbody>. */
  readonly columnOrder = this.tableSettings.columnOrder;
  /** Column widths (per-user) in px; absent key = auto. */
  readonly columnWidths = this.tableSettings.columnWidths;

  /** Static metadata per column: label + which header filter (if any) it owns. */
  readonly COLDEFS: Record<string, { label: string; filter?: 'category' | 'priority' | 'status' | 'date' | 'modDate' }> = {
    subject:      { label: 'Case' },
    customerName: { label: 'Customer' },
    categoryName: { label: 'Category', filter: 'category' },
    priority:     { label: 'Priority', filter: 'priority' },
    status:       { label: 'Status', filter: 'status' },
    createdAtUtc: { label: 'Created', filter: 'date' },
    updatedAtUtc: { label: 'Modified on', filter: 'modDate' },
  };
  /** Columns in the current (per-user) display order, with metadata. */
  readonly orderedColumns = computed(() => this.columnOrder().map((k) => ({ key: k, ...this.COLDEFS[k] })));

  /** Landing index (among current columns) where the dragged column will drop.
      Drives the live drop-indicator bar. Computed from the pointer's X position
      vs each column's center — NOT CDK's placeholder index, which is unreliable
      on a <tr> drop-list + border-collapse (it kept reporting 0, so every drag
      landed in the first column). */
  readonly dragOverIndex = signal<number | null>(null);

  /** Key of the column currently being dragged (for source styling). */
  readonly draggingKey = signal<string | null>(null);

  /** Last pointer X (client coords) seen during an active drag. Captured in
      onDragMoved and read by dropColumn. Kept in a PLAIN field (not the
      dragOverIndex signal) because CDK fires (cdkDragEnded) BEFORE
      (cdkDropListDropped) — if we relied on the signal, onDragEnded would null
      it before dropColumn runs and we'd fall back to CDK's unreliable
      currentIndex (which is ~0 on a <tr> + border-collapse table, so every
      drag snapped to the first column). */
  private lastDragX: number | null = null;

  /** The <tr cdkDropList> — used to read live column geometry during a drag. */
  @ViewChild('dropList') private dropListRef?: ElementRef<HTMLTableRowElement>;

  /** Begin a header drag — remember which column is moving (for styling). */
  onDragStarted(key: string): void {
    this.draggingKey.set(key);
  }

  /** Reorder after drop. We IGNORE CDK's event.currentIndex (unreliable here)
      and use the pointer-derived landing index instead, so the column lands
      exactly where the live indicator showed. NOTE: cdkDragEnded fires BEFORE
      cdkDropListDropped, so the dragOverIndex signal may already be null by now
      — read lastDragX (a plain field) instead, which is not cleared early. */
  dropColumn(event: CdkDragDrop<string[]>): void {
    const desired = this.computeDropIndex(this.lastDragX) ?? event.currentIndex;
    const order = [...this.columnOrder()];
    if (desired >= 0 && desired < order.length && event.previousIndex !== desired) {
      moveItemInArray(order, event.previousIndex, desired);
      this.columnOrder.set(order);
      this.tableSettings.persist();
    }
    this.lastDragX = null;
    this.dragOverIndex.set(null);
    this.draggingKey.set(null);
  }

  /** Live: where will the dragged column land? Compute from the pointer's X
      position vs each <th>'s center — robust regardless of CDK's internal
      placeholder index (which misbehaved on a <tr> + border-collapse table).
      Drives the bright drop-indicator bar shown during the drag. Returns null
      if geometry is unavailable. */
  private computeDropIndex(pointerX: number | null): number | null {
    const list = this.dropListRef?.nativeElement;
    if (!list || pointerX == null) return null;
    const x = pointerX - window.scrollX;
    const ths = Array.from(list.querySelectorAll('th')) as HTMLElement[];
    let best = -1;
    let bestDist = Infinity;
    ths.forEach((th, idx) => {
      const r = th.getBoundingClientRect();
      const center = r.left + r.width / 2;
      const d = Math.abs(x - center);
      if (d < bestDist) { bestDist = d; best = idx; }
    });
    return best >= 0 ? best : null;
  }

  onDragMoved(e: CdkDragMove): void {
    this.lastDragX = e.pointerPosition.x;
    this.dragOverIndex.set(this.computeDropIndex(this.lastDragX));
  }

  /** Clear drag state when the drag ends (drop or cancel). */
  onDragEnded(): void {
    this.dragOverIndex.set(null);
    this.draggingKey.set(null);
  }

  /** Header click sorts (matches old behaviour) — only the grip initiates a drag. */
  /** Header click sorts (matches old behaviour). Clicks on the resize handle
      are stopped at the handle (see template) so double-click-to-auto-fit
      never triggers a sort. */
  onHeaderClick(key: string): void {
    this.toggleSort(key as any);
  }

  /** Reset columns AND widths to default for the current user. */
  resetColumns(): void {
    this.tableSettings.reset();
  }

  // ── Column resize (right-edge handle) ──
  private resizing: { key: string; startX: number; startW: number } | null = null;

  /** Begin dragging a column's right-edge resize handle. */
  startResize(event: MouseEvent, key: string): void {
    event.preventDefault();
    event.stopPropagation();
    const th = (event.currentTarget as HTMLElement).closest('th');
    const startW = this.columnWidths()[key]
      ?? th?.getBoundingClientRect().width
      ?? 120;
    this.resizing = { key, startX: event.clientX, startW };
    window.addEventListener('mousemove', this.onResizeMove);
    window.addEventListener('mouseup', this.onResizeEnd);
  }

  /** Live width update while dragging the resize handle. */
  onResizeMove = (e: MouseEvent): void => {
    if (!this.resizing) return;
    const next = Math.max(MIN_COL_WIDTH, Math.round(this.resizing.startW + (e.clientX - this.resizing.startX)));
    this.columnWidths.update((m) => ({ ...m, [this.resizing!.key]: next }));
  };

  /** End the resize drag and persist the final width. */
  onResizeEnd = (): void => {
    if (this.resizing) this.tableSettings.persist();
    this.resizing = null;
    window.removeEventListener('mousemove', this.onResizeMove);
    window.removeEventListener('mouseup', this.onResizeEnd);
  };

  /** Double-click a resize handle -> clear that column's custom width (auto). */
  clearColumnWidth(key: string, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.tableSettings.clearWidth(key);
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
    this.pollActive.set(true);
    this.realtime.start(); // open the SSE push (instant assignment reflection)
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
    this.fetchAndApply();
  }

  /**
   * Silent refresh used by the 30s poll — re-fetches with the current filter
   * state but does NOT toggle the loading spinner (so the table doesn't flash
   * every interval). Errors are swallowed so one failed tick doesn't break the
   * list or the ongoing poll.
   */
  private silentRefresh(): void {
    this.fetchAndApply(true);
  }

  /** Fetches cases using the current filter state and applies client-side
      filters (Open pseudo-status, AI-only, search, date preset). When
      `silent` is true, loading state is left untouched. */
  private fetchAndApply(silent = false): void {
    const f = this.filters();
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
          const modPreset = this.modDateFilterPreset();
          if (modPreset !== 'all') {
            // A case with no edits uses its created date as the modified date.
            filtered = filterByDatePreset(
              filtered,
              modPreset,
              (c) => c.updatedAtUtc ?? c.createdAtUtc,
              this.modCustomDateFrom(),
              this.modCustomDateTo(),
              this.modCustomDateSingle(),
            );
          }
          this.cases.set(filtered);
          if (!silent) {
            this.dataLoading.set(false);
            this.placeHeaderDropdownAfterLoad();
          }
        },
        error: () => {
          if (!silent) {
            this.dataLoading.set(false);
            this.placeHeaderDropdownAfterLoad();
          }
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

  /** Sets the date filter preset from the "Modified on" header dropdown. */
  setModDatePreset(preset: DatePreset): void {
    this.modDateFilterPreset.set(preset);
    // Close the dropdown for presets that don't need date inputs; keep it
    // open for date-requiring presets so the user can type dates inline.
    if (preset === 'all' || preset === 'today' || preset === '7days' || preset === '30days') {
      this.openHeaderFilter.set(null);
      this.detachDropdownScrollWatch();
    }
    this.load();
  }

  /** Updates a custom-date input (From/To/single) for the "Modified on" filter. */
  onModCustomDateChange(field: 'from' | 'to' | 'single', value: string): void {
    if (field === 'from') this.modCustomDateFrom.set(value);
    else if (field === 'to') this.modCustomDateTo.set(value);
    else this.modCustomDateSingle.set(value);
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
    } else if (chip.key === 'modDate') {
      this.modDateFilterPreset.set('all');
      this.modCustomDateFrom.set('');
      this.modCustomDateTo.set('');
      this.modCustomDateSingle.set('');
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
