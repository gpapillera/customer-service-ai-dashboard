import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { CaseService } from '../cases/case.service';
import { Customer, Case } from '../shared/models';

/**
 * Customer detail view: profile info plus the customer's case history.
 */
@Component({
  selector: 'app-customer-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './customer-detail.component.html',
  styleUrl: './customer-detail.component.scss',
})
export class CustomerDetailComponent implements OnInit {
  private readonly service = inject(CustomerService);
  private readonly caseService = inject(CaseService);
  private readonly route = inject(ActivatedRoute);
  readonly router = inject(Router);

  readonly customer = signal<Customer | null>(null);
  readonly cases = signal<Case[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.service.get(id).subscribe({
      next: (c) => {
        this.customer.set(c);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
    this.caseService.list({}).subscribe((list) => {
      this.cases.set(list.filter((c) => c.customerId === id));
    });
  }

  /** Opens the new-case modal pre-targeted at this customer. */
  newCase(): void {
    const id = this.customer()?.id;
    if (id) this.router.navigateByUrl(`/cases/new?customerId=${id}`);
  }

  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }
  formatDate(value?: string): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString();
  }
}
