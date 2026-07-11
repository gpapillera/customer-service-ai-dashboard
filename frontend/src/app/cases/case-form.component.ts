import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RevealDirective } from '../shared/reveal.directive';
import { CaseService } from './case.service';
import { CustomerService } from '../customers/customer.service';
import { Case, Customer } from '../shared/models';
import { CATEGORIES } from '../shared/categories';

/**
 * Create / edit case form. On create, the backend ML model suggests a
 * priority (shown when the agent leaves priority blank). On edit, the agent
 * can override priority/status.
 */
@Component({
  selector: 'app-case-form',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatIconModule,
    MatSlideToggleModule,
    CsIconComponent,
    RevealDirective,
  ],
  templateUrl: './case-form.component.html',
  styleUrl: './case-form.component.scss',
})
export class CaseFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly caseService = inject(CaseService);
  private readonly customerService = inject(CustomerService);
  private readonly route = inject(ActivatedRoute);
  readonly router = inject(Router);

  readonly categories = CATEGORIES;
  readonly customers = signal<Customer[]>([]);
  readonly isEdit = signal(false);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly suggestedPriority = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    subject: ['', Validators.required],
    description: [''],
    categoryId: [null as number | null, Validators.required],
    customerId: [null as number | null, Validators.required],
    status: ['Open' as Case['status']],
    priority: ['Medium' as Case['priority']],
    useAiPriority: [true],
  });

  ngOnInit(): void {
    this.customerService.list().subscribe((list) => this.customers.set(list));
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEdit.set(true);
      this.loading.set(true);
      this.caseService.get(Number(id)).subscribe({
        next: (c) => {
          this.form.patchValue({
            subject: c.subject,
            description: c.description,
            categoryId: c.categoryId,
            customerId: c.customerId,
            status: c.status,
            priority: c.priority,
          });
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
    }
  }

  /** Submits the form, creating or updating the case. */
  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    const id = this.route.snapshot.paramMap.get('id');

    if (id) {
      this.caseService
        .update(Number(id), {
          subject: v.subject,
          description: v.description,
          status: v.status,
          priority: v.priority,
          categoryId: v.categoryId!,
          assignedToUserId: null,
        })
        .subscribe({
          next: () => this.router.navigateByUrl(`/cases/${id}`),
          error: () => this.fail(),
        });
      return;
    }

    // Create: let the backend suggest priority when the toggle is on.
    const priority = v.useAiPriority ? null : v.priority;
    this.caseService
      .create({
        subject: v.subject,
        description: v.description,
        categoryId: v.categoryId!,
        customerId: v.customerId!,
        priority,
      })
      .subscribe({
        next: (created) => this.router.navigateByUrl(`/cases/${created.id}`),
        error: () => this.fail(),
      });
  }

  private fail(): void {
    this.saving.set(false);
    this.error.set('Could not save the case. Please try again.');
  }
}
