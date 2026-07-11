import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { Customer } from '../shared/models';

/**
 * Customer list with debounced search and quick actions (view / edit / delete).
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

  readonly customers = signal<Customer[]>([]);
  readonly loading = signal(true);
  readonly searchTerm = signal('');

  ngOnInit(): void {
    this.load();
  }

  /** Loads all customers (or searches when a term is present). */
  load(): void {
    this.loading.set(true);
    const term = this.searchTerm().trim();
    const req = term
      ? this.service.search(term)
      : this.service.list();
    req.subscribe({
      next: (list) => {
        this.customers.set(list);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** Debounced search trigger from the input. */
  onSearch(value: string): void {
    this.searchTerm.set(value);
    this.load();
  }

  /** Deletes a customer after confirmation. */
  remove(id: number): void {
    if (!confirm('Delete this customer? This cannot be undone.')) return;
    this.service.delete(id).subscribe(() => this.load());
  }
}
