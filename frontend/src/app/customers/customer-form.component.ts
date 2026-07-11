import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RevealDirective } from '../shared/reveal.directive';
import { CustomerService } from './customer.service';
import { Customer } from '../shared/models';

/**
 * Create / edit customer form. When the route has an `:id`, it loads and
 * edits; otherwise it creates a new customer.
 */
@Component({
  selector: 'app-customer-form',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatIconModule,
    CsIconComponent,
    RevealDirective,
  ],
  templateUrl: './customer-form.component.html',
  styleUrl: './customer-form.component.scss',
})
export class CustomerFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(CustomerService);
  private readonly route = inject(ActivatedRoute);
  readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    phone: ['' as string],
    company: ['' as string],
    address: ['' as string],
  });

  readonly isEdit = signal(false);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEdit.set(true);
      this.loading.set(true);
      this.service.get(Number(id)).subscribe({
        next: (c: Customer) => {
          this.form.patchValue({
            name: c.name,
            email: c.email,
            phone: c.phone ?? '',
            company: c.company ?? '',
            address: c.address ?? '',
          });
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
    }
  }

  /** Submits the form, creating or updating the customer. */
  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const value = this.form.getRawValue();
    const id = this.route.snapshot.paramMap.get('id');

    const onDone = () => this.router.navigateByUrl('/customers');
    const onErr = () => {
      this.saving.set(false);
      this.error.set('Could not save customer. Please try again.');
    };

    if (id) {
      this.service.update({ ...value, id: Number(id) }).subscribe({ next: onDone, error: onErr });
    } else {
      this.service.create(value).subscribe({ next: onDone, error: onErr });
    }
  }
}
