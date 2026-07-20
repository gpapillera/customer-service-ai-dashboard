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
import { MatDialog } from '@angular/material/dialog';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CaseService } from './case.service';
import { CallLogService } from './call-log.service';
import { CaseFormComponent, CaseFormDialogData } from './case-form.component';
import { Case, CallLog, Agent } from '../shared/models';

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
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);

  readonly case = signal<Case | null>(null);
  readonly logs = signal<CallLog[]>([]);
  readonly loading = signal(true);
  /** Sentinel sent to the backend to explicitly clear the assignee. */
  private readonly unassignSentinel = '__unassign__';
  readonly unassigning = signal(false);
  /** Agents available for assignment (GET /api/users). */
  readonly agents = signal<Agent[]>([]);
  readonly assigning = signal(false);

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
    this.caseService.agents().subscribe((list) => this.agents.set(list));
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

  /** Explicitly unassigns the case (sends the unassign sentinel). */
  unassign(): void {
    const c = this.case();
    if (!c || !c.assignedToUserId) return;
    this.unassigning.set(true);
    this.caseService
      .update(c.id, {
        subject: c.subject,
        description: c.description,
        status: c.status,
        priority: c.priority,
        categoryId: c.categoryId,
        assignedToUserId: this.unassignSentinel,
      })
      .subscribe({
        next: () => {
          this.case.set({ ...c, assignedToUserId: null, assignedToUserName: null });
          this.unassigning.set(false);
        },
        error: () => this.unassigning.set(false),
      });
  }

  /** Assigns (or reassigns) the case to the chosen agent via the existing
      update path. Sends the selected agent id explicitly so the backend sets
      the assignee; the null-preservation logic leaves every other field
      untouched (re-verifies the earlier data-loss fix). */
  assignTo(agentId: string | null): void {
    const c = this.case();
    if (!c) return;
    // No-op when the selection matches the current assignee.
    if ((agentId ?? null) === (c.assignedToUserId ?? null)) return;
    this.assigning.set(true);
    this.caseService
      .update(c.id, {
        subject: c.subject,
        description: c.description,
        status: c.status,
        priority: c.priority,
        categoryId: c.categoryId,
        assignedToUserId: agentId,
      })
      .subscribe({
        next: () => {
          const name = this.agents().find((a) => a.id === agentId)?.fullName ?? null;
          this.case.set({ ...c, assignedToUserId: agentId, assignedToUserName: name });
          this.assigning.set(false);
        },
        error: () => this.assigning.set(false),
      });
  }

  /** Opens the edit-case modal directly; navigates to Cases List if deleted. */
  edit(): void {
    const id = this.case()?.id;
    if (!id) return;
    const data: CaseFormDialogData = { caseId: id };
    const ref = this.dialog.open(CaseFormComponent, {
      data,
      width: '560px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((result) => {
      if (result && (result as { deleted?: boolean }).deleted) {
        // The case no longer exists — go back to the list.
        this.router.navigateByUrl('/cases');
      }
    });
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
