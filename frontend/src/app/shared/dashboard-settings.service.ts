import { Injectable, inject, signal, effect } from '@angular/core';
import { AuthService } from '../auth/auth.service';

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

const LEGACY_KEY = 'cs-dashboard-widgets';

const DEFAULT_ORDER = ['kpis', 'charts', 'recent', 'overdue', 'workload'];

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
 * Persisted in localStorage under a user-scoped key
 * (``cs-dashboard-widgets-{userName}``) so admin and agent settings are
 * fully independent.
 */
@Injectable({ providedIn: 'root' })
export class DashboardSettingsService {
  readonly showKpiCards = signal(true);
  readonly showCharts = signal(true);
  readonly showRecentCases = signal(true);
  readonly showOverdueFollowups = signal(true);
  readonly showAgentWorkload = signal(true);
  readonly widgetOrder = signal<string[]>([...DEFAULT_ORDER]);

  private readonly auth = inject(AuthService);

  constructor() {
    this.loadSettings();

    // React to user changes (login/logout/switch) by reloading settings.
    effect(() => {
      this.auth.currentUser(); // subscribe to user changes
      this.loadSettings();
    }, { allowSignalWrites: true });
  }

  /** Build a user-scoped localStorage key. Falls back to the legacy
   *  unscoped key when no user is logged in. */
  private storageKey(): string {
    const user = this.auth.currentUser();
    return user?.userName ? `cs-dashboard-widgets-${user.userName}` : LEGACY_KEY;
  }

  /** Load settings from localStorage (scoped to current user).
   *  Migrates from the legacy unscoped key on first access per user. */
  private loadSettings(): void {
    const key = this.storageKey();
    let raw = localStorage.getItem(key);

    // Migrate from the legacy unscoped key (if a user is logged in).
    if (raw === null && this.auth.currentUser()) {
      const old = localStorage.getItem(LEGACY_KEY);
      if (old) {
        raw = old;
        localStorage.setItem(key, old);
        localStorage.removeItem(LEGACY_KEY);
      }
    }

    let s: DashboardWidgetSettings;
    if (raw) {
      try {
        s = { ...defaults, ...JSON.parse(raw) };
      } catch {
        s = { ...defaults };
      }
    } else {
      s = { ...defaults };
    }

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
      this.storageKey(),
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
