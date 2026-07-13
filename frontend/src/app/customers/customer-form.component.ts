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
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RevealDirective } from '../shared/reveal.directive';
import { CustomerService } from './customer.service';
import { Customer } from '../shared/models';

/**
 * Create / edit customer form, rendered inside a MatDialog (same modal
 * pattern as the case form). When opened with a customer `id` in the dialog
 * data it edits; otherwise it creates a new customer. Closes the dialog with
 * the saved customer id (or null when cancelled).
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
    MatDialogModule,
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
  private readonly dialogRef = inject(MatDialogRef<CustomerFormComponent>);
  /** Optional customer id when opened in edit mode (from dialog data or route). */
  private readonly dialogCustomerId = inject<number | undefined>(MAT_DIALOG_DATA, { optional: true });

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
    const id = this.dialogCustomerId ?? this.route.snapshot.paramMap.get('id');
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
    const id = this.dialogCustomerId ?? this.route.snapshot.paramMap.get('id');

    const onDone = (savedId: number) => this.dialogRef.close(savedId);
    const onErr = () => {
      this.saving.set(false);
      this.error.set('Could not save customer. Please try again.');
    };

    if (id) {
      this.service.update({ ...value, id: Number(id) }).subscribe({ next: () => onDone(Number(id)), error: onErr });
    } else {
      this.service.create(value).subscribe({ next: (c) => onDone(c.id), error: onErr });
    }
  }

  /** Closes the dialog without saving. */
  cancel(): void {
    this.dialogRef.close(null);
  }
}
