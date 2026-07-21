import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { UserService } from './user.service';
import { Agent, UpdateAgent } from '../shared/models';
import { Dashboard } from '../shared/models';

/**
 * Admin-only list of Agent-role users with open-case counts. Clicking a card
 * opens an overlay showing editable name/email fields, open-case count, and the
 * full KPI set sourced from the shared dashboard service (Phase 11).
 */
@Component({
  selector: 'app-agent-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './agent-list.component.html',
  styleUrl: './agent-list.component.scss',
})
export class AgentListComponent implements OnInit {
  private readonly userService = inject(UserService);

  readonly agents = signal<Agent[]>([]);
  readonly loading = signal(true);

  // ── Agent detail overlay state ──
  readonly selected = signal<Agent | null>(null);
  readonly kpis = signal<Dashboard | null>(null);
  readonly editing = signal(false);
  readonly draft = signal<UpdateAgent>({ fullName: '', email: '' });
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.loadAgents();
  }

  private loadAgents(): void {
    this.userService.agentsSummary().subscribe({
      next: (list) => {
        this.agents.set(list);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** Opens the detail overlay for the clicked agent. */
  openAgent(agent: Agent): void {
    this.selected.set(agent);
    this.editing.set(false);
    this.error.set(null);
    this.draft.set({ fullName: agent.fullName, email: agent.email });
    this.kpis.set(null);
    this.userService.getAgentKpis(agent.id).subscribe({
      next: (d) => this.kpis.set(d),
      error: () => this.error.set('Could not load KPIs.'),
    });
  }

  /** Closes the overlay. */
  close(): void {
    this.selected.set(null);
  }

  /** Enters edit mode. */
  startEdit(): void {
    const a = this.selected();
    if (!a) return;
    this.draft.set({ fullName: a.fullName, email: a.email });
    this.editing.set(true);
    this.error.set(null);
  }

  /** Cancels edit mode. */
  cancelEdit(): void {
    this.editing.set(false);
    this.error.set(null);
  }

  /** Saves the edited agent profile. */
  save(): void {
    const a = this.selected();
    if (!a) return;
    this.saving.set(true);
    this.error.set(null);
    this.userService.updateAgent(a.id, this.draft()).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        // Refresh the agents list + selected agent data
        this.loadAgents();
        this.openAgent({ ...a, ...this.draft() });
      },
      error: (err) => {
        this.saving.set(false);
        const msg = err?.error?.error ?? err?.error?.title as string | undefined;
        this.error.set(msg ?? 'Could not save changes.');
      },
    });
  }

  /** Agent KPIs for the detail overlay, rendered as the same card pattern as the dashboard. */
  get agentKpis(): { label: string; value: number; icon: string; tone: string }[] {
    const d = this.kpis();
    if (!d) return [];
    return [
      { label: 'My Cases', value: d.myCases, icon: 'briefcase', tone: 'indigo' },
      { label: 'My Open', value: d.myOpenCases, icon: 'clock', tone: 'blue' },
      { label: 'My High Priority', value: d.myHighPriorityCases, icon: 'priority_high', tone: 'red' },
      { label: 'My Resolved', value: d.myResolvedCases, icon: 'check_circle', tone: 'green' },
      { label: 'My AI Predicted', value: d.myAiPredictedCases, icon: 'auto_awesome', tone: 'purple' },
      { label: 'My Overdue', value: d.myOverdueFollowUps, icon: 'schedule', tone: 'amber' },
    ];
  }
}
