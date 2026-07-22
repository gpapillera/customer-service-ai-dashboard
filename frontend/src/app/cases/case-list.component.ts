import { Component, computed, inject, OnInit, signal } from '@angular/core';
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
import { RouteLoadingService } from '../shared/route-loading.service';
import { KbdNavDirective } from '../shared/keyboard-nav.directive';
import { CaseService } from './case.service';
import { CaseFormComponent } from './case-form.component';
import { Case } from '../shared/models';
import { CATEGORIES } from '../shared/categories';
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
    KbdNavDirective,
    SearchFilterToolbarComponent,
  ],
  templateUrl: './case-list.component.html',
  styleUrl: './case-list.component.scss',
})
export class CaseListComponent implements OnInit {
  private readonly service = inject(CaseService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
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
  /** Status / priority option lists for the search-filter toolbar. */
  readonly statuses = ['Open', 'New', 'InProgress', 'Escalated', 'Resolved', 'Closed'];
  readonly priorities = ['Low', 'Medium', 'High'];
  /** Category names passed to the toolbar (kept in sync with CATEGORIES). */
  readonly categoryNames = CATEGORIES.map((c) => c.name);

  /** Initial toolbar values (for query-param pre-fill). Empty = "All …". */
  toolbarSearch = '';
  toolbarStatus = '';
  toolbarPriority = '';
  toolbarCategory = '';

  readonly filters = signal({
    status: '' as string,
    priority: '' as string,
    categoryId: null as number | null,
    search: '' as string,
    aiOnly: false,
    overdue: false,
    assignedToMe: false,
  });

  /** True when the "Open" pseudo-filter (everything except Closed) is active. */
  readonly isOpenFilter = signal(false);

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
    return chips;
  });

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
      // "Open" is a pseudo-status (everything except Closed) handled client-side.
      if (status === 'Open') {
        this.isOpenFilter.set(true);
      } else {
        this.filters.update((f) => ({ ...f, status }));
      }
      this.toolbarStatus = status;
    }
    if (priority) {
      this.filters.update((f) => ({ ...f, priority }));
      this.toolbarPriority = priority;
    }
    if (categoryId) {
      this.filters.update((f) => ({ ...f, categoryId: Number(categoryId) }));
      const cat = this.categories.find((c) => c.id === Number(categoryId));
      this.toolbarCategory = cat?.name ?? '';
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
    // "Open" is a pseudo-status (everything except Closed) — fetch all and
    // filter client-side so the count matches the dashboard KPI exactly.
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
            filtered = filtered.filter((c) => c.status !== 'Closed');
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
          this.cases.set(filtered);
          this.dataLoading.set(false);
        },
        error: () => this.dataLoading.set(false),
      });
  }

  /** Updates a single filter field and reloads. */
  updateFilter(key: keyof ReturnType<typeof this.filters>, value: string | number | null): void {
    if (key === 'status') {
      // "Open" is a pseudo-status handled client-side.
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

  /** Toolbar (Row A) handlers — feed values into the existing filter state. */
  onSearchChanged(value: string): void {
    this.toolbarSearch = value;
    this.filters.update((f) => ({ ...f, search: value }));
    this.load();
  }
  onStatusChanged(value: string): void {
    // "Open" is a pseudo-status handled client-side.
    this.isOpenFilter.set(value === 'Open');
    this.toolbarStatus = value;
    if (value !== 'Open') {
      this.filters.update((f) => ({ ...f, status: value }));
    }
    this.load();
  }
  onPriorityChanged(value: string): void {
    this.toolbarPriority = value;
    this.filters.update((f) => ({ ...f, priority: value }));
    this.load();
  }
  onCategoryChanged(value: string): void {
    this.toolbarCategory = value;
    const cat = this.categories.find((c) => c.name === value);
    this.filters.update((f) => ({ ...f, categoryId: cat ? cat.id : null }));
    this.load();
  }

  /** Clears a single active filter chip and reloads. */
  clearFilter(chip: { key: string; label: string }): void {
    if (chip.key === 'status') {
      this.isOpenFilter.set(false);
      this.filters.update((f) => ({ ...f, status: '' }));
      this.toolbarStatus = '';
    } else if (chip.key === 'aiOnly') {
      this.filters.update((f) => ({ ...f, aiOnly: false }));
    } else if (chip.key === 'priority') {
      this.filters.update((f) => ({ ...f, priority: '' }));
      this.toolbarPriority = '';
    } else if (chip.key === 'categoryId') {
      this.filters.update((f) => ({ ...f, categoryId: null }));
      this.toolbarCategory = '';
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
}
