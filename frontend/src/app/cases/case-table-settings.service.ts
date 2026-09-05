import { Injectable, inject, signal, effect } from '@angular/core';
import { AuthService } from '../auth/auth.service';

/** All columns the Cases table can show, in the default order. */
export const CASE_COLUMNS = [
  'subject', 'customerName', 'categoryName', 'priority',
  'status', 'createdAtUtc', 'assignedToUserId', 'updatedAtUtc',
] as const;
export type CaseColumn = (typeof CASE_COLUMNS)[number];

/** Minimum allowed column width in px (resize can't go below this). */
export const MIN_COL_WIDTH = 64;

interface CaseTablePrefs {
  order: string[];
  widths: Record<string, number>;
}

const LEGACY_KEY = 'cs-case-cols'; // used only when no user is logged in

const defaultPrefs = (): CaseTablePrefs => ({
  order: [...CASE_COLUMNS],
  widths: {},
});

@Injectable({ providedIn: 'root' })
export class CaseTableSettingsService {
  /** Ordered list of column keys; drives both <thead> and <tbody>. */
  readonly columnOrder = signal<string[]>([...CASE_COLUMNS]);
  /** Per-column width in px; missing/absent key = auto (natural) width. */
  readonly columnWidths = signal<Record<string, number>>({});

  private readonly auth = inject(AuthService);

  constructor() {
    this.load();
    // Reload when the signed-in user changes (login / logout / switch) so
    // each user's layout is independent — one user's order/widths never
    // bleed into another's.
    effect(() => {
      this.auth.currentUser(); // subscribe to user changes
      this.load();
    }, { allowSignalWrites: true });
  }

  private storageKey(): string {
    const u = this.auth.currentUser();
    return u?.userName ? `cs-case-cols-${u.userName}` : LEGACY_KEY;
  }

  private load(): void {
    let raw: string | null = null;
    try { raw = localStorage.getItem(this.storageKey()); } catch { raw = null; }
    const prefs = this.parse(raw);
    this.columnOrder.set(prefs.order);
    this.columnWidths.set(prefs.widths);
  }

  /** Parse stored blob -> sanitized { order, widths }. Falls back to defaults. */
  private parse(raw: string | null): CaseTablePrefs {
    if (!raw) return defaultPrefs();
    try {
      const o = JSON.parse(raw);
      if (!o || typeof o !== 'object') return defaultPrefs();
      return { order: this.normalizeOrder(o.order), widths: this.normalizeWidths(o.widths) };
    } catch {
      return defaultPrefs();
    }
  }

  /** Keep only known columns, dedupe, preserve stored order, append missing. */
  private normalizeOrder(parsed: unknown): string[] {
    if (!Array.isArray(parsed)) return [...CASE_COLUMNS];
    const known = new Set<string>(CASE_COLUMNS);
    const seen = new Set<string>();
    const ordered: string[] = [];
    for (const k of parsed) {
      if (typeof k === 'string' && known.has(k) && !seen.has(k)) {
        seen.add(k);
        ordered.push(k);
      }
    }
    for (const c of CASE_COLUMNS) if (!seen.has(c)) ordered.push(c);
    return ordered;
  }

  /** Keep only known columns with finite, sane widths. */
  private normalizeWidths(parsed: unknown): Record<string, number> {
    const out: Record<string, number> = {};
    if (parsed && typeof parsed === 'object') {
      const w = parsed as Record<string, unknown>;
      for (const k of CASE_COLUMNS) {
        const v = w[k];
        if (typeof v === 'number' && isFinite(v) && v >= MIN_COL_WIDTH) {
          out[k] = Math.round(v);
        }
      }
    }
    return out;
  }

  /** Persist current order + widths for the current user. */
  persist(): void {
    try {
      localStorage.setItem(
        this.storageKey(),
        JSON.stringify({ order: this.columnOrder(), widths: this.columnWidths() }),
      );
    } catch { /* ignore quota / private-mode errors */ }
  }

  /** Set/replace one column's width (px) and persist. */
  setWidth(key: string, px: number): void {
    const w = Math.max(MIN_COL_WIDTH, Math.round(px));
    this.columnWidths.update((m) => ({ ...m, [key]: w }));
    this.persist();
  }

  /** Clear one column's custom width (back to auto) and persist. */
  clearWidth(key: string): void {
    this.columnWidths.update((m) => {
      const copy = { ...m };
      delete copy[key];
      return copy;
    });
    this.persist();
  }

  /** Restore default order + widths for the current user. */
  reset(): void {
    this.columnOrder.set([...CASE_COLUMNS]);
    this.columnWidths.set({});
    this.persist();
  }
}
