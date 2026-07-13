import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartOptions } from 'chart.js';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { DashboardService } from './dashboard.service';
import { Dashboard, RecentCase } from '../shared/models';

/**
 * Dashboard: 6 KPI cards, weekly trend line, priority donut, horizontal
 * category bar, status bar chart, and a recent-cases list — all wired to the
 * API.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatButtonModule,
    RouterLink,
    BaseChartDirective,
    RevealDirective,
    CsIconComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly service = inject(DashboardService);

  readonly data = signal<Dashboard | null>(null);
  readonly loading = signal(true);

  readonly trendChart: ChartConfiguration<'line'> = {
    type: 'line',
    data: {
      labels: [],
      datasets: [{
        data: [],
        label: 'Cases created',
        borderColor: '#4f46e5',
        backgroundColor: 'rgba(79, 70, 229, 0.12)',
        fill: true,
        tension: 0.4,
        pointRadius: 0,
        pointHoverRadius: 5,
      }],
    },
    options: this.lineOptions(),
  };

  readonly priorityChart: ChartConfiguration<'doughnut'> = {
    type: 'doughnut',
    data: {
      labels: ['Low', 'Medium', 'High'],
      datasets: [{
        data: [0, 0, 0],
        backgroundColor: ['#10b981', '#f59e0b', '#ef4444'],
        borderWidth: 0,
        hoverOffset: 6,
      }],
    },
    options: this.doughnutOptions(),
  };

  readonly categoryChart: ChartConfiguration<'bar'> = {
    type: 'bar',
    data: { labels: [], datasets: [{ data: [], label: 'Cases', backgroundColor: '#4f46e5', borderRadius: 6 }] },
    options: this.barOptions(true),
  };

  readonly statusChart: ChartConfiguration<'bar'> = {
    type: 'bar',
    data: { labels: [], datasets: [{ data: [], backgroundColor: [] }] },
    options: this.barOptions(false),
  };

  ngOnInit(): void {
    this.service.get().subscribe({
      next: (d) => {
        this.data.set(d);
        this.trendChart.data.labels = d.trend.map((t) => t.date.slice(5));
        this.trendChart.data.datasets[0].data = d.trend.map((t) => t.count);

        this.priorityChart.data.datasets[0].data = [
          d.byPriority['Low'] ?? 0,
          d.byPriority['Medium'] ?? 0,
          d.byPriority['High'] ?? 0,
        ];

        this.categoryChart.data.labels = d.byCategory.map((c) => c.category);
        this.categoryChart.data.datasets[0].data = d.byCategory.map((c) => c.count);

        const statusOrder = ['New', 'InProgress', 'Escalated', 'Resolved', 'Closed'];
        const statusColors: Record<string, string> = {
          New: '#3b82f6', InProgress: '#4f46e5', Escalated: '#ef4444',
          Resolved: '#10b981', Closed: '#94a3b8',
        };
        const labels = statusOrder.filter((s) => s in (d.byStatus ?? {}));
        this.statusChart.data.labels = labels;
        this.statusChart.data.datasets[0].data = labels.map((s) => d.byStatus[s]);
        this.statusChart.data.datasets[0].backgroundColor = labels.map((s) => statusColors[s]);

        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** 6 KPI cards derived from the dashboard payload. */
  get kpis() {
    const d = this.data();
    if (!d) return [];
    return [
      { label: 'Total Cases', value: d.totalCases, icon: 'briefcase', tone: 'indigo' },
      { label: 'Open Cases', value: d.openCases, icon: 'clock', tone: 'blue' },
      { label: 'High Priority', value: d.highPriorityCases, icon: 'priority_high', tone: 'red' },
      { label: 'Resolved', value: d.resolvedCases, icon: 'check_circle', tone: 'green' },
      { label: 'Customers', value: d.totalCustomers, icon: 'people', tone: 'indigo' },
      { label: 'AI Predicted', value: d.aiPredictedCases, icon: 'auto_awesome', tone: 'purple' },
    ];
  }

  /** Recent cases for the bottom list. */
  get recentCases(): RecentCase[] {
    return this.data()?.recentCases ?? [];
  }

  /** Helper for the template: status pill class. */
  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  /** Helper for the template: priority pill class. */
  priorityClass(p: string): string {
    return 'priority-' + p.toLowerCase();
  }

  private lineOptions(): ChartOptions<'line'> {
    return {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false }, tooltip: { mode: 'index', intersect: false } },
      scales: {
        x: { grid: { display: false } },
        y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: 'rgba(0,0,0,0.05)' } },
      },
    };
  }

  private doughnutOptions(): ChartOptions<'doughnut'> {
    return {
      responsive: true,
      maintainAspectRatio: false,
      cutout: '68%',
      plugins: {
        legend: { display: true, position: 'bottom' },
      },
    };
  }

  private barOptions(horizontal: boolean): ChartOptions<'bar'> {
    return {
      responsive: true,
      maintainAspectRatio: false,
      indexAxis: horizontal ? 'y' : 'x',
      plugins: { legend: { display: false } },
      scales: {
        x: { beginAtZero: true, ticks: { precision: 0 }, grid: { display: !horizontal } },
        y: { beginAtZero: true, ticks: { precision: 0 }, grid: { display: horizontal } },
      },
    };
  }
}
