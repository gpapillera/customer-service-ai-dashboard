import { AfterViewInit, Component, computed, inject, OnInit, QueryList, signal, ViewChildren } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink, Router } from '@angular/router';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartOptions, ChartEvent } from 'chart.js';
import { RevealDirective } from '../shared/reveal.directive';
import { CsIconComponent } from '../shared/cs-icon.component';
import { RouteLoadingService } from '../shared/route-loading.service';
import { DashboardService } from './dashboard.service';
import { Dashboard, RecentCase } from '../shared/models';
import { CATEGORIES } from '../shared/categories';

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
export class DashboardComponent implements OnInit, AfterViewInit {
  private readonly service = inject(DashboardService);
  private readonly routeLoading = inject(RouteLoadingService);
  private readonly router = inject(Router);

  readonly data = signal<Dashboard | null>(null);
  /** Internal data-fetch state. */
  private readonly dataLoading = signal(true);
  /** True while the dashboard is loading OR a route navigation is in progress. */
  readonly loading = computed(() => this.dataLoading() || this.routeLoading.loading());

  /** Chart.js directive instances, used to replay the entrance animation. */
  @ViewChildren(BaseChartDirective) private chartRefs!: QueryList<BaseChartDirective>;
  private entrancePlayed = false;

  readonly trendChart: ChartConfiguration<'line'> = {
    type: 'line',
    data: {
      labels: [],
      datasets: [{
        data: [],
        label: 'Cases created',
        borderColor: '#4f46e5',
        borderWidth: 1.5,
        backgroundColor: (ctx: any) => {
          const chart = ctx.chart;
          const { ctx: c, chartArea } = chart;
          if (!chartArea) return 'rgba(79, 70, 229, 0.12)';
          const gradient = c.createLinearGradient(0, chartArea.top, 0, chartArea.bottom);
          gradient.addColorStop(0, 'rgba(79, 70, 229, 0.4)');
          gradient.addColorStop(1, 'rgba(79, 70, 229, 0)');
          return gradient;
        },
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
        this.trendChart.data.labels = d.trend.map((t) => t.date);
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
        // Always show all 5 statuses (even when a count is 0) so no bar is
        // dropped from the x-axis. Use 0 for missing/absent statuses.
        const byStatus = d.byStatus ?? {};
        this.statusChart.data.labels = statusOrder;
        this.statusChart.data.datasets[0].data = statusOrder.map((s) => byStatus[s] ?? 0);
        this.statusChart.data.datasets[0].backgroundColor = statusOrder.map((s) => statusColors[s]);

        this.dataLoading.set(false);
        // Replay a clear entrance animation once all four chart canvases exist.
        this.tryPlayEntrance();
      },
      error: () => this.dataLoading.set(false),
    });
  }

  /** 6 KPI cards derived from the dashboard payload. */
  get kpis() {
    const d = this.data();
    if (!d) return [];
    return [
      { label: 'Total Cases', value: d.totalCases, icon: 'briefcase', tone: 'indigo', link: '/cases' },
      { label: 'Open Cases', value: d.openCases, icon: 'clock', tone: 'blue', link: '/cases?status=Open' },
      { label: 'High Priority', value: d.highPriorityCases, icon: 'priority_high', tone: 'red', link: '/cases?priority=High' },
      { label: 'Resolved', value: d.resolvedCases, icon: 'check_circle', tone: 'green', link: '/cases?status=Resolved' },
      { label: 'Customers', value: d.totalCustomers, icon: 'people', tone: 'indigo', link: '/customers' },
      { label: 'AI Predicted', value: d.aiPredictedCases, icon: 'auto_awesome', tone: 'purple', link: '/cases?aiOnly=true' },
    ];
  }

  /** Navigate to the page backing a KPI card (with pre-applied filters). */
  openKpi(link: string): void {
    this.router.navigateByUrl(link);
  }

  /**
   * Handles clicks on any dashboard chart. Maps the clicked element's index
   * back to its label and navigates to /cases with the matching filter —
   * same mapping as the KPI cards. The Weekly Trend chart has no per-day
   * filter yet, so any click there goes to unfiltered /cases.
   */
  onChartClick(which: 'trend' | 'priority' | 'category' | 'status', event: { event?: ChartEvent; active?: any[] }): void {
    const active = event?.active;
    if (!active || active.length === 0) return;
    const index = active[0].index;
    let params: Record<string, string> | undefined;
    if (which === 'priority') {
      const label = this.priorityChart.data.labels?.[index];
      if (label) params = { priority: String(label) };
    } else if (which === 'status') {
      const label = this.statusChart.data.labels?.[index];
      if (label) params = { status: String(label) };
    } else if (which === 'category') {
      const label = this.categoryChart.data.labels?.[index];
      // The case-list category filter is numeric (categoryId), so map the
      // chart's category name back to its id from the shared constant.
      const cat = CATEGORIES.find((c) => c.name === label);
      if (cat) params = { categoryId: String(cat.id) };
    }
    // 'trend' → no date filter supported yet; navigate unfiltered.
    this.router.navigate(['/cases'], { queryParams: params });
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

  /** Parse a 'YYYY-MM-DD' (or ISO) date string into a local Date, or null. */
  private static parseDate(s: string): Date | null {
    const datePart = s.split('T')[0];
    const parts = datePart.split('-').map(Number);
    if (parts.length < 3 || parts.some((n) => isNaN(n))) return null;
    const [y, m, day] = parts;
    return new Date(y, m - 1, day);
  }

  /** Short axis label, e.g. "Jul 13". */
  private static fmtShort(d: Date): string {
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }

  /** Long tooltip title, e.g. "Jul 13, 2026". */
  private static fmtLong(d: Date): string {
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  /** After the view (and all four chart directives) initialize, replay. */
  ngAfterViewInit(): void {
    this.tryPlayEntrance();
  }

  /** Force a visible "grow-in" entrance animation on all four charts. */
  private tryPlayEntrance(): void {
    if (this.entrancePlayed) return;
    const refs = this.chartRefs;
    // Wait until all four chart canvases are registered, otherwise the
    // later-created charts would render their final frame instantly.
    if (!refs || refs.length < 4) {
      setTimeout(() => this.tryPlayEntrance(), 30);
      return;
    }
    this.entrancePlayed = true;
    refs.forEach((ref) => {
      const chart = ref.chart as any;
      if (chart) {
        if (chart.config && chart.config.type === 'line') (window as any).__trendChart = chart;
        chart.reset();
        chart.update();
      }
    });
  }

  private lineOptions(): ChartOptions<'line'> {
    // "Life line" effect: the blue line traces in from left to right like a
    // heart-monitor, each point extending smoothly from the previous one.
    const totalDuration = 900;
    const points = 30; // weekly trend spans ~30 days
    const delayBetweenPoints = totalDuration / points;
    const previousY = (ctx: any) =>
      ctx.index === 0
        ? ctx.chart.scales.y.getPixelForValue(0)
        : ctx.chart.getDatasetMeta(ctx.datasetIndex).data[ctx.index - 1].getProps(['y'], true).y;
    return {
      responsive: true,
      maintainAspectRatio: false,
      animation: {
        x: {
          type: 'number',
          easing: 'linear',
          duration: delayBetweenPoints,
          from: NaN,
          delay(ctx: any) {
            if (ctx.type !== 'data' || ctx.xStarted) return 0;
            ctx.xStarted = true;
            return ctx.index * delayBetweenPoints;
          },
        },
        y: {
          type: 'number',
          easing: 'easeOutCubic',
          duration: delayBetweenPoints * 1.5,
          from: previousY,
          delay(ctx: any) {
            if (ctx.type !== 'data' || ctx.yStarted) return 0;
            ctx.yStarted = true;
            return ctx.index * delayBetweenPoints;
          },
        },
      } as any,
      plugins: {
        legend: { display: false },
        tooltip: {
          mode: 'index',
          intersect: false,
          callbacks: {
            title: (items: any) => {
              const label = items?.[0]?.label;
              const d = DashboardComponent.parseDate(String(label));
              return d ? DashboardComponent.fmtLong(d) : label;
            },
          },
        },
      },
      scales: {
        x: {
          grid: { display: false },
          ticks: {
            autoSkip: false,
            maxRotation: 0,
            minRotation: 0,
            // Use a regular function so `this` is the scale (which exposes
            // getLabelForValue). On a category axis `value` is the data index
            // (0..n), not the label, so resolve the real label first. We then
            // show only evenly-spaced indices (plus the first/last) so the
            // date labels are evenly distributed with consistent spacing.
            // autoSkip is disabled above so Chart.js doesn't drop the ticks
            // our callback wants to keep.
            callback(this: any, value: any): string {
              const label = this.getLabelForValue(value);
              const d = DashboardComponent.parseDate(String(label));
              if (!d) return String(label);
              const labels = this.chart?.data?.labels as unknown[] | undefined;
              const total = labels ? labels.length : 0;
              if (total <= 1) return DashboardComponent.fmtShort(d);
              const target = 7; // ~7 evenly spaced labels
              const step = Math.ceil((total - 1) / (target - 1));
              const isEdge = value === 0 || value === total - 1;
              const isStep = value % step === 0;
              return isEdge || isStep ? DashboardComponent.fmtShort(d) : '';
            },
          },
        },
        y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: 'rgba(0,0,0,0.05)' } },
      },
    };
  }

  private doughnutOptions(): ChartOptions<'doughnut'> {
    return {
      responsive: true,
      maintainAspectRatio: false,
      cutout: '68%',
      animation: { animateRotate: true, animateScale: true, duration: 900, easing: 'easeOutQuart' },
      plugins: {
        legend: {
          display: true,
          position: 'bottom',
          labels: {
            usePointStyle: true,
            pointStyle: 'circle',
            // Append the actual case count to each priority label, e.g.
            // "Low (4)". Reads the live dataset values so the counts stay
            // accurate instead of being hardcoded.
            generateLabels: (chart: any) => {
              const dataset = chart.data.datasets[0];
              const data = (dataset?.data as number[]) ?? [];
              const labels = (chart.data.labels as string[]) ?? [];
              const colors = (dataset?.backgroundColor as string[]) ?? [];
              return labels.map((label, i) => ({
                text: `${label} (${data[i] ?? 0})`,
                fillStyle: colors[i],
                strokeStyle: colors[i],
                lineWidth: 0,
                hidden: false,
                index: i,
              }));
            },
          } as any,
        },
      },
    };
  }

  private barOptions(horizontal: boolean): ChartOptions<'bar'> {
    return {
      responsive: true,
      maintainAspectRatio: false,
      indexAxis: horizontal ? 'y' : 'x',
      animation: { duration: 900, easing: 'easeOutQuart' },
      plugins: { legend: { display: false } },
      scales: {
        x: { beginAtZero: true, ticks: { precision: 0 }, grid: { display: !horizontal } },
        y: { beginAtZero: true, ticks: { precision: 0 }, grid: { display: horizontal } },
      },
    };
  }
}
