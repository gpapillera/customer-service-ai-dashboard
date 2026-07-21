import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { CustomerFormComponent } from './customer-form.component';
import { CaseFormComponent, CaseFormDialogData } from '../cases/case-form.component';
import { Customer, Case } from '../shared/models';
import { AuthService } from '../auth/auth.service';

/**
 * Customer detail view: profile info plus the customer's case history.
 * "Edit" opens the customer form as a modal and refreshes the view in place.
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
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  readonly auth = inject(AuthService);

  readonly customer = signal<Customer | null>(null);
  readonly cases = signal<Case[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  /** Loads the customer and their case history. */
  private load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loading.set(true);
    this.service.get(id).subscribe({
      next: (c) => {
        this.customer.set(c);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
    this.loadCases();
  }

  /** Opens the new-case modal directly on this page, locked to this customer. */
  newCase(): void {
    const id = this.customer()?.id;
    if (!id) return;
    const data: CaseFormDialogData = { customerId: id };
    const ref = this.dialog.open(CaseFormComponent, {
      data,
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((savedId) => {
      if (savedId) this.loadCases();
    });
  }

  /** Reloads only the case history for this customer (in place). */
  private loadCases(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.service.customerCases(id).subscribe((list) => {
      this.cases.set(list);
    });
  }

  /** Opens the edit-customer modal, prefilled, and refreshes in place on save. */
  edit(): void {
    const id = this.customer()?.id;
    if (!id) return;
    const ref = this.dialog.open(CustomerFormComponent, {
      data: id,
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((savedId) => {
      if (savedId) this.load();
    });
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
