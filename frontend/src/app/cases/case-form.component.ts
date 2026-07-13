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
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CaseService } from './case.service';
import { CustomerService } from '../customers/customer.service';
import { Case, Customer } from '../shared/models';
import { CATEGORIES } from '../shared/categories';
import { ConfirmDialogComponent, ConfirmDialogData } from '../shared/confirm-dialog.component';

/** Dialog data accepted by the case form. */
export interface CaseFormDialogData {
  /** Case id when opened in edit mode. */
  caseId?: number;
  /** When set (create mode), the Customer field is prefilled and locked. */
  customerId?: number;
}

/**
 * Create / edit case form, rendered inside a MatDialog. On create, the
 * backend ML model suggests a priority (previewable on demand via the
 * "Get AI suggestion" action). On edit, the agent can override
 * priority/status. Closes the dialog with the saved case id. When opened
 * with `customerId` it prefills + locks the Customer field (used from the
 * Customer Detail page); otherwise the Customer field behaves as normal.
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
    MatDialogModule,
    CsIconComponent,
  ],
  templateUrl: './case-form.component.html',
  styleUrl: './case-form.component.scss',
})
export class CaseFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly caseService = inject(CaseService);
  private readonly customerService = inject(CustomerService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialogRef = inject(MatDialogRef<CaseFormComponent>);
  private readonly dialog = inject(MatDialog);
  /** Dialog data: optional case id (edit) and/or locked customer id (create). */
  private readonly dialogData = inject<CaseFormDialogData>(MAT_DIALOG_DATA, { optional: true }) ?? {};

  readonly categories = CATEGORIES;
  readonly customers = signal<Customer[]>([]);
  readonly isEdit = signal(false);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly suggestedPriority = signal<string | null>(null);
  readonly predicting = signal(false);
  readonly error = signal<string | null>(null);
  readonly deleting = signal(false);
  /** When set, the Customer field is prefilled and disabled. */
  readonly lockedCustomerId = signal<number | null>(this.dialogData.customerId ?? null);

  readonly form = this.fb.nonNullable.group({
    subject: ['', Validators.required],
    description: [''],
    categoryId: [null as number | null, Validators.required],
    customerId: [{ value: null as number | null, disabled: true }, Validators.required],
    status: ['New' as Case['status']],
    priority: ['Medium' as Case['priority']],
    useAiPriority: [true],
  });

  ngOnInit(): void {
    this.customerService.list().subscribe((list) => this.customers.set(list));
    const id = this.dialogData.caseId ?? this.route.snapshot.paramMap.get('id');
    const presetCustomer = this.route.snapshot.queryParamMap.get('customerId');
    if (presetCustomer) {
      this.form.patchValue({ customerId: Number(presetCustomer) });
    }
    if (this.lockedCustomerId()) {
      // Prefill + lock the Customer field to the given customer.
      this.form.patchValue({ customerId: this.lockedCustomerId() });
    } else {
      // Editable: enable the Customer select (e.g. opened from Cases List).
      this.form.controls.customerId.enable();
    }
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

  /** Previews an AI priority suggestion on demand (does not save). */
  getAiSuggestion(): void {
    const v = this.form.getRawValue();
    if (v.categoryId == null || v.customerId == null) {
      this.error.set('Select a customer and category first to preview AI priority.');
      return;
    }
    this.predicting.set(true);
    this.error.set(null);
    this.caseService.predictPriority({
      categoryId: v.categoryId,
      customerId: v.customerId,
      description: v.description,
    }).subscribe({
      next: (res) => {
        this.suggestedPriority.set(res.priority);
        this.form.patchValue({ priority: res.priority as Case['priority'] });
        this.predicting.set(false);
      },
      error: () => {
        this.predicting.set(false);
        this.error.set('Could not get AI suggestion.');
      },
    });
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
    const id = this.dialogData.caseId ?? this.route.snapshot.paramMap.get('id');

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
          next: () => this.dialogRef.close(Number(id)),
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
        next: (created) => this.dialogRef.close(created.id),
        error: () => this.fail(),
      });
  }

  /** Closes the dialog without saving. */
  cancel(): void {
    this.dialogRef.close(null);
  }

  /** Deletes the case after a confirmation dialog (edit mode only). */
  deleteCase(): void {
    const id = this.dialogData.caseId ?? this.route.snapshot.paramMap.get('id');
    if (!id) return;
    const ref: MatDialogRef<ConfirmDialogComponent, boolean> = this.dialog.open(
      ConfirmDialogComponent,
      {
        data: {
          title: 'Delete case',
          message: "Delete this case? This can't be undone.",
          confirmText: 'Delete',
          cancelText: 'Cancel',
          icon: 'delete',
        } as ConfirmDialogData,
        width: '400px',
        maxWidth: '92vw',
        autoFocus: false,
      },
    );
    ref.afterClosed().subscribe((confirmed) => {
      if (!confirmed) return;
      this.deleting.set(true);
      this.caseService.delete(Number(id)).subscribe({
        next: () => {
          // Close the case modal; the caller navigates to the Cases List.
          this.dialogRef.close({ deleted: true, id: Number(id) });
        },
        error: () => {
          this.deleting.set(false);
          this.error.set('Could not delete the case. Please try again.');
        },
      });
    });
  }

  /** Priority pill class for the AI suggestion preview. */
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }

  /** Sets the final priority from the segmented control. */
  setPriority(p: string): void {
    this.form.controls.priority.setValue(p as Case['priority']);
  }

  private fail(): void {
    this.saving.set(false);
    this.error.set('Could not save the case. Please try again.');
  }
}
