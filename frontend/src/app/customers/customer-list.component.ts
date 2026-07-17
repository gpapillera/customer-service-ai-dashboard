import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RouteLoadingService } from '../shared/route-loading.service';
import { CustomerService } from './customer.service';
import { CustomerFormComponent } from './customer-form.component';
import { Customer } from '../shared/models';

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
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './customer-list.component.html',
  styleUrl: './customer-list.component.scss',
})
export class CustomerListComponent implements OnInit {
  private readonly service = inject(CustomerService);
  private readonly dialog = inject(MatDialog);
  private readonly routeLoading = inject(RouteLoadingService);

  readonly customers = signal<Customer[]>([]);
  /** Internal data-fetch state. */
  private readonly dataLoading = signal(true);
  /** True while the list is loading OR a route navigation is in progress. */
  readonly loading = computed(() => this.dataLoading() || this.routeLoading.loading());
  readonly searchTerm = signal('');

  ngOnInit(): void {
    this.load();
  }

  /** Loads all customers (or searches when a term is present). */
  load(): void {
    this.dataLoading.set(true);
    const term = this.searchTerm().trim();
    const req = term
      ? this.service.search(term)
      : this.service.list();
    req.subscribe({
      next: (list) => {
        this.customers.set(list);
        this.dataLoading.set(false);
      },
      error: () => this.dataLoading.set(false),
    });
  }

  /** Debounced search trigger from the input. */
  onSearch(value: string): void {
    this.searchTerm.set(value);
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

  /** Deletes a customer after confirmation. */
  remove(id: number): void {
    if (!confirm('Delete this customer? This cannot be undone.')) return;
    this.service.delete(id).subscribe(() => this.load());
  }

  /** Deterministic avatar color from the customer id. */
  avatarColor(id: number): string {
    const palette = [
      '#4f46e5', '#0ea5e9', '#10b981', '#f59e0b',
      '#ef4444', '#8b5cf6', '#ec4899', '#14b8a6',
    ];
    return palette[id % palette.length];
  }

  /** Formats a UTC date string for display. */
  formatDate(value?: string): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString();
  }
}
