import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { UserService } from './user.service';
import { Agent } from '../shared/models';

/**
 * Admin-only, read-only list of Agent-role users with each agent's currently
 * open (not Resolved/Closed) case count. Phase 5 — no create/edit/delete.
 */
@Component({
  selector: 'app-agent-list',
  standalone: true,
  imports: [
    CommonModule,
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

  ngOnInit(): void {
    this.userService.agentsSummary().subscribe({
      next: (list) => {
        this.agents.set(list);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
