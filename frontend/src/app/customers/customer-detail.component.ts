import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { Customer } from '../shared/models';

/**
 * Customer detail view: profile info plus a link to the customer's cases.
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
  private readonly route = inject(ActivatedRoute);

  readonly customer = signal<Customer | null>(null);
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
  }
}
