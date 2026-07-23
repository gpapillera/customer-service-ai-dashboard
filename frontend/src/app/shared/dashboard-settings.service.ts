import { Injectable, signal } from '@angular/core';

export interface DashboardWidgetSettings {
  showKpiCards: boolean;
  showCharts: boolean;
  showRecentCases: boolean;
  showOverdueFollowups: boolean;
  showAgentWorkload: boolean;
}

const STORAGE_KEY = 'cs-dashboard-widgets';

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
};

/**
 * Manages per-widget visibility for the Dashboard page.
 * Persisted in localStorage under ``cs-dashboard-widgets``.
 */
@Injectable({ providedIn: 'root' })
export class DashboardSettingsService {
  readonly showKpiCards = signal(true);
  readonly showCharts = signal(true);
  readonly showRecentCases = signal(true);
  readonly showOverdueFollowups = signal(true);
  readonly showAgentWorkload = signal(true);

  constructor() {
    const s = loadSettings();
    this.showKpiCards.set(s.showKpiCards);
    this.showCharts.set(s.showCharts);
    this.showRecentCases.set(s.showRecentCases);
    this.showOverdueFollowups.set(s.showOverdueFollowups);
    this.showAgentWorkload.set(s.showAgentWorkload);
  }

  /** Persist current toggle states to localStorage. */
  private persist(): void {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        showKpiCards: this.showKpiCards(),
        showCharts: this.showCharts(),
        showRecentCases: this.showRecentCases(),
        showOverdueFollowups: this.showOverdueFollowups(),
        showAgentWorkload: this.showAgentWorkload(),
      }),
    );
  }

  toggleKpiCards(): void {
    this.showKpiCards.update((v) => !v);
    this.persist();
  }

  toggleCharts(): void {
    this.showCharts.update((v) => !v);
    this.persist();
  }

  toggleRecentCases(): void {
    this.showRecentCases.update((v) => !v);
    this.persist();
  }

  toggleOverdueFollowups(): void {
    this.showOverdueFollowups.update((v) => !v);
    this.persist();
  }

  toggleAgentWorkload(): void {
    this.showAgentWorkload.update((v) => !v);
    this.persist();
  }
}
