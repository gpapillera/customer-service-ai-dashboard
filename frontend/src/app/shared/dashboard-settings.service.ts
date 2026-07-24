import { Injectable, signal } from '@angular/core';

export interface DashboardWidgetSettings {
  showKpiCards: boolean;
  showCharts: boolean;
  showRecentCases: boolean;
  showOverdueFollowups: boolean;
  showAgentWorkload: boolean;
  widgetOrder: string[];
}

export const WIDGET_LABELS: Record<string, string> = {
  kpis: 'KPI Cards',
  charts: 'Charts',
  recent: 'Recent Cases',
  overdue: 'Overdue Follow-ups',
  workload: 'Agent Workload',
};

const STORAGE_KEY = 'cs-dashboard-widgets';

const DEFAULT_ORDER = ['kpis', 'charts', 'recent', 'overdue', 'workload'];

function loadSettings(): DashboardWidgetSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return { ...defaults, ...JSON.parse(raw) };
  } catch { /* ignore corrupt data */ }
  return { ...defaults };
}

const defaults: DashboardWidgetSettings = {
  showKpiCards: true,
  showCharts: true,
  showRecentCases: true,
  showOverdueFollowups: true,
  showAgentWorkload: true,
  widgetOrder: [...DEFAULT_ORDER],
};

/**
 * Manages per-widget visibility and ordering for the Dashboard page.
 * Persisted in localStorage under ``cs-dashboard-widgets``.
 */
@Injectable({ providedIn: 'root' })
export class DashboardSettingsService {
  readonly showKpiCards = signal(true);
  readonly showCharts = signal(true);
  readonly showRecentCases = signal(true);
  readonly showOverdueFollowups = signal(true);
  readonly showAgentWorkload = signal(true);
  readonly widgetOrder = signal<string[]>([...DEFAULT_ORDER]);

  constructor() {
    const s = loadSettings();
    this.showKpiCards.set(s.showKpiCards);
    this.showCharts.set(s.showCharts);
    this.showRecentCases.set(s.showRecentCases);
    this.showOverdueFollowups.set(s.showOverdueFollowups);
    this.showAgentWorkload.set(s.showAgentWorkload);
    this.widgetOrder.set(s.widgetOrder ?? [...DEFAULT_ORDER]);
  }

  /** Persist current toggle states and order to localStorage. */
  private persist(): void {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        showKpiCards: this.showKpiCards(),
        showCharts: this.showCharts(),
        showRecentCases: this.showRecentCases(),
        showOverdueFollowups: this.showOverdueFollowups(),
        showAgentWorkload: this.showAgentWorkload(),
        widgetOrder: this.widgetOrder(),
      }),
    );
  }

  /** Move a widget from one index to another (drag-drop reorder). */
  moveWidget(fromIndex: number, toIndex: number): void {
    this.widgetOrder.update((order: string[]) => {
      const updated = [...order];
      const [moved] = updated.splice(fromIndex, 1);
      updated.splice(toIndex, 0, moved);
      return updated;
    });
    this.persist();
  }

  toggleKpiCards(): void {
    this.showKpiCards.update((v: boolean) => !v);
    this.persist();
  }

  toggleCharts(): void {
    this.showCharts.update((v: boolean) => !v);
    this.persist();
  }

  toggleRecentCases(): void {
    this.showRecentCases.update((v: boolean) => !v);
    this.persist();
  }

  toggleOverdueFollowups(): void {
    this.showOverdueFollowups.update((v: boolean) => !v);
    this.persist();
  }

  toggleAgentWorkload(): void {
    this.showAgentWorkload.update((v: boolean) => !v);
    this.persist();
  }
}
