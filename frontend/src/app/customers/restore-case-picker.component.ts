import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { Case } from '../shared/models';

/** Data passed into the restore-case-picker dialog. */
export interface RestoreCasePickerData {
  customerId: number;
  customerName: string;
}

/**
 * Account-restore helper: when an admin restores a soft-deleted customer, this
 * dialog lists the customer's binned cases and lets them pick which ones to
 * bring back. All are selected by default. Returning an empty array means
 * "restore none of the cases" (customer account only — matches the backend
 * contract), returning `null` means cancel.
 */
@Component({
  selector: 'app-restore-case-picker',
  standalone: true,
  imports: [
    CommonModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    CsIconComponent,
  ],
  templateUrl: './restore-case-picker.component.html',
  styleUrl: './restore-case-picker.component.scss',
})
export class RestoreCasePickerComponent {
  private readonly dialogRef = inject(MatDialogRef<RestoreCasePickerComponent>);
  private readonly service = inject(CustomerService);
  readonly data = inject<RestoreCasePickerData>(MAT_DIALOG_DATA);

  /** Binned cases loaded for this customer. */
  readonly cases = signal<Case[]>([]);
  readonly loading = signal(true);
  /** Set of selected case ids (default: all). */
  private readonly selected = signal<Set<number>>(new Set());

  constructor() {
    this.service.customerDeletedCases(this.data.customerId).subscribe({
      next: (list) => {
        this.cases.set(list);
        this.selected.set(new Set(list.map((c) => c.id)));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** True when every binned case is selected. */
  readonly allSelected = computed(() => {
    const ids = this.cases().map((c) => c.id);
    const sel = this.selected();
    return ids.length > 0 && ids.every((id) => sel.has(id));
  });

  toggle(id: number, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) next.add(id);
    else next.delete(id);
    this.selected.set(next);
  }

  toggleAll(checked: boolean): void {
    this.selected.set(checked ? new Set(this.cases().map((c) => c.id)) : new Set());
  }

  isChecked(id: number): boolean {
    return this.selected().has(id);
  }

  /** Confirm: return the chosen ids (empty = restore all). */
  confirm(): void {
    this.dialogRef.close([...this.selected()]);
  }

  /** Cancel: null signals the caller to abort the restore. */
  cancel(): void {
    this.dialogRef.close(null);
  }

  formatDate(value?: string | null): string {
    if (!value) return '—';
    return new Date(value).toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }
}
