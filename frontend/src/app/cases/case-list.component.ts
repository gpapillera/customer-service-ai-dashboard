import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RouteLoadingService } from '../shared/route-loading.service';
import { CaseService } from './case.service';
import { CaseFormComponent } from './case-form.component';
import { Case } from '../shared/models';
import { CATEGORIES } from '../shared/categories';

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
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './case-list.component.html',
  styleUrl: './case-list.component.scss',
})
export class CaseListComponent implements OnInit {
  private readonly service = inject(CaseService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly routeLoading = inject(RouteLoadingService);

  readonly cases = signal<Case[]>([]);
  /** Internal data-fetch state. */
  private readonly dataLoading = signal(true);
  /** True while the list is loading OR a route navigation is in progress. */
  readonly loading = computed(() => this.dataLoading() || this.routeLoading.loading());
  readonly categories = CATEGORIES;

  readonly filters = signal({
    status: '' as string,
    priority: '' as string,
    categoryId: null as number | null,
    search: '' as string,
    aiOnly: false,
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
    if (f.aiOnly) chips.push({ key: 'aiOnly', label: 'AI Predicted' });
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
    if (status) {
      // "Open" is a pseudo-status (everything except Closed) handled client-side.
      if (status === 'Open') {
        this.isOpenFilter.set(true);
      } else {
        this.filters.update((f) => ({ ...f, status }));
      }
    }
    if (priority) this.filters.update((f) => ({ ...f, priority }));
    if (categoryId) this.filters.update((f) => ({ ...f, categoryId: Number(categoryId) }));
    if (aiOnly) this.filters.update((f) => ({ ...f, aiOnly: true }));

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

  /** Clears a single active filter chip and reloads. */
  clearFilter(chip: { key: string; label: string }): void {
    if (chip.key === 'status') {
      this.isOpenFilter.set(false);
      this.filters.update((f) => ({ ...f, status: '' }));
    } else if (chip.key === 'aiOnly') {
      this.filters.update((f) => ({ ...f, aiOnly: false }));
    } else {
      this.filters.update((f) => ({ ...f, [chip.key]: chip.key === 'categoryId' ? null : '' }));
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
