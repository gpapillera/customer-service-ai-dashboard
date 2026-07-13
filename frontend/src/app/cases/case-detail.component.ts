import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink, Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CaseService } from './case.service';
import { CallLogService } from './call-log.service';
import { Case, CallLog } from '../shared/models';

/**
 * Case detail: shows the case, its AI-suggested priority, and the call/follow-up
 * log with an inline form to add new entries.
 */
@Component({
  selector: 'app-case-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './case-detail.component.html',
  styleUrl: './case-detail.component.scss',
})
export class CaseDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  readonly router = inject(Router);
  private readonly caseService = inject(CaseService);
  private readonly callLogService = inject(CallLogService);
  private readonly fb = inject(FormBuilder);

  readonly case = signal<Case | null>(null);
  readonly logs = signal<CallLog[]>([]);
  readonly loading = signal(true);

  readonly statuses: Case['status'][] = ['New', 'InProgress', 'Escalated', 'Resolved', 'Closed'];
  readonly priorities: Case['priority'][] = ['Low', 'Medium', 'High'];

  readonly logForm = this.fb.nonNullable.group({
    direction: ['Outbound' as CallLog['direction']],
    notes: ['', Validators.required],
    durationSeconds: [0],
  });
  readonly savingLog = signal(false);

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.caseService.get(id).subscribe({
      next: (c) => {
        this.case.set(c);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
    this.callLogService.listByCase(id).subscribe((logs) => this.logs.set(logs));
  }

  /** Adds a call / follow-up log to the case. */
  addLog(): void {
    if (this.logForm.invalid) {
      this.logForm.markAllAsTouched();
      return;
    }
    const id = this.case()?.id;
    if (!id) return;
    this.savingLog.set(true);
    const v = this.logForm.getRawValue();
    this.callLogService
      .create({ caseId: id, direction: v.direction, notes: v.notes, durationSeconds: v.durationSeconds })
      .subscribe({
        next: (log) => {
          this.logs.update((l) => [...l, log]);
          this.logForm.reset({ direction: 'Outbound', notes: '', durationSeconds: 0 });
          this.savingLog.set(false);
        },
        error: () => this.savingLog.set(false),
      });
  }

  /** Updates the case status immediately from the side card. */
  updateStatus(status: Case['status']): void {
    const c = this.case();
    if (!c || c.status === status) return;
    this.caseService
      .update(c.id, {
        subject: c.subject,
        description: c.description,
        status,
        priority: c.priority,
        categoryId: c.categoryId,
        assignedToUserId: null,
      })
      .subscribe(() => this.case.set({ ...c, status }));
  }

  /** Updates the case priority immediately from the side card. */
  updatePriority(priority: Case['priority']): void {
    const c = this.case();
    if (!c || c.priority === priority) return;
    this.caseService
      .update(c.id, {
        subject: c.subject,
        description: c.description,
        status: c.status,
        priority,
        categoryId: c.categoryId,
        assignedToUserId: null,
      })
      .subscribe(() => this.case.set({ ...c, priority, priorityAutoSuggested: false }));
  }

  /** Opens the edit-case modal. */
  edit(): void {
    this.router.navigateByUrl(`/cases/${this.case()?.id}/edit`);
  }

  /** Status pill class. */
  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  /** Priority pill class. */
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }

  /** Formats a UTC date string for display. */
  formatDate(value: string): string {
    return new Date(value).toLocaleString();
  }
}
