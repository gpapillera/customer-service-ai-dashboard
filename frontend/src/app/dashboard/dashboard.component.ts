import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartOptions } from 'chart.js';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { DashboardService } from './dashboard.service';
import { Dashboard } from '../shared/models';

/**
 * Dashboard: KPI cards (total / open / high-priority / customers) plus a
 * 30-day trend line chart and a by-category bar chart, wired to the API.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
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
    data: { labels: [], datasets: [{ data: [], label: 'Cases created', fill: true, tension: 0.35 }] },
    options: this.baseOptions() as ChartConfiguration<'line'>['options'],
  };

  readonly categoryChart: ChartConfiguration<'bar'> = {
    type: 'bar',
    data: { labels: [], datasets: [{ data: [], label: 'Cases' }] },
    options: this.baseOptions(false) as ChartConfiguration<'bar'>['options'],
  };

  ngOnInit(): void {
    this.service.get().subscribe({
      next: (d) => {
        this.data.set(d);
        this.trendChart.data.labels = d.trend.map((t) => t.date.slice(5));
        this.trendChart.data.datasets[0].data = d.trend.map((t) => t.count);
        this.categoryChart.data.labels = d.byCategory.map((c) => c.category);
        this.categoryChart.data.datasets[0].data = d.byCategory.map((c) => c.count);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** KPI cards derived from the dashboard payload. */
  get kpis() {
    const d = this.data();
    if (!d) return [];
    return [
      { label: 'Total cases', value: d.totalCases, icon: 'confirmation_number', tone: 'blue' },
      { label: 'Open cases', value: d.openCases, icon: 'folder_open', tone: 'amber' },
      { label: 'High priority', value: d.highPriorityCases, icon: 'priority_high', tone: 'red' },
      { label: 'Customers', value: d.totalCustomers, icon: 'people', tone: 'green' },
    ];
  }

  private baseOptions(legend = true): ChartOptions {
    return {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: legend },
      },
      scales: {
        x: { grid: { display: false } },
        y: { beginAtZero: true, ticks: { precision: 0 } },
      },
    };
  }
}
