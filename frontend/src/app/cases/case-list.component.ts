import { Component, inject, OnInit, signal } from '@angular/core';
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

  readonly cases = signal<Case[]>([]);
  readonly loading = signal(true);
  readonly categories = CATEGORIES;

  readonly filters = signal({
    status: '' as string,
    priority: '' as string,
    categoryId: null as number | null,
    search: '' as string,
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
      this.loading.set(false);
      return;
    }
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
    this.loading.set(true);
    const f = this.filters();
    this.service
      .list({
        status: f.status || undefined,
        priority: f.priority || undefined,
        categoryId: f.categoryId ?? undefined,
      })
      .subscribe({
        next: (list) => {
          const term = f.search.trim().toLowerCase();
          const filtered = term
            ? list.filter(
                (c) =>
                  c.subject.toLowerCase().includes(term) ||
                  c.description.toLowerCase().includes(term) ||
                  c.customerName.toLowerCase().includes(term),
              )
            : list;
          this.cases.set(filtered);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  /** Updates a single filter field and reloads. */
  updateFilter(key: keyof ReturnType<typeof this.filters>, value: string | number | null): void {
    this.filters.update((f) => ({ ...f, [key]: value }));
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
