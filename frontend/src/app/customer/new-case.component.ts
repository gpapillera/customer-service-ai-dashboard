import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CATEGORIES } from '../shared/categories';
import { CustomerService } from './customer.service';

/**
 * Customer "New case" form. Posts to the customer-portal endpoint, which
 * attaches the customer id from the JWT and runs the shared AI-priority
 * prediction. The customer never sees priority/AI fields.
 */
@Component({
  selector: 'app-new-case',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    CsIconComponent,
  ],
  templateUrl: './new-case.component.html',
  styleUrl: './new-case.component.scss',
})
export class NewCaseComponent {
  private readonly fb = inject(FormBuilder);
  private readonly customerService = inject(CustomerService);
  private readonly router = inject(Router);

  readonly categories = CATEGORIES;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    subject: ['', [Validators.required, Validators.maxLength(300)]],
    description: [''],
    categoryId: [1, Validators.required],
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const { subject, description, categoryId } = this.form.getRawValue();
    this.customerService
      .createCase({ subject, description, categoryId })
      .subscribe({
        next: (created) => this.router.navigateByUrl(`/customer/cases/${created.id}`),
        error: () => {
          this.saving.set(false);
          this.error.set('We could not create your case. Please try again.');
        },
      });
  }
}
