import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { CustomerCaseSummary } from '../shared/models';

/**
 * Customer "My Cases" list. Shows only the calling customer's own cases and
 * links into the detail view. No priority / AI / agent data is rendered.
 */
@Component({
  selector: 'app-my-cases-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatIconModule,
    CsIconComponent,
  ],
  templateUrl: './my-cases-list.component.html',
  styleUrl: './my-cases-list.component.scss',
})
export class MyCasesListComponent implements OnInit {
  private readonly customerService = inject(CustomerService);
  private readonly router = inject(Router);

  readonly cases = signal<CustomerCaseSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.customerService.listCases().subscribe({
      next: (list) => {
        this.cases.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('We could not load your cases. Please try again.');
      },
    });
  }

  open(id: number): void {
    this.router.navigateByUrl(`/customer/cases/${id}`);
  }

  newCase(): void {
    this.router.navigateByUrl('/customer/cases/new');
  }

  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  formatDate(value: string): string {
    return new Date(value).toLocaleString();
  }
}
